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

  private static void WriteMinimalInstallTree(string dir, string? token = null)
  {
    var configPath = SmartGuardPaths.ConfigFile(dir);
    var repo = new GuardConfigRepository(configPath);
    var config = GuardConfig.CreateDefault(dir);
    if (!string.IsNullOrWhiteSpace(token))
      config.GitHubToken = token;
    repo.Save(config);

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

    internal FakeLogEventSource(
      IReadOnlyList<LogEvent> events,
      LogSourceCatalog? catalog = null,
      int cancelAfter = int.MaxValue,
      CancellationToken cancellationToken = default)
    {
      _events = events;
      _catalog = catalog;
      _cancelAfter = cancelAfter;
      _cancellationToken = cancellationToken;
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
        if (yielded > _cancelAfter)
          throw new OperationCanceledException(cancellationToken);

        yield return logEvent;
        await Task.Yield();
      }
    }
  }
}
