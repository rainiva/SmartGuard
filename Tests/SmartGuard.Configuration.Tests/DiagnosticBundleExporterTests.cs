using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class DiagnosticBundleExporterTests
{
  [Fact]
  public void SanitizeConfigJson_removes_GitHubToken_and_GitHubTokenProtected()
  {
    const string token = "ghp_secretExportToken1234567890";
    var protectedValue = GitHubTokenProtector.Protect(token)!;
    var json = $$"""
      {
        "BalancedThresholdSec": 300,
        "GitHubToken": "{{token}}",
        "GitHubTokenProtected": "{{protectedValue}}"
      }
      """;

    var sanitized = DiagnosticBundleExporter.SanitizeConfigJson(json);

    sanitized.Should().NotContain("GitHubToken");
    sanitized.Should().NotContain("GitHubTokenProtected");
    sanitized.Should().NotContain(token);
    sanitized.Should().Contain("BalancedThresholdSec");
  }

  [Fact]
  public void Export_writes_manifest_config_status_and_logs_with_sanitized_config()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");
    const string token = "ghp_bundleExportToken1234567890";

    try
    {
      var configPath = SmartGuardPaths.ConfigFile(dir);
      var repo = new GuardConfigRepository(configPath);
      var config = GuardConfig.CreateDefault(dir);
      config.GitHubToken = token;
      repo.Save(config);

      var statusPath = SmartGuardPaths.StatusFile(dir);
      File.WriteAllText(statusPath, """{"paused":false,"idleSeconds":42}""");

      var logPath = SmartGuardPaths.DefaultLogFile(dir);
      File.WriteAllText(logPath, "line-one\nline-two\n");

      var startupLogPath = SmartGuardPaths.StartupLogFile(dir);
      File.WriteAllText(startupLogPath, "startup-line\n");

      DiagnosticBundleExporter.Export(dir, zipPath, appVersion: "9.9.9-test");

      File.Exists(zipPath).Should().BeTrue();
      using var archive = ZipFile.OpenRead(zipPath);

      archive.Entries.Select(e => e.FullName.Replace('\\', '/'))
        .Should().Contain([
          "manifest.json",
          "config/SmartGuard.config.json",
          "status/SmartGuard.status.json",
          "logs/SmartGuard.log",
          "logs/SmartGuard.startup.log",
        ]);

      var manifestJson = ReadEntryText(archive, "manifest.json");
      using var manifest = JsonDocument.Parse(manifestJson);
      manifest.RootElement.GetProperty("appVersion").GetString().Should().Be("9.9.9-test");
      manifest.RootElement.GetProperty("exportedAt").GetString().Should().NotBeNullOrWhiteSpace();
      manifest.RootElement.GetProperty("files").EnumerateArray()
        .Select(e => e.GetProperty("path").GetString())
        .Should().Contain([
          "config/SmartGuard.config.json",
          "status/SmartGuard.status.json",
          "logs/SmartGuard.log",
          "logs/SmartGuard.startup.log",
        ]);

      var exportedConfig = ReadEntryText(archive, "config/SmartGuard.config.json");
      exportedConfig.Should().NotContain("GitHubToken");
      exportedConfig.Should().NotContain("GitHubTokenProtected");
      exportedConfig.Should().NotContain(token);

      ReadEntryText(archive, "status/SmartGuard.status.json").Should().Contain("idleSeconds");
      ReadEntryText(archive, "logs/SmartGuard.log").Should().Contain("line-two");
      ReadEntryText(archive, "logs/SmartGuard.startup.log").Should().Contain("startup-line");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Export_marks_missing_files_in_manifest_without_throwing()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");

    try
    {
      var configPath = SmartGuardPaths.ConfigFile(dir);
      var repo = new GuardConfigRepository(configPath);
      repo.Save(GuardConfig.CreateDefault(dir));

      DiagnosticBundleExporter.Export(dir, zipPath, appVersion: "1.0.0");

      using var archive = ZipFile.OpenRead(zipPath);
      var manifestJson = ReadEntryText(archive, "manifest.json");
      using var manifest = JsonDocument.Parse(manifestJson);

      var files = manifest.RootElement.GetProperty("files").EnumerateArray()
        .ToDictionary(
          e => e.GetProperty("path").GetString()!,
          e => e.GetProperty("status").GetString());

      files["config/SmartGuard.config.json"].Should().Be("included");
      files["status/SmartGuard.status.json"].Should().Be("missing");
      files["logs/SmartGuard.log"].Should().Be("missing");
      files["logs/SmartGuard.startup.log"].Should().Be("missing");

      archive.Entries.Select(e => e.FullName.Replace('\\', '/'))
        .Should().NotContain("status/SmartGuard.status.json");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Export_truncates_main_log_tail_when_over_512KB()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");

    try
    {
      var configPath = SmartGuardPaths.ConfigFile(dir);
      var repo = new GuardConfigRepository(configPath);
      repo.Save(GuardConfig.CreateDefault(dir));

      var logPath = SmartGuardPaths.DefaultLogFile(dir);
      var head = "HEAD-MARKER\n";
      var padding = new string('x', DiagnosticBundleExporter.MainLogTailMaxBytes);
      var tail = "TAIL-MARKER";
      File.WriteAllText(logPath, head + padding + tail);

      DiagnosticBundleExporter.Export(dir, zipPath, appVersion: "1.0.0");

      using var archive = ZipFile.OpenRead(zipPath);
      var logText = ReadEntryText(archive, "logs/SmartGuard.log");
      logText.Should().Contain("TAIL-MARKER");
      logText.Should().NotContain("HEAD-MARKER");
      logText.Length.Should().BeLessThanOrEqualTo(DiagnosticBundleExporter.MainLogTailMaxBytes + 64);

      var manifestJson = ReadEntryText(archive, "manifest.json");
      using var manifest = JsonDocument.Parse(manifestJson);
      var logEntry = manifest.RootElement.GetProperty("files").EnumerateArray()
        .Single(e => e.GetProperty("path").GetString() == "logs/SmartGuard.log");
      logEntry.GetProperty("status").GetString().Should().Be("truncated");
      logEntry.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Export_includes_jsonl_log_entry()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "diag.zip");

    try
    {
      var logPath = SmartGuardPaths.DefaultLogFile(dir);
      File.WriteAllLines(logPath, new[]
      {
        "[INFO] 2026-08-01 10:00:00 [Engine] AppStarted",
        "[ERROR] 2026-08-01 10:01:00 [GuardianLoop] IterationError op=op-abc"
      });

      DiagnosticBundleExporter.Export(dir, zipPath, "1.0.0");

      using var archive = ZipFile.OpenRead(zipPath);
      var jsonlEntry = archive.Entries.FirstOrDefault(e => e.Name == "SmartGuard.jsonl");
      jsonlEntry.Should().NotBeNull("Diagnostic bundle must include JSONL log entry");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Export_jsonl_only_reads_log_tail_when_over_512KB()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "diag.zip");
    try
    {
      var logPath = SmartGuardPaths.DefaultLogFile(dir);
      var head = "[INFO] 2026-08-01 00:00:00 [Engine] HEAD_MARKER_EVENT\n";
      var padding = new string('x', DiagnosticBundleExporter.MainLogTailMaxBytes);
      var tail = "\n[ERROR] 2026-08-01 10:01:00 [GuardianLoop] TAIL_MARKER_EVENT op=op-tail";
      File.WriteAllText(logPath, head + padding + tail);

      DiagnosticBundleExporter.Export(dir, zipPath, "1.0.0");

      using var archive = ZipFile.OpenRead(zipPath);
      var jsonl = ReadEntryText(archive, "logs/SmartGuard.jsonl");
      jsonl.Should().Contain("TAIL_MARKER_EVENT", "尾部 512KiB 内的行应被导出");
      jsonl.Should().NotContain("HEAD_MARKER_EVENT", "超出 512KiB 尾部上限的头部行不得被读取/导出");
      System.Text.Encoding.UTF8.GetByteCount(jsonl)
        .Should().BeLessThanOrEqualTo(DiagnosticBundleExporter.MainLogTailMaxBytes + 4096);
    }
    finally { Directory.Delete(dir, recursive: true); }
  }

  private static string ReadEntryText(ZipArchive archive, string entryPath)
  {
    var entry = archive.GetEntry(entryPath)
      ?? archive.Entries.SingleOrDefault(e => e.FullName.Replace('\\', '/') == entryPath);
    entry.Should().NotBeNull($"expected zip entry {entryPath}");
    using var stream = entry!.Open();
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
  }

  private static string CreateTempDir()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }
}
