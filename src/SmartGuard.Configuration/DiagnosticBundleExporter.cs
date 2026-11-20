using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartGuard.Configuration;

public static partial class DiagnosticBundleExporter
{
  public const int ManifestSchemaVersion = 2;
  public const int MainLogTailMaxBytes = 512 * 1024;

  private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

  public static void Export(string root, string destinationZipPath, string? appVersion = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(root);
    ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);
    throw new NotSupportedException(
      "Direct Export without ILogEventSource is no longer supported. Construct a DiagnosticBundleRequest and call ExportAsync.");
  }

  public static Task ExportAsync(
    DiagnosticBundleRequest request,
    IProgress<DiagnosticBundleProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentException.ThrowIfNullOrWhiteSpace(request.Root);
    ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationZipPath);
    ArgumentNullException.ThrowIfNull(request.EventSource);

    if (request.Scope == DiagnosticBundleScope.CurrentOperation && request.OperationKey is null)
      throw new ArgumentException("OperationKey is required for CurrentOperation scope.", nameof(request));

    if (File.Exists(request.DestinationZipPath) && !request.OverwriteExisting)
    {
      throw new InvalidOperationException(
        "Destination already exists and OverwriteExisting is false.");
    }

    return ExportCoreAsync(request, progress, cancellationToken);
  }

  internal static string SanitizeConfigJson(string configJson) => DiagnosticRedactor.RedactConfigJson(configJson);

  private static async Task ExportCoreAsync(
    DiagnosticBundleRequest request,
    IProgress<DiagnosticBundleProgress>? progress,
    CancellationToken cancellationToken)
  {
    var destinationDirectory = Path.GetDirectoryName(request.DestinationZipPath);
    if (!string.IsNullOrEmpty(destinationDirectory))
      Directory.CreateDirectory(destinationDirectory);

    var tempPath = request.DestinationZipPath + ".tmp-" + Guid.NewGuid().ToString("N") + ".zip";
    var fileEntries = new List<ManifestFileEntry>();

    try
    {
      progress?.Report(new DiagnosticBundleProgress("collecting", 0, 4));
      cancellationToken.ThrowIfCancellationRequested();

      await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
      using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
      {
        AddJsonFileEntry(SmartGuardPaths.ConfigFile(request.Root), "config/SmartGuard.config.json", 8 * 1024 * 1024,
          SanitizeConfigJson, archive, fileEntries, cancellationToken);
        AddJsonFileEntry(SmartGuardPaths.StatusFile(request.Root), "status/SmartGuard.status.json", 256 * 1024,
          DiagnosticRedactor.RedactJson, archive, fileEntries, cancellationToken);

        progress?.Report(new DiagnosticBundleProgress("streaming-logs", 1, 4));
        await AddScopedLogEntriesAsync(request, archive, fileEntries, cancellationToken);

        if (request.IncludeStartupLog)
          AddOptionalRedactedFileEntry(SmartGuardPaths.StartupLogFile(request.Root), "logs/SmartGuard.startup.log", archive, fileEntries, cancellationToken);

        if (request.IncludeEmergencyLog)
          AddOptionalRedactedFileEntry(LogFallbackChain.EmergencyLogFile, "logs/SmartGuard.emergency.log", archive, fileEntries, cancellationToken);

        if (request.IncludeCrashLog)
          AddOptionalRedactedFileEntry(CrashLogWriter.CrashLogFile, "logs/SmartGuard.crash.log", archive, fileEntries, cancellationToken);

        if (request.IncludeCrashRingLog)
          AddOptionalRedactedFileEntry(CrashRingBuffer.CrashRingLogFile, "logs/SmartGuard.crash-ring.log", archive, fileEntries, cancellationToken);

        progress?.Report(new DiagnosticBundleProgress("manifest", 3, 4));
        var manifestJson = BuildManifestJson(request, fileEntries);
        WriteTextEntry(archive, "manifest.json", manifestJson, fileEntries, ManifestFileEntry.Included("manifest.json"));
      }

      cancellationToken.ThrowIfCancellationRequested();
      PublishBundle(tempPath, request.DestinationZipPath, request.OverwriteExisting);
      // Publication is the commit point. A failed progress observer or backup
      // cleanup cannot turn a committed file into an apparent export failure.
      try { progress?.Report(new DiagnosticBundleProgress("complete", 4, 4)); } catch { }
    }
    catch (OperationCanceledException)
    {
      TryDeleteFile(tempPath);
      throw;
    }
    catch
    {
      TryDeleteFile(tempPath);
      throw;
    }
  }

  private static async Task AddScopedLogEntriesAsync(
    DiagnosticBundleRequest request,
    ZipArchive archive,
    List<ManifestFileEntry> fileEntries,
    CancellationToken cancellationToken)
  {
    var effectiveExpression = ResolveEffectiveExpression(request);
    var collected = new Queue<(string Id, string Jsonl, string Text, int JsonBytes, int TextBytes)>();
    var jsonBytes = 0;
    var textBytes = 0;
    var truncated = false;
    var redactionFailed = false;
    var readFailed = false;
    var omittedRecords = 0;
    var seen = new HashSet<string>(StringComparer.Ordinal);
    void Collect(LogEvent item)
    {
      try
      {
        if (!MatchesTimeRange(item, request) || !LogQueryExpressionEvaluator.Matches(item, effectiveExpression)) return;
        var sanitized = DiagnosticRedactor.RedactEvent(item);
        var json = LogJsonlFormatter.FormatLine(sanitized);
        var text = LogEventDisplayFormatter.FormatDisplayLine(sanitized);
        var jsonLength = Encoding.UTF8.GetByteCount(json) + 1;
        var textLength = Encoding.UTF8.GetByteCount(text) + 1;
        const int payloadBudget = MainLogTailMaxBytes - 4096;
        if (jsonLength > payloadBudget || textLength > payloadBudget) { truncated = true; omittedRecords++; return; }
        if (!seen.Add(item.EventId)) return;
        collected.Enqueue((item.EventId, json, text, jsonLength, textLength)); jsonBytes += jsonLength; textBytes += textLength;
        while (jsonBytes > payloadBudget || textBytes > payloadBudget)
        {
          var removed = collected.Dequeue(); jsonBytes -= removed.JsonBytes; textBytes -= removed.TextBytes; truncated = true;
          // The identity set is only the retained bounded view, never all
          // historical events in a long streaming export.
          seen.Remove(removed.Id);
          omittedRecords++;
        }
      }
      catch { redactionFailed = true; truncated = true; omittedRecords++; }
    }

    if (request.Scope == DiagnosticBundleScope.RecentWindow)
    {
      foreach (var logEvent in request.RecentWindowEvents ?? Array.Empty<LogEvent>())
      {
        cancellationToken.ThrowIfCancellationRequested();
        Collect(logEvent);
      }
    }
    else
    {
      try
      {
        var readScope = BuildReadScope(request);
        await foreach (var logEvent in request.EventSource.StreamAsync(readScope, cancellationToken))
        {
          cancellationToken.ThrowIfCancellationRequested();
          Collect(logEvent);
        }
      }
      catch (OperationCanceledException) { throw; }
      catch { readFailed = true; truncated = true; }
    }

    if (truncated)
    {
      var marker = LogStructuredSink.CreateComponentEvent(LogSeverity.Warn, LogComponentRegistry.Configuration,
        LogChannelRegistry.Diagnostics, EventNameRegistry.LogDeduplicationEmitted, "诊断日志已截断或省略无法脱敏的记录");
      var json = LogJsonlFormatter.FormatLine(marker); var text = LogEventDisplayFormatter.FormatDisplayLine(marker);
      collected.Enqueue((marker.EventId, json, text, Encoding.UTF8.GetByteCount(json) + 1, Encoding.UTF8.GetByteCount(text) + 1));
    }
    if (collected.Count == 0)
    {
      fileEntries.Add(ManifestFileEntry.Missing("logs/SmartGuard.jsonl"));
      fileEntries.Add(ManifestFileEntry.Missing("logs/SmartGuard.log"));
      return;
    }

    var jsonl = string.Join("\n", collected.Select(item => item.Jsonl)) + "\n";
    var logText = string.Join("\n", collected.Select(item => item.Text)) + "\n";

    foreach (var (path, content) in new[] { ("logs/SmartGuard.jsonl", jsonl), ("logs/SmartGuard.log", logText) })
      WriteTextEntry(archive, path, content, fileEntries, new ManifestFileEntry(path, truncated ? "truncated" : "included", truncated, null)
      { ReadStatus = readFailed ? "unreadable" : "included", OmittedRecords = omittedRecords,
        OmittedReason = readFailed ? "unreadable" : redactionFailed ? "redaction-failed" : truncated ? "byte-budget" : null });
  }

  private static LogQueryExpression ResolveEffectiveExpression(DiagnosticBundleRequest request)
  {
    return request.Scope switch
    {
      DiagnosticBundleScope.CurrentOperation =>
        LogOperationGraph.CombineWithBase(
          request.FilterExpression,
          LogOperationGraph.BuildCorrelationExpression(request.OperationKey!)),
      _ => request.FilterExpression ?? LogQueryMatchAllExpression.Instance,
    };
  }

  private static LogReadScope BuildReadScope(DiagnosticBundleRequest request)
  {
    if (request.CatalogSnapshot is null)
      return new LogReadScope(Array.Empty<LogSourceIdentity>());

    return new LogReadScope(
      request.CatalogSnapshot.MainHistoryOldestFirst.Concat(request.IncludeStartupLog ? request.CatalogSnapshot.StartupHistoryOldestFirst : [])
        .Select(source => source.Identity)
        .ToArray());
  }

  private static bool MatchesTimeRange(LogEvent logEvent, DiagnosticBundleRequest request)
  {
    if (request.TimeRangeStart is { } start && logEvent.Timestamp < start)
      return false;
    if (request.TimeRangeEnd is { } end && logEvent.Timestamp > end)
      return false;
    return true;
  }


  private static string BuildManifestJson(DiagnosticBundleRequest request, IReadOnlyList<ManifestFileEntry> files)
  {
    var manifest = new JsonObject
    {
      ["schemaVersion"] = ManifestSchemaVersion,
      ["exportedAt"] = DateTimeOffset.Now.ToString("o"),
      ["appVersion"] = request.AppVersion ?? ResolveAppVersion(),
      ["osVersion"] = RuntimeInformation.OSDescription,
      ["scope"] = request.Scope.ToString(),
      ["files"] = new JsonArray(files.Select(f => f.ToJson()).ToArray()),
    };

    if (request.FilterExpression is not null and not LogQueryMatchAllExpression)
      manifest["filterExpression"] = DiagnosticRedactor.RedactText(LogQueryExpressionComparer.ToCanonicalText(request.FilterExpression));

    if (request.TimeRangeStart is not null || request.TimeRangeEnd is not null)
    {
      manifest["timeRange"] = new JsonObject
      {
        ["start"] = request.TimeRangeStart?.ToString("o"),
        ["end"] = request.TimeRangeEnd?.ToString("o"),
      };
    }

    if (request.CatalogSnapshot is not null)
    {
      manifest["sourceCatalog"] = new JsonArray(
        request.CatalogSnapshot.MainHistoryOldestFirst.Concat(request.IncludeStartupLog ? request.CatalogSnapshot.StartupHistoryOldestFirst : [])
          .Select(source => new JsonObject
          {
            ["path"] = Path.GetFileName(source.FilePath),
            ["kind"] = source.Kind.ToString(),
            ["identity"] = source.Identity.ContentHashHex,
          })
          .ToArray<JsonNode?>());
    }

    return manifest.ToJsonString(JsonWriteOptions);
  }

  private static string ResolveAppVersion()
  {
    var version = typeof(DiagnosticBundleExporter).Assembly.GetName().Version;
    if (version is null)
      return "unknown";

    return version.Build > 0
      ? $"{version.Major}.{version.Minor}.{version.Build}"
      : $"{version.Major}.{version.Minor}";
  }

  private static void WriteTextEntry(
    ZipArchive archive,
    string entryPath,
    string content,
    List<ManifestFileEntry> fileEntries,
    ManifestFileEntry manifestEntry)
  {
    WriteBinaryEntry(archive, entryPath, Encoding.UTF8.GetBytes(content), fileEntries, manifestEntry);
  }

  private static void WriteBinaryEntry(
    ZipArchive archive,
    string entryPath,
    byte[] content,
    List<ManifestFileEntry> fileEntries,
    ManifestFileEntry manifestEntry)
  {
    var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
    using var entryStream = entry.Open();
    entryStream.Write(content, 0, content.Length);
    fileEntries.Add(manifestEntry with { IncludedBytes = content.Length, Sha256Hex = ComputeSha256Hex(content) });
  }

  private static string ComputeSha256Hex(string content)
    => ComputeSha256Hex(Encoding.UTF8.GetBytes(content));

  private static string ComputeSha256Hex(byte[] content)
    => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

  private static void TryDeleteFile(string path)
  {
    try
    {
      if (File.Exists(path))
        File.Delete(path);
    }
    catch
    {
      // Best-effort cleanup for cancelled/failed exports.
    }
  }

  private sealed record ManifestFileEntry(string Path, string Status, bool Truncated, string? Sha256Hex)
  {
    internal string? ReadStatus { get; init; }
    internal long? SourceBytes { get; init; }
    internal int? IncludedBytes { get; init; }
    internal string? OmittedReason { get; init; }
    internal bool? MaintenanceCompleted { get; init; }
    internal int? OmittedRecords { get; init; }
    internal static ManifestFileEntry Included(string path, string? sha256Hex = null)
      => new(path, "included", false, sha256Hex);

    internal static ManifestFileEntry Missing(string path)
      => new(path, "missing", false, null);

    internal static ManifestFileEntry TruncatedEntry(string path, string? sha256Hex = null)
      => new(path, "truncated", true, sha256Hex);

    internal JsonObject ToJson()
    {
      var node = new JsonObject
      {
        ["path"] = Path,
        ["status"] = Status,
      };
      if (Truncated)
        node["truncated"] = true;
      if (!string.IsNullOrWhiteSpace(Sha256Hex))
        node["sha256"] = Sha256Hex;
      if (ReadStatus is not null) node["readStatus"] = ReadStatus;
      if (SourceBytes is not null) node["sourceBytes"] = SourceBytes;
      if (IncludedBytes is not null) node["includedBytes"] = IncludedBytes;
      if (OmittedReason is not null) node["omittedReason"] = OmittedReason;
      if (MaintenanceCompleted is not null) node["maintenanceCompleted"] = MaintenanceCompleted;
      if (OmittedRecords is not null) node["omittedRecords"] = OmittedRecords;
      return node;
    }
  }
}
