using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartGuard.Configuration;

public static class DiagnosticBundleExporter
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
        AddConfigEntry(request.Root, archive, fileEntries);
        AddStatusEntry(request.Root, archive, fileEntries);

        progress?.Report(new DiagnosticBundleProgress("streaming-logs", 1, 4));
        await AddScopedLogEntriesAsync(request, archive, fileEntries, cancellationToken);

        if (request.IncludeStartupLog)
          AddOptionalRedactedFileEntry(SmartGuardPaths.StartupLogFile(request.Root), "logs/SmartGuard.startup.log", archive, fileEntries);

        if (request.IncludeEmergencyLog)
          AddOptionalRedactedFileEntry(LogFallbackChain.EmergencyLogFile, "logs/SmartGuard.emergency.log", archive, fileEntries);

        if (request.IncludeCrashLog)
          AddOptionalRedactedFileEntry(CrashLogWriter.CrashLogFile, "logs/SmartGuard.crash.log", archive, fileEntries);

        progress?.Report(new DiagnosticBundleProgress("manifest", 3, 4));
        var manifestJson = BuildManifestJson(request, fileEntries);
        WriteTextEntry(archive, "manifest.json", manifestJson, fileEntries, ManifestFileEntry.Included("manifest.json"));
      }

      progress?.Report(new DiagnosticBundleProgress("complete", 4, 4));

      if (File.Exists(request.DestinationZipPath))
        File.Delete(request.DestinationZipPath);

      File.Move(tempPath, request.DestinationZipPath);
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
    var collected = new List<LogEvent>();
    var truncated = false;

    if (request.Scope == DiagnosticBundleScope.RecentWindow)
    {
      foreach (var logEvent in request.RecentWindowEvents ?? Array.Empty<LogEvent>())
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (!MatchesTimeRange(logEvent, request))
          continue;
        if (!LogQueryExpressionEvaluator.Matches(logEvent, effectiveExpression))
          continue;
        collected.Add(DiagnosticRedactor.RedactEvent(logEvent));
      }
    }
    else
    {
      var readScope = BuildReadScope(request);
      await foreach (var logEvent in request.EventSource.StreamAsync(readScope, cancellationToken))
      {
        if (!MatchesTimeRange(logEvent, request))
          continue;
        if (!LogQueryExpressionEvaluator.Matches(logEvent, effectiveExpression))
          continue;

        collected.Add(DiagnosticRedactor.RedactEvent(logEvent));
        var payloadBytes = EstimatePayloadBytes(collected);
        if (payloadBytes > MainLogTailMaxBytes)
        {
          truncated = true;
          while (collected.Count > 0 && EstimatePayloadBytes(collected) > MainLogTailMaxBytes)
            collected.RemoveAt(0);
        }
      }
    }

    if (collected.Count == 0)
    {
      fileEntries.Add(ManifestFileEntry.Missing("logs/SmartGuard.jsonl"));
      fileEntries.Add(ManifestFileEntry.Missing("logs/SmartGuard.log"));
      return;
    }

    var jsonl = string.Join("\n", collected.Select(LogJsonlFormatter.FormatLine));
    var logText = string.Join("\n", collected.Select(LogEventDisplayFormatter.FormatDisplayLine));

    AddJsonlLogEntry(archive, fileEntries, jsonl, truncated);
    AddMainLogEntry(archive, fileEntries, logText, truncated);
  }

  private static void AddJsonlLogEntry(
    ZipArchive archive,
    List<ManifestFileEntry> fileEntries,
    string jsonl,
    bool truncated)
  {
    WriteTextEntry(archive, "logs/SmartGuard.jsonl", jsonl, fileEntries,
      truncated
        ? ManifestFileEntry.TruncatedEntry("logs/SmartGuard.jsonl", ComputeSha256Hex(jsonl))
        : ManifestFileEntry.Included("logs/SmartGuard.jsonl", ComputeSha256Hex(jsonl)));
  }

  private static void AddMainLogEntry(
    ZipArchive archive,
    List<ManifestFileEntry> fileEntries,
    string logText,
    bool truncated)
  {
    WriteTextEntry(archive, "logs/SmartGuard.log", logText, fileEntries,
      truncated
        ? ManifestFileEntry.TruncatedEntry("logs/SmartGuard.log", ComputeSha256Hex(logText))
        : ManifestFileEntry.Included("logs/SmartGuard.log", ComputeSha256Hex(logText)));
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
      request.CatalogSnapshot.MainHistoryOldestFirst
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

  private static int EstimatePayloadBytes(IReadOnlyList<LogEvent> events)
  {
    var total = 0;
    foreach (var logEvent in events)
      total += Encoding.UTF8.GetByteCount(LogJsonlFormatter.FormatLine(logEvent));
    return total;
  }

  private static void AddConfigEntry(string root, ZipArchive archive, List<ManifestFileEntry> fileEntries)
  {
    const string entryPath = "config/SmartGuard.config.json";
    var sourcePath = SmartGuardPaths.ConfigFile(root);
    if (!File.Exists(sourcePath))
    {
      fileEntries.Add(ManifestFileEntry.Missing(entryPath));
      return;
    }

    var sanitized = SanitizeConfigJson(File.ReadAllText(sourcePath));
    WriteTextEntry(archive, entryPath, sanitized, fileEntries, ManifestFileEntry.Included(entryPath, ComputeSha256Hex(sanitized)));
  }

  private static void AddStatusEntry(string root, ZipArchive archive, List<ManifestFileEntry> fileEntries)
  {
    const string entryPath = "status/SmartGuard.status.json";
    var sourcePath = SmartGuardPaths.StatusFile(root);
    if (!File.Exists(sourcePath))
    {
      fileEntries.Add(ManifestFileEntry.Missing(entryPath));
      return;
    }

    var bytes = File.ReadAllBytes(sourcePath);
    WriteBinaryEntry(archive, entryPath, bytes, fileEntries, ManifestFileEntry.Included(entryPath, ComputeSha256Hex(bytes)));
  }

  private static void AddOptionalRedactedFileEntry(
    string sourcePath,
    string entryPath,
    ZipArchive archive,
    List<ManifestFileEntry> fileEntries)
  {
    if (!File.Exists(sourcePath))
    {
      fileEntries.Add(ManifestFileEntry.Missing(entryPath));
      return;
    }

    var redacted = DiagnosticRedactor.RedactText(File.ReadAllText(sourcePath));
    WriteTextEntry(archive, entryPath, redacted, fileEntries, ManifestFileEntry.Included(entryPath, ComputeSha256Hex(redacted)));
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
      manifest["filterExpression"] = LogQueryExpressionComparer.ToCanonicalText(request.FilterExpression);

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
        request.CatalogSnapshot.MainHistoryOldestFirst
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
    fileEntries.Add(manifestEntry);
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
      return node;
    }
  }
}
