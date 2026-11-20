using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class DiagnosticBundleExporterTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task Publication_failure_preserves_target_without_complete(bool locked)
  {
    var root = CreateTempDir();
    try
    {
      WriteMinimalInstallTree(root);
      var target = Path.Combine(root, "bundle.zip");
      FileStream? held = null;
      var stages = new List<string>();
      var progress = new InlineProgress(p =>
      {
        stages.Add(p.Stage);
        if (p.Stage != "manifest") return;
        File.WriteAllText(target, "ORIGINAL");
        if (locked) held = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);
      });
      try
      {
        var request = BuildRequest(root, target, new FakeLogEventSource(CreateSampleEvents()), DiagnosticBundleScope.FullAvailableHistory, overwriteExisting: locked);
        var run = () => DiagnosticBundleExporter.ExportAsync(request, progress);
        await run.Should().ThrowAsync<Exception>();
      }
      finally { held?.Dispose(); }
      File.ReadAllText(target).Should().Be("ORIGINAL");
      stages.Should().NotContain("complete");
      Directory.GetFiles(root, "*.tmp-*.zip").Should().BeEmpty();
    }
    finally { Directory.Delete(root, true); }
  }

  [Fact]
  public async Task Optional_jsonl_is_recursively_redacted_and_manifest_hash_matches_actual_bytes()
  {
    var root = CreateTempDir();
    try
    {
      WriteMinimalInstallTree(root);
      var item = CreateEvent("safe", DateTimeOffset.UtcNow) with
      { Properties = new Dictionary<string, JsonElement> { ["data"] = JsonSerializer.SerializeToElement(new[] { new { secret = "nested-fallback-secret" } }) } };
      File.WriteAllText(SmartGuardPaths.StartupLogFile(root), LogJsonlFormatter.FormatLine(item) + "\n{ damaged secret }\n", new UTF8Encoding(false));
      var request = BuildRequest(root, Path.Combine(root, "bundle.zip"), new FakeLogEventSource([]), DiagnosticBundleScope.FullAvailableHistory, includeStartupLog: true);
      await DiagnosticBundleExporter.ExportAsync(request);
      using var zip = ZipFile.OpenRead(request.DestinationZipPath);
      var text = ReadEntryText(zip, "logs/SmartGuard.startup.log");
      text.Should().NotContain("nested-fallback-secret").And.NotContain("damaged secret");
      using var manifest = JsonDocument.Parse(ReadEntryText(zip, "manifest.json"));
      var file = manifest.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("path").GetString() == "logs/SmartGuard.startup.log");
      file.GetProperty("includedBytes").GetInt32().Should().Be(Encoding.UTF8.GetByteCount(text));
      file.GetProperty("sha256").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant());
      file.GetProperty("omittedReason").GetString().Should().Be("redaction-failed");
    }
    finally { Directory.Delete(root, true); }
  }

  [Fact]
  public async Task Main_read_failure_preserves_partial_evidence_and_other_files()
  {
    var root = CreateTempDir();
    try
    {
      WriteMinimalInstallTree(root);
      var request = BuildRequest(root, Path.Combine(root, "bundle.zip"), new FakeLogEventSource(CreateSampleEvents(), failAfter: 1), DiagnosticBundleScope.FullAvailableHistory);
      await DiagnosticBundleExporter.ExportAsync(request);
      using var zip = ZipFile.OpenRead(request.DestinationZipPath);
      ReadEntryText(zip, "logs/SmartGuard.jsonl").Should().Contain("line-one");
      zip.GetEntry("status/SmartGuard.status.json").Should().NotBeNull();
      using var manifest = JsonDocument.Parse(ReadEntryText(zip, "manifest.json"));
      var main = manifest.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("path").GetString() == "logs/SmartGuard.jsonl");
      main.GetProperty("readStatus").GetString().Should().Be("unreadable");
      main.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }
    finally { Directory.Delete(root, true); }
  }

  [Fact]
  public async Task Invalid_config_does_not_abort_redacted_diagnostic_export()
  {
    var root = CreateTempDir();
    try
    {
      WriteMinimalInstallTree(root);
      File.WriteAllText(SmartGuardPaths.ConfigFile(root), "{ damaged-secret-fixture }");
      var request = BuildRequest(root, Path.Combine(root, "bundle.zip"), new FakeLogEventSource(CreateSampleEvents()), DiagnosticBundleScope.FullAvailableHistory);
      await DiagnosticBundleExporter.ExportAsync(request);
      using var zip = ZipFile.OpenRead(request.DestinationZipPath);
      zip.GetEntry("config/SmartGuard.config.json").Should().BeNull();
      using var manifest = JsonDocument.Parse(ReadEntryText(zip, "manifest.json"));
      var config = manifest.RootElement.GetProperty("files").EnumerateArray().Single(file => file.GetProperty("path").GetString() == "config/SmartGuard.config.json");
      config.GetProperty("readStatus").GetString().Should().Be("redaction-failed");
      zip.GetEntry("logs/SmartGuard.jsonl").Should().NotBeNull();
    }
    finally { Directory.Delete(root, true); }
  }

  [Fact]
  public async Task Recent_window_and_oversized_supplement_are_strictly_bounded()
  {
    var root = CreateTempDir();
    try
    {
      WriteMinimalInstallTree(root);
      var startup = SmartGuardPaths.StartupLogFile(root);
      using (var file = new FileStream(startup, FileMode.Create))
      {
        file.SetLength(100 * 1024 * 1024); file.Seek(-32, SeekOrigin.End);
        file.Write(Encoding.UTF8.GetBytes("\ncomplete-tail --token=secret\n"));
      }
      var events = Enumerable.Range(0, 1000).Select(i => CreateEvent(new string('界', 1024), DateTimeOffset.UtcNow)).ToArray();
      var request = BuildRequest(root, Path.Combine(root, "bundle.zip"), new FakeLogEventSource(events), DiagnosticBundleScope.RecentWindow,
        recentWindowEvents: events, includeStartupLog: true);
      await DiagnosticBundleExporter.ExportAsync(request);
      using var zip = ZipFile.OpenRead(request.DestinationZipPath);
      zip.GetEntry("logs/SmartGuard.jsonl")!.Length.Should().BeLessThanOrEqualTo(512 * 1024);
      zip.GetEntry("logs/SmartGuard.log")!.Length.Should().BeLessThanOrEqualTo(512 * 1024);
      zip.GetEntry("logs/SmartGuard.startup.log")!.Length.Should().BeLessThanOrEqualTo(256 * 1024);
      ReadEntryText(zip, "logs/SmartGuard.startup.log").Should().Contain("complete-tail").And.NotContain("=secret");
    }
    finally { Directory.Delete(root, true); }
  }

  [Fact]
  public async Task Cancellation_at_manifest_preserves_target_and_never_reports_complete()
  {
    var root = CreateTempDir();
    try
    {
      WriteMinimalInstallTree(root);
      var target = Path.Combine(root, "bundle.zip"); File.WriteAllText(target, "ORIGINAL");
      using var cancellation = new CancellationTokenSource();
      var stages = new List<string>();
      var progress = new InlineProgress(item => { stages.Add(item.Stage); if (item.Stage == "manifest") cancellation.Cancel(); });
      var request = BuildRequest(root, target, new FakeLogEventSource(CreateSampleEvents()), DiagnosticBundleScope.FullAvailableHistory, overwriteExisting: true);
      var action = () => DiagnosticBundleExporter.ExportAsync(request, progress, cancellation.Token);
      await action.Should().ThrowAsync<OperationCanceledException>();
      File.ReadAllText(target).Should().Be("ORIGINAL"); stages.Should().NotContain("complete");
      Directory.GetFiles(root, "*.tmp-*.zip").Should().BeEmpty();
    }
    finally { Directory.Delete(root, true); }
  }

  [Fact]
  public async Task Status_nested_arrays_and_event_exception_are_redacted()
  {
    var root = CreateTempDir();
    try
    {
      WriteMinimalInstallTree(root);
      File.WriteAllText(SmartGuardPaths.StatusFile(root), """{"data":[{"secret":"nested-secret","path":"C:\\Users\\private.user\\a"}]}""");
      var redact = () => DiagnosticRedactor.RedactJson(File.ReadAllText(SmartGuardPaths.StatusFile(root)));
      redact.Should().NotThrow();
      var item = CreateEvent("normal", DateTimeOffset.UtcNow) with
      {
        Exception = new LogExceptionInfo("System.Exception", @"C:\Users\private.user\a --token=exception-secret", null, null),
        Properties = new Dictionary<string, JsonElement> { ["nested"] = JsonSerializer.SerializeToElement(new[] { new { password = "array-secret", path = @"C:\Users\private.user\a" } }) }
      };
      var request = BuildRequest(root, Path.Combine(root, "bundle.zip"), new FakeLogEventSource([item]), DiagnosticBundleScope.FullAvailableHistory);
      await DiagnosticBundleExporter.ExportAsync(request);
      using var zip = ZipFile.OpenRead(request.DestinationZipPath);
      var contents = ReadEntryText(zip, "logs/SmartGuard.jsonl") + ReadEntryText(zip, "status/SmartGuard.status.json");
      contents.Should().NotContain("nested-secret").And.NotContain("array-secret").And.NotContain("exception-secret").And.NotContain("private.user");
    }
    finally { Directory.Delete(root, true); }
  }

  private sealed class InlineProgress(Action<DiagnosticBundleProgress> report) : IProgress<DiagnosticBundleProgress>
  { public void Report(DiagnosticBundleProgress value) => report(value); }

  [Fact]
  public void SanitizeConfigJson_removes_retired_secret_fields()
  {
    const string token = "ghp_secretExportToken1234567890";
    const string protectedValue = "legacy-protected-value";
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

  [Theory]
  [InlineData(DiagnosticBundleScope.RecentWindow)]
  [InlineData(DiagnosticBundleScope.CurrentQuery)]
  [InlineData(DiagnosticBundleScope.CurrentOperation)]
  [InlineData(DiagnosticBundleScope.FullAvailableHistory)]
  public async Task ExportAsync_writes_manifest_scope_filter_time_hash_and_status(DiagnosticBundleScope scope)
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");
    var events = CreateSampleEvents();
    var filter = scope == DiagnosticBundleScope.CurrentQuery
      ? new LogQueryFieldExpression("component", LogQueryOperator.Equals, "Engine")
      : null;
    LogOperationCorrelationKey? operationKey = scope == DiagnosticBundleScope.CurrentOperation
      ? new LogOperationCorrelationKey(LogCorrelationKeyKind.OperationId, "op-abc")
      : null;

    try
    {
      WriteMinimalInstallTree(dir);
      var request = BuildRequest(
        dir,
        zipPath,
        new FakeLogEventSource(events),
        scope,
        filter,
        operationKey,
        recentWindowEvents: scope == DiagnosticBundleScope.RecentWindow ? events : null,
        timeStart: DateTimeOffset.Parse("2026-08-01T09:00:00Z"),
        timeEnd: DateTimeOffset.Parse("2026-08-01T11:00:00Z"));

      await DiagnosticBundleExporter.ExportAsync(request);

      using var archive = ZipFile.OpenRead(zipPath);
      var manifestJson = ReadEntryText(archive, "manifest.json");
      using var manifest = JsonDocument.Parse(manifestJson);

      manifest.RootElement.GetProperty("schemaVersion").GetInt32()
        .Should().Be(DiagnosticBundleExporter.ManifestSchemaVersion);
      manifest.RootElement.GetProperty("scope").GetString().Should().Be(scope.ToString());
      manifest.RootElement.GetProperty("appVersion").GetString().Should().Be("9.9.9-test");
      manifest.RootElement.GetProperty("exportedAt").GetString().Should().NotBeNullOrWhiteSpace();
      manifest.RootElement.GetProperty("timeRange").GetProperty("start").GetString().Should().NotBeNullOrWhiteSpace();
      manifest.RootElement.GetProperty("timeRange").GetProperty("end").GetString().Should().NotBeNullOrWhiteSpace();

      if (filter is not null)
        manifest.RootElement.GetProperty("filterExpression").GetString().Should().NotBeNullOrWhiteSpace();

      var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToList();
      files.Should().NotBeEmpty();
      foreach (var included in files.Where(f => f.GetProperty("status").GetString() is "included" or "truncated"))
      {
        included.TryGetProperty("sha256", out var hash).Should().BeTrue();
        hash.GetString().Should().MatchRegex("^[a-f0-9]{64}$");
      }
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public async Task ExportAsync_redacts_token_password_secret_username_paths_and_command_args()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");
    const string token = "ghp_bundleExportToken1234567890";

    try
    {
      WriteMinimalInstallTree(dir, token);
      File.WriteAllText(
        SmartGuardPaths.StartupLogFile(dir),
        "startup for C:\\Users\\secret.user\\AppData --token=startup-secret\n");

      var sensitiveEvent = new LogEvent(
        LogEvent.CurrentSchemaVersion,
        "evt-sensitive",
        DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
        LogSeverity.Error,
        "Engine",
        "Lifecycle",
        "AppStarted",
        "failed loading C:\\Users\\secret.user\\config --password=plain-secret",
        null,
        "op-abc",
        null,
        null,
        null,
        null,
        null,
        new Dictionary<string, System.Text.Json.JsonElement>
        {
          ["secret"] = System.Text.Json.JsonSerializer.SerializeToElement("property-secret"),
        });

      var request = BuildRequest(
        dir,
        zipPath,
        new FakeLogEventSource([sensitiveEvent]),
        DiagnosticBundleScope.FullAvailableHistory,
        includeStartupLog: true);

      await DiagnosticBundleExporter.ExportAsync(request);

      using var archive = ZipFile.OpenRead(zipPath);
      var exportedConfig = ReadEntryText(archive, "config/SmartGuard.config.json");
      exportedConfig.Should().NotContain(token);
      exportedConfig.Should().NotContain("GitHubToken");

      var jsonl = ReadEntryText(archive, "logs/SmartGuard.jsonl");
      jsonl.Should().NotContain("secret.user");
      jsonl.Should().NotContain("plain-secret");
      jsonl.Should().NotContain("property-secret");

      var startup = ReadEntryText(archive, "logs/SmartGuard.startup.log");
      startup.Should().NotContain("secret.user");
      startup.Should().NotContain("startup-secret");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public async Task ExportAsync_uses_catalog_snapshot_instead_of_live_rotation()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");
    var snapshotPath = SmartGuardPaths.DefaultLogFile(dir);
    var catalog = LogSourceCatalog.Build(snapshotPath, null);
    var snapshotEvent = CreateEvent("SNAPSHOT_MARKER", DateTimeOffset.Parse("2026-08-01T10:00:00Z"));
    var liveOnlyEvent = CreateEvent("LIVE_ONLY_MARKER", DateTimeOffset.Parse("2026-08-01T10:01:00Z"));

    try
    {
      WriteMinimalInstallTree(dir);
      var request = BuildRequest(
        dir,
        zipPath,
        new FakeLogEventSource([snapshotEvent, liveOnlyEvent], catalog),
        DiagnosticBundleScope.FullAvailableHistory,
        catalogSnapshot: catalog);

      await DiagnosticBundleExporter.ExportAsync(request);

      using var archive = ZipFile.OpenRead(zipPath);
      var manifestJson = ReadEntryText(archive, "manifest.json");
      manifestJson.Should().Contain("sourceCatalog");
      var jsonl = ReadEntryText(archive, "logs/SmartGuard.jsonl");
      jsonl.Should().Contain("SNAPSHOT_MARKER");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public async Task ExportAsync_default_overwrite_false_leaves_existing_target_untouched()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");
    File.WriteAllText(zipPath, "ORIGINAL");

    try
    {
      WriteMinimalInstallTree(dir);
      var request = BuildRequest(
        dir,
        zipPath,
        new FakeLogEventSource(CreateSampleEvents()),
        DiagnosticBundleScope.FullAvailableHistory);

      var act = () => DiagnosticBundleExporter.ExportAsync(request);

      await act.Should().ThrowAsync<InvalidOperationException>();
      File.ReadAllText(zipPath).Should().Be("ORIGINAL");
      Directory.GetFiles(dir, "*.tmp-*.zip").Should().BeEmpty();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public async Task ExportAsync_overwrite_existing_true_atomically_replaces_target()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");
    File.WriteAllText(zipPath, "ORIGINAL");

    try
    {
      WriteMinimalInstallTree(dir);
      var request = BuildRequest(
        dir,
        zipPath,
        new FakeLogEventSource(CreateSampleEvents()),
        DiagnosticBundleScope.FullAvailableHistory,
        overwriteExisting: true);

      await DiagnosticBundleExporter.ExportAsync(request);

      File.Exists(zipPath).Should().BeTrue();
      using var archive = ZipFile.OpenRead(zipPath);
      archive.Entries.Should().Contain(e => e.FullName.Replace('\\', '/') == "manifest.json");
      Directory.GetFiles(dir, "*.tmp-*.zip").Should().BeEmpty();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public async Task ExportAsync_cancel_deletes_temp_zip_and_preserves_existing_target()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");
    File.WriteAllText(zipPath, "ORIGINAL");

    try
    {
      WriteMinimalInstallTree(dir);
      using var cts = new CancellationTokenSource();
      var request = BuildRequest(
        dir,
        zipPath,
        new FakeLogEventSource(CreateSampleEvents(), cancelAfter: 1, cancellationToken: cts.Token),
        DiagnosticBundleScope.FullAvailableHistory,
        overwriteExisting: true);

      var act = () => DiagnosticBundleExporter.ExportAsync(request, cancellationToken: cts.Token);
      cts.Cancel();

      await act.Should().ThrowAsync<OperationCanceledException>();
      File.ReadAllText(zipPath).Should().Be("ORIGINAL");
      Directory.GetFiles(dir, "*.tmp-*.zip").Should().BeEmpty();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public async Task ExportAsync_marks_missing_files_in_manifest_without_throwing()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");

    try
    {
      var configPath = SmartGuardPaths.ConfigFile(dir);
      var repo = new GuardConfigRepository(configPath);
      repo.Save(GuardConfig.CreateDefault(dir));

      var request = BuildRequest(
        dir,
        zipPath,
        new FakeLogEventSource(Array.Empty<LogEvent>()),
        DiagnosticBundleScope.FullAvailableHistory);

      await DiagnosticBundleExporter.ExportAsync(request);

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
      files["logs/SmartGuard.jsonl"].Should().Be("missing");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public async Task ExportAsync_truncates_streamed_logs_when_over_512KB()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");
    var events = Enumerable.Range(0, 4000)
      .Select(i => CreateEvent(new string('x', 256) + i, DateTimeOffset.Parse("2026-08-01T10:00:00Z").AddSeconds(i)))
      .ToList();

    try
    {
      WriteMinimalInstallTree(dir);
      var request = BuildRequest(
        dir,
        zipPath,
        new FakeLogEventSource(events),
        DiagnosticBundleScope.FullAvailableHistory);

      await DiagnosticBundleExporter.ExportAsync(request);

      using var archive = ZipFile.OpenRead(zipPath);
      var jsonl = ReadEntryText(archive, "logs/SmartGuard.jsonl");
      Encoding.UTF8.GetByteCount(jsonl).Should().BeLessThanOrEqualTo(DiagnosticBundleExporter.MainLogTailMaxBytes + 4096);

      var manifestJson = ReadEntryText(archive, "manifest.json");
      using var manifest = JsonDocument.Parse(manifestJson);
      var jsonlEntry = manifest.RootElement.GetProperty("files").EnumerateArray()
        .Single(e => e.GetProperty("path").GetString() == "logs/SmartGuard.jsonl");
      jsonlEntry.GetProperty("status").GetString().Should().Be("truncated");
      jsonlEntry.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public async Task ExportAsync_current_operation_scope_filters_to_operation_key()
  {
    var dir = CreateTempDir();
    var zipPath = Path.Combine(dir, "bundle.zip");
    var events = new[]
    {
      CreateEvent("match", DateTimeOffset.Parse("2026-08-01T10:00:00Z"), operationId: "op-target"),
      CreateEvent("skip", DateTimeOffset.Parse("2026-08-01T10:01:00Z"), operationId: "op-other"),
    };

    try
    {
      WriteMinimalInstallTree(dir);
      var request = BuildRequest(
        dir,
        zipPath,
        new FakeLogEventSource(events),
        DiagnosticBundleScope.CurrentOperation,
        operationKey: new LogOperationCorrelationKey(LogCorrelationKeyKind.OperationId, "op-target"));

      await DiagnosticBundleExporter.ExportAsync(request);

      using var archive = ZipFile.OpenRead(zipPath);
      var jsonl = ReadEntryText(archive, "logs/SmartGuard.jsonl");
      jsonl.Should().Contain("match");
      jsonl.Should().NotContain("skip");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  private static DiagnosticBundleRequest BuildRequest(
    string dir,
    string zipPath,
    ILogEventSource eventSource,
    DiagnosticBundleScope scope,
    LogQueryExpression? filter = null,
    LogOperationCorrelationKey? operationKey = null,
    IReadOnlyList<LogEvent>? recentWindowEvents = null,
    LogSourceCatalog? catalogSnapshot = null,
    DateTimeOffset? timeStart = null,
    DateTimeOffset? timeEnd = null,
    bool overwriteExisting = false,
    bool includeStartupLog = false)
    => new()
    {
      Root = dir,
      DestinationZipPath = zipPath,
      AppVersion = "9.9.9-test",
      EventSource = eventSource,
      Scope = scope,
      FilterExpression = filter,
      OperationKey = operationKey,
      RecentWindowEvents = recentWindowEvents,
      CatalogSnapshot = catalogSnapshot,
      TimeRangeStart = timeStart,
      TimeRangeEnd = timeEnd,
      OverwriteExisting = overwriteExisting,
      IncludeStartupLog = includeStartupLog,
    };

  private static IReadOnlyList<LogEvent> CreateSampleEvents()
    =>
    [
      CreateEvent("line-one", DateTimeOffset.Parse("2026-08-01T10:00:00Z"), operationId: "op-abc"),
      CreateEvent("line-two", DateTimeOffset.Parse("2026-08-01T10:01:00Z"), operationId: "op-abc"),
    ];

  private static LogEvent CreateEvent(
    string message,
    DateTimeOffset timestamp,
    string? operationId = null)
    => new(
      LogEvent.CurrentSchemaVersion,
      Guid.NewGuid().ToString("N"),
      timestamp,
      LogSeverity.Info,
      "Engine",
      "Lifecycle",
      "AppStarted",
      message,
      null,
      operationId,
      null,
      null,
      null,
      null,
      null,
      LogEvent.EmptyProperties);

  private static void WriteMinimalInstallTree(string dir, string? retiredSecret = null)
  {
    var configPath = SmartGuardPaths.ConfigFile(dir);
    var repo = new GuardConfigRepository(configPath);
    var config = GuardConfig.CreateDefault(dir);
    repo.Save(config);
    if (!string.IsNullOrWhiteSpace(retiredSecret))
      File.WriteAllText(configPath, $$"""
        {
          "BalancedThresholdSec": 300,
          "GitHubToken": "{{retiredSecret}}"
        }
        """);

    var statusPath = SmartGuardPaths.StatusFile(dir);
    File.WriteAllText(statusPath, """{"paused":false,"idleSeconds":42}""");
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

  private sealed class FakeLogEventSource : ILogEventSource
  {
    private readonly IReadOnlyList<LogEvent> _events;
    private readonly LogSourceCatalog? _catalog;
    private readonly int _cancelAfter;
    private readonly CancellationToken _cancellationToken;
    private readonly int _failAfter;

    internal FakeLogEventSource(
      IReadOnlyList<LogEvent> events,
      LogSourceCatalog? catalog = null,
      int cancelAfter = int.MaxValue,
      CancellationToken cancellationToken = default,
      int failAfter = int.MaxValue)
    {
      _events = events;
      _catalog = catalog;
      _cancelAfter = cancelAfter;
      _cancellationToken = cancellationToken;
      _failAfter = failAfter;
    }

    public Task<LogEventPage> ReadLatestAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadOlderAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadNewerAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public async IAsyncEnumerable<LogEvent> StreamAsync(
      LogReadScope scope,
      [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var yielded = 0;
      foreach (var logEvent in _events)
      {
        if (_cancellationToken.IsCancellationRequested || cancellationToken.IsCancellationRequested)
          throw new OperationCanceledException(cancellationToken);

        yielded++;
        if (yielded > _failAfter) throw new IOException("fixture read failure");
        if (yielded > _cancelAfter)
          throw new OperationCanceledException(cancellationToken);

        yield return logEvent;
        await Task.Yield();
      }
    }
  }
}
