using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.LogView;

public sealed record LogFileSlice(long Length, string Text);

public sealed record LogInitialView(long FileLength, string Text, bool IsTailTruncated);

public sealed partial class LogTailReader : ILogEventSource, ILogPhysicalLineSource, ILogSnapshotPhysicalLineSource
{
  public const int DefaultTargetEventCount = 200;
  public const int DefaultMaxBytes = 262_144;
  private const int SnapshotBoundaryBytes = 4096;

  private readonly string _configuredLogPath;
  private readonly string? _startupFallbackPath;
  private readonly LogSourceCatalog _catalog;
  private readonly Dictionary<LogSourceIdentity, (long ByteOffset, string Text)> _pendingPartialLines = new();

  public LogTailReader(string logPath, string? startupFallbackPath = null)
  {
    _configuredLogPath = logPath;
    _startupFallbackPath = startupFallbackPath;
    _catalog = LogSourceCatalog.Build(logPath, startupFallbackPath);
  }

  public LogSourceCatalog Catalog => _catalog;

  internal LogSourceCatalog CatalogForTests => _catalog;

  public Task<LogEventPage> ReadLatestAsync(LogReadRequest request, CancellationToken cancellationToken)
    => Task.FromResult(ReadLatest(request, cancellationToken));

  public Task<LogEventPage> ReadOlderAsync(LogReadRequest request, CancellationToken cancellationToken)
    => Task.FromResult(ReadOlder(request, cancellationToken));

  public Task<LogEventPage> ReadNewerAsync(LogReadRequest request, CancellationToken cancellationToken)
    => Task.FromResult(ReadNewer(request, cancellationToken));

  public IEnumerable<LogPhysicalLine> EnumeratePhysicalLines(
    LogReadScope scope,
    CancellationToken cancellationToken)
  {
    foreach (var source in EnumerateCatalogSources(_catalog))
    {
      if (scope.Sources.Count > 0 && !scope.Sources.Contains(source.Identity))
        continue;

      foreach (var line in LogUtf8LineScanner.ReadLinesForward(source, 0, null, cancellationToken))
      {
        cancellationToken.ThrowIfCancellationRequested();
        yield return line;
      }
    }
  }

  public LogSourceSnapshot CaptureSnapshot()
  {
    var catalog = LogSourceCatalog.Build(_configuredLogPath, _startupFallbackPath);
    var sources = CaptureSnapshotSources(catalog);
    return new LogSourceSnapshot(sources, ComputeSnapshotFingerprint(sources));
  }

  public IEnumerable<LogPhysicalLine> EnumeratePhysicalLines(
    LogReadScope scope,
    LogSourceSnapshot snapshot,
    LogReadDirection direction,
    LogReadCursor? cursor,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(scope);
    ArgumentNullException.ThrowIfNull(snapshot);
    cancellationToken.ThrowIfCancellationRequested();
    LogTailReaderTestMetrics.MaybeDelayForTests();

    if (!string.Equals(snapshot.Fingerprint, ComputeSnapshotFingerprint(snapshot.Sources), StringComparison.Ordinal))
      throw new LogSnapshotInvalidatedException("The snapshot fingerprint is inconsistent.");

    var sources = ResolveSnapshotSources(snapshot);
    return direction switch
    {
      LogReadDirection.Newer => EnumerateSnapshotLinesForward(scope, sources, cursor, cancellationToken),
      LogReadDirection.Latest => EnumerateSnapshotLinesBackward(scope, sources, cursor, cancellationToken),
      LogReadDirection.Older => EnumerateSnapshotLinesBackward(scope, sources, cursor, cancellationToken),
      _ => EnumerateSnapshotLinesBackward(scope, sources, cursor, cancellationToken),
    };
  }

  public async IAsyncEnumerable<LogPhysicalLine> StreamPhysicalLinesAsync(
    LogReadScope scope,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    foreach (var line in EnumeratePhysicalLines(scope, cancellationToken))
      yield return line;
  }

  public async IAsyncEnumerable<LogEvent> StreamAsync(
    LogReadScope scope,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    foreach (var line in EnumeratePhysicalLines(scope, cancellationToken))
      yield return LogEventDecoder.Decode(line);
  }

  private LogEventPage ReadLatest(LogReadRequest request, CancellationToken cancellationToken)
  {
    if (HasSupplementalHistory)
      return ReadMergedHistory(request with { Direction = LogReadDirection.Latest }, cancellationToken);
    // Delay hooks live in ReadFromOffset/ReadRecentTail so one Latest flight
    // matches the lifetime-test expectation of ~1-2 physical reads, not an
    // extra bookkeeping hit at the entry point.
    if (cancellationToken.IsCancellationRequested)
      return LogEventPage.Empty;

    var targetCount = NormalizeTargetCount(request.TargetEventCount);
    var maxBytes = NormalizeMaxBytes(request.MaxBytes);

    var mainCollected = CollectMainEventsNewestFirst(
      endBoundaries: null,
      targetCount: targetCount,
      maxBytes: maxBytes,
      cancellationToken,
      out var hasMoreMain,
      out var stoppedEarly,
      cursorSourceExclusiveUpperBound: null);

    if (stoppedEarly || mainCollected.Count == 0)
      return LogEventPage.Empty;

    var merged = mainCollected;
    merged.Sort((a, b) => CompareEventsChronologically(a.Event, b.Event));

    if (merged.Count > targetCount)
      merged = merged.Skip(merged.Count - targetCount).ToList();

    var hasOlder = hasMoreMain;
    return BuildPage(merged, hasOlder, false, mainCollected);
  }

  private LogEventPage ReadOlder(LogReadRequest request, CancellationToken cancellationToken)
  {
    if (HasSupplementalHistory)
      return ReadMergedHistory(request with { Direction = LogReadDirection.Older }, cancellationToken);
    if (cancellationToken.IsCancellationRequested)
      return LogEventPage.Empty;

    if (request.Cursor is null)
      return ReadLatest(request, cancellationToken);

    var targetCount = NormalizeTargetCount(request.TargetEventCount);
    var maxBytes = NormalizeMaxBytes(request.MaxBytes);

    var endBoundaries = BuildEndBoundary(request.Cursor);
    var collected = CollectMainEventsNewestFirst(
      endBoundaries,
      targetCount,
      maxBytes,
      cancellationToken,
      out var hasMoreMain,
      out var stoppedEarly,
      request.Cursor.Source);

    if (stoppedEarly || collected.Count == 0)
      return LogEventPage.Empty;

    collected.Sort((a, b) => CompareEventsChronologically(a.Event, b.Event));
    if (collected.Count > targetCount)
      collected = collected.TakeLast(targetCount).ToList();

    var hasOlder = hasMoreMain || HasEventsBefore(collected.MinBy(item => item.Line.ByteOffset), cancellationToken);
    return BuildPage(collected, hasOlder, false);
  }

  private LogEventPage ReadNewer(LogReadRequest request, CancellationToken cancellationToken)
  {
    if (HasSupplementalHistory)
      return ReadMergedHistory(request with { Direction = LogReadDirection.Newer }, cancellationToken);
    if (cancellationToken.IsCancellationRequested)
      return LogEventPage.Empty;

    if (request.Cursor is null)
      return ReadLatest(request, cancellationToken);

    var targetCount = NormalizeTargetCount(request.TargetEventCount);
    var maxBytes = NormalizeMaxBytes(request.MaxBytes);
    var cursor = request.Cursor;
    var source = FindSource(cursor.Source);
    if (source is null)
      return LogEventPage.Empty;

    var events = new List<(LogEvent Event, LogPhysicalLine Line)>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var consumedBytes = 0;

    var startOffset = _pendingPartialLines.TryGetValue(cursor.Source, out var pending)
      ? pending.ByteOffset
      : LogUtf8LineScanner.GetLineEndOffset(source.FilePath, cursor.ByteOffset);

    foreach (var line in LogUtf8LineScanner.ReadLinesForward(source, startOffset, null, cancellationToken))
    {
      if (cancellationToken.IsCancellationRequested)
        return LogEventPage.Empty;

      var effectiveLine = line;
      if (_pendingPartialLines.TryGetValue(line.Source, out var previous))
      {
        if (line.ByteOffset == previous.ByteOffset)
        {
          if (!line.IsComplete)
          {
            _pendingPartialLines[line.Source] = (line.ByteOffset, previous.Text + line.Text);
            break;
          }

          effectiveLine = new LogPhysicalLine(
            line.Source,
            line.ByteOffset,
            previous.Text + line.Text,
            true,
            line.Ordinal);
          _pendingPartialLines.Remove(line.Source);
        }
      }

      if (!effectiveLine.IsComplete)
      {
        _pendingPartialLines[effectiveLine.Source] = (effectiveLine.ByteOffset, effectiveLine.Text);
        break;
      }

      _pendingPartialLines.Remove(effectiveLine.Source);
      consumedBytes += Encoding.UTF8.GetByteCount(effectiveLine.Text) + 1;
      if (consumedBytes > maxBytes)
        break;

      var decoded = LogEventDecoder.Decode(effectiveLine);
      if (!seen.Add(decoded.EventId))
        continue;

      events.Add((decoded, effectiveLine));
      if (events.Count >= targetCount)
        break;
    }

    AppendNewerAcrossSources(source, events, seen, targetCount, maxBytes - consumedBytes, cancellationToken);

    var ordered = events
      .OrderBy(e => e.Event, Comparer<LogEvent>.Create(CompareEventsChronologically))
      .ToList();

    if (ordered.Count == 0)
      return LogEventPage.Empty;

    var page = BuildPage(ordered, false, false);
    // The continuation flag must use the same physical boundary as its cursor;
    // display order can differ after a clock change or delayed delivery.
    var boundary = page.NewerCursor!;
    var physicalLast = ordered.First(item => item.Line.Source == boundary.Source && item.Line.ByteOffset == boundary.ByteOffset);
    return page with { HasNewer = HasNewerAfter(physicalLast, FindSource(boundary.Source)!) };
  }

  private void AppendNewerAcrossSources(
    LogSourceDescriptor currentSource,
    List<(LogEvent Event, LogPhysicalLine Line)> events,
    HashSet<string> seen,
    int targetCount,
    int remainingBytes,
    CancellationToken cancellationToken)
  {
    if (events.Count >= targetCount || remainingBytes <= 0)
      return;

    var startIndex = _catalog.MainHistoryOldestFirst
      .ToList()
      .FindIndex(s => s.Identity == currentSource.Identity);
    if (startIndex < 0)
      return;

    for (var i = startIndex + 1; i < _catalog.MainHistoryOldestFirst.Count; i++)
    {
      var source = _catalog.MainHistoryOldestFirst[i];
      foreach (var line in LogUtf8LineScanner.ReadLinesForward(source, 0, null, cancellationToken))
      {
        if (cancellationToken.IsCancellationRequested)
          return;

        if (!line.IsComplete)
          break;

        remainingBytes -= Encoding.UTF8.GetByteCount(line.Text) + 1;
        if (remainingBytes < 0)
          return;

        var decoded = LogEventDecoder.Decode(line);
        if (!seen.Add(decoded.EventId))
          continue;

        events.Add((decoded, line));
        if (events.Count >= targetCount)
          return;
      }
    }
  }

  private List<(LogEvent Event, LogPhysicalLine Line)> CollectMainEventsNewestFirst(
    Dictionary<LogSourceIdentity, long>? endBoundaries,
    int targetCount,
    int maxBytes,
    CancellationToken cancellationToken,
    out bool hasMore,
    out bool stoppedEarly,
    LogSourceIdentity? cursorSourceExclusiveUpperBound)
  {
    hasMore = false;
    stoppedEarly = false;
    var collected = new List<(LogEvent Event, LogPhysicalLine Line)>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var remainingBytes = maxBytes;
    var reachedCursorSource = cursorSourceExclusiveUpperBound is null;

    foreach (var source in _catalog.MainHistoryNewestFirst)
    {
      if (cursorSourceExclusiveUpperBound is not null)
      {
        if (!reachedCursorSource)
        {
          if (source.Identity == cursorSourceExclusiveUpperBound)
            reachedCursorSource = true;
          else
            continue;
        }
      }

      if (cancellationToken.IsCancellationRequested)
      {
        stoppedEarly = true;
        return new List<(LogEvent Event, LogPhysicalLine Line)>();
      }

      var endOffset = endBoundaries?.TryGetValue(source.Identity, out var boundary) == true
        ? boundary
        : LogUtf8LineScanner.GetFileLength(source.FilePath);

      if (endOffset <= 0)
        continue;

      foreach (var line in LogUtf8LineScanner.ReadLinesBackward(source, endOffset, remainingBytes, cancellationToken))
      {
        if (cancellationToken.IsCancellationRequested)
        {
          stoppedEarly = true;
          return new List<(LogEvent Event, LogPhysicalLine Line)>();
        }

        remainingBytes -= Encoding.UTF8.GetByteCount(line.Text) + 1;
        var decoded = LogEventDecoder.Decode(line);
        if (!seen.Add(decoded.EventId))
          continue;

        collected.Add((decoded, line));
        if (collected.Count >= targetCount)
        {
          hasMore = HasMoreBefore(line, source);
          if (!hasMore)
            hasMore = HasOlderSourcesBefore(source);
          return collected;
        }

        if (remainingBytes <= 0)
        {
          hasMore = true;
          return collected;
        }
      }

      if (endBoundaries?.ContainsKey(source.Identity) == true)
        break;
    }

    hasMore = HasOlderSourcesBefore(_catalog.MainHistoryOldestFirst.FirstOrDefault());
    return collected;
  }

  private static DateTimeOffset ResolveStartupTimestamp(LogEvent startupEvent)
  {
    if (LogViewerComposer.TryParseTimestamp(startupEvent.Message, out var parsed))
      return new DateTimeOffset(parsed, TimeZoneInfo.Local.GetUtcOffset(parsed));

    return startupEvent.Timestamp;
  }

  private Dictionary<LogSourceIdentity, long> BuildEndBoundary(LogReadCursor cursor)
  {
    var boundaries = new Dictionary<LogSourceIdentity, long>();
    var source = FindSource(cursor.Source);
    if (source is null)
      return boundaries;

    boundaries[cursor.Source] = cursor.ByteOffset;

    var oldestFirst = _catalog.MainHistoryOldestFirst;
    for (var i = 0; i < oldestFirst.Count; i++)
    {
      if (oldestFirst[i].Identity != cursor.Source)
        continue;

      for (var j = 0; j < i; j++)
        boundaries[oldestFirst[j].Identity] = LogUtf8LineScanner.GetFileLength(oldestFirst[j].FilePath);

      break;
    }

    return boundaries;
  }

  private LogEventPage BuildPage(
    IReadOnlyList<(LogEvent Event, LogPhysicalLine Line)> events,
    bool hasOlder,
    bool hasNewer,
    IReadOnlyList<(LogEvent Event, LogPhysicalLine Line)>? cursorEvents = null)
  {
    if (events.Count == 0)
      return LogEventPage.Empty;

    // Paging advances physical files. Equal/out-of-order timestamps may change
    // display order, but must never move a cursor into the middle of its page.
    var sourceOrder = _catalog.MainHistoryOldestFirst.Select((source, index) => (source.Identity, index))
      .ToDictionary(item => item.Identity, item => item.index);
    var cursorSourceEvents = (cursorEvents ?? events)
      .Where(e => FindSource(e.Line.Source)?.Kind != LogSourceKind.StartupFallback)
      .OrderBy(e => sourceOrder.GetValueOrDefault(e.Line.Source))
      .ThenBy(e => e.Line.ByteOffset)
      .ToList();

    if (cursorSourceEvents.Count == 0)
    {
      return new LogEventPage(
        events.Select(e => e.Event).ToList(),
        null,
        null,
        hasOlder,
        hasNewer);
    }

    var first = cursorSourceEvents[0];
    var last = cursorSourceEvents[^1];
    var olderCursor = new LogReadCursor(first.Line.Source, first.Line.ByteOffset, first.Event.EventId);
    var newerCursor = new LogReadCursor(last.Line.Source, last.Line.ByteOffset, last.Event.EventId);

    return new LogEventPage(
      events.Select(e => e.Event).ToList(),
      olderCursor,
      newerCursor,
      hasOlder,
      hasNewer);
  }

  private bool HasEventsBefore((LogEvent Event, LogPhysicalLine Line)? entry, CancellationToken cancellationToken)
  {
    if (entry is null)
      return false;

    var source = FindSource(entry.Value.Line.Source);
    return source is not null && HasMoreBefore(entry.Value.Line, source);
  }

  private LogSourceDescriptor? FindSource(LogSourceIdentity identity)
    => _catalog.MainHistoryOldestFirst.FirstOrDefault(s => s.Identity == identity)
       ?? _catalog.StartupHistoryOldestFirst.FirstOrDefault(s => s.Identity == identity);

  private bool HasMoreBefore(LogPhysicalLine line, LogSourceDescriptor source)
  {
    if (line.ByteOffset > 0)
      return true;

    return HasOlderSourcesBefore(source);
  }

  private bool HasOlderSourcesBefore(LogSourceDescriptor? source)
  {
    if (source is null)
      return false;

    var index = _catalog.MainHistoryOldestFirst
      .ToList()
      .FindIndex(s => s.Identity == source.Identity);
    return index > 0;
  }

  private bool HasNewerAfter((LogEvent Event, LogPhysicalLine Line) entry, LogSourceDescriptor currentSource)
  {
    var line = entry.Line;
    var endOffset = LogUtf8LineScanner.GetLineEndOffset(currentSource.FilePath, line.ByteOffset);
    if (endOffset < LogUtf8LineScanner.GetFileLength(currentSource.FilePath))
      return true;

    var index = _catalog.MainHistoryOldestFirst
      .ToList()
      .FindIndex(s => s.Identity == currentSource.Identity);
    return index >= 0 && index < _catalog.MainHistoryOldestFirst.Count - 1;
  }

  private static int CompareEventsChronologically(LogEvent left, LogEvent right)
  {
    var compare = left.Timestamp.CompareTo(right.Timestamp);
    if (compare != 0)
      return compare;

    return string.Compare(left.EventId, right.EventId, StringComparison.Ordinal);
  }

  private IReadOnlyList<LogSourceSnapshotEntry> CaptureSnapshotSources(LogSourceCatalog catalog)
  {
    var sources = new List<LogSourceSnapshotEntry>();
    foreach (var source in EnumerateCatalogSources(catalog))
    {
      var capturedLength = LogUtf8LineScanner.GetFileLength(source.FilePath);
      sources.Add(new LogSourceSnapshotEntry(
        source.Identity,
        source.Kind,
        capturedLength,
        ComputeBoundaryFingerprint(source.FilePath, capturedLength)));
    }

    return sources;
  }

  private IReadOnlyList<ResolvedSnapshotSource> ResolveSnapshotSources(LogSourceSnapshot snapshot)
  {
    var catalog = LogSourceCatalog.Build(_configuredLogPath, _startupFallbackPath);
    var currentSources = EnumerateCatalogSources(catalog).ToArray();
    var resolved = new List<ResolvedSnapshotSource>(snapshot.Sources.Count);
    var seen = new HashSet<LogSourceIdentity>();
    foreach (var snapshotSource in snapshot.Sources)
    {
      if (!seen.Add(snapshotSource.Source))
        continue;

      var source = ResolveSnapshotSource(snapshotSource, currentSources);
      ValidateSnapshotSource(snapshotSource, source);
      resolved.Add(new ResolvedSnapshotSource(snapshotSource, source));
    }

    return resolved;
  }

  private static IEnumerable<LogSourceDescriptor> EnumerateCatalogSources(LogSourceCatalog catalog)
  {
    foreach (var startup in catalog.StartupHistoryOldestFirst)
      yield return startup;

    foreach (var source in catalog.MainHistoryOldestFirst)
      yield return source;
  }

  private static string ComputeSnapshotFingerprint(IReadOnlyList<LogSourceSnapshotEntry> sources)
  {
    var payload = string.Join(
      "\n",
      sources.Select(source => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{source.Source.CreatedAtUtc:O}|{source.Source.ContentHashHex}|{source.Kind}|{source.CapturedLength}|{source.BoundaryFingerprint}")));
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
  }

  private static string ComputeBoundaryFingerprint(string path, long endOffsetExclusive)
  {
    if (!File.Exists(path) || endOffsetExclusive <= 0)
      return Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant();

    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    var end = Math.Min(endOffsetExclusive, stream.Length);
    if (end <= 0)
      return Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant();

    var windowLength = (int)Math.Min(SnapshotBoundaryBytes, end);
    var buffer = new byte[windowLength];
    stream.Seek(end - windowLength, SeekOrigin.Begin);
    var read = 0;
    while (read < windowLength)
    {
      var chunkRead = stream.Read(buffer, read, windowLength - read);
      if (chunkRead <= 0)
        break;

      read += chunkRead;
    }

    if (read != buffer.Length)
      buffer = buffer[..read];

    return Convert.ToHexString(SHA256.HashData(buffer)).ToLowerInvariant();
  }

  private static LogSourceDescriptor ResolveSnapshotSource(
    LogSourceSnapshotEntry snapshotSource,
    IReadOnlyList<LogSourceDescriptor> currentSources)
  {
    var matches = currentSources
      .Where(source => source.Identity == snapshotSource.Source)
      .ToList();

    if (snapshotSource.Kind == LogSourceKind.StartupFallback)
      matches = matches.Where(source => source.Kind == LogSourceKind.StartupFallback).ToList();

    return matches.Count switch
    {
      1 => matches[0],
      0 => throw new LogSnapshotInvalidatedException($"Snapshot source '{snapshotSource.Source}' is missing."),
      _ => throw new LogSnapshotInvalidatedException($"Snapshot source '{snapshotSource.Source}' cannot be resolved uniquely."),
    };
  }

  private static void ValidateSnapshotSource(
    LogSourceSnapshotEntry snapshotSource,
    LogSourceDescriptor source)
  {
    var currentLength = LogUtf8LineScanner.GetFileLength(source.FilePath);
    if (currentLength < snapshotSource.CapturedLength)
    {
      throw new LogSnapshotInvalidatedException(
        $"Snapshot source '{snapshotSource.Source}' was truncated from {snapshotSource.CapturedLength} to {currentLength} bytes.");
    }

    var boundaryFingerprint = ComputeBoundaryFingerprint(source.FilePath, snapshotSource.CapturedLength);
    if (!string.Equals(boundaryFingerprint, snapshotSource.BoundaryFingerprint, StringComparison.Ordinal))
    {
      throw new LogSnapshotInvalidatedException(
        $"Snapshot source '{snapshotSource.Source}' no longer matches the captured boundary fingerprint.");
    }
  }

  private static IEnumerable<LogPhysicalLine> EnumerateSnapshotLinesForward(
    LogReadScope scope,
    IReadOnlyList<ResolvedSnapshotSource> sources,
    LogReadCursor? cursor,
    CancellationToken cancellationToken)
  {
    var sourceFilter = scope.Sources.Count == 0
      ? null
      : scope.Sources.ToHashSet();
    var cursorIndex = cursor is null
      ? 0
      : FindSnapshotSourceIndex(sources, cursor.Source);

    for (var i = cursorIndex; i < sources.Count; i++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var source = sources[i];
      if (sourceFilter is not null && !sourceFilter.Contains(source.Snapshot.Source))
        continue;

      var startOffset = cursor is not null && i == cursorIndex
        ? LogUtf8LineScanner.GetLineEndOffset(source.Descriptor.FilePath, cursor.ByteOffset)
        : 0;

      foreach (var line in LogUtf8LineScanner.ReadLinesForward(
                 source.Descriptor,
                 startOffset,
                 source.Snapshot.CapturedLength,
                 cancellationToken))
      {
        cancellationToken.ThrowIfCancellationRequested();
        yield return line;
      }
    }
  }

  private static IEnumerable<LogPhysicalLine> EnumerateSnapshotLinesBackward(
    LogReadScope scope,
    IReadOnlyList<ResolvedSnapshotSource> sources,
    LogReadCursor? cursor,
    CancellationToken cancellationToken)
  {
    var sourceFilter = scope.Sources.Count == 0
      ? null
      : scope.Sources.ToHashSet();
    var cursorIndex = cursor is null
      ? sources.Count - 1
      : FindSnapshotSourceIndex(sources, cursor.Source);

    for (var i = cursorIndex; i >= 0; i--)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var source = sources[i];
      if (sourceFilter is not null && !sourceFilter.Contains(source.Snapshot.Source))
        continue;

      var endOffset = cursor is not null && i == cursorIndex
        ? cursor.ByteOffset
        : source.Snapshot.CapturedLength;
      if (endOffset <= 0)
        continue;

      var maxBytes = (int)Math.Min(int.MaxValue, Math.Max(endOffset, 1));
      foreach (var line in LogUtf8LineScanner.ReadLinesBackward(
                 source.Descriptor,
                 endOffset,
                 maxBytes,
                 cancellationToken))
      {
        cancellationToken.ThrowIfCancellationRequested();
        yield return line;
      }
    }
  }

  private static int FindSnapshotSourceIndex(
    IReadOnlyList<ResolvedSnapshotSource> sources,
    LogSourceIdentity identity)
  {
    for (var i = 0; i < sources.Count; i++)
    {
      if (sources[i].Snapshot.Source == identity)
        return i;
    }

    throw new LogSnapshotInvalidatedException($"Snapshot cursor source '{identity}' is unavailable.");
  }

  private sealed record ResolvedSnapshotSource(
    LogSourceSnapshotEntry Snapshot,
    LogSourceDescriptor Descriptor);

  private static int NormalizeTargetCount(int targetEventCount)
    => targetEventCount > 0 ? targetEventCount : DefaultTargetEventCount;

  private static int NormalizeMaxBytes(int maxBytes)
    => maxBytes > 0 ? maxBytes : DefaultMaxBytes;

  public static long GetFileLength(string path)
    => LogUtf8LineScanner.GetFileLength(path);

  public static LogFileSlice ReadFromOffset(string path, long startOffset)
  {
    LogTailReaderTestMetrics.MaybeDelayForTests();
    LogTailReaderTestMetrics.RecordReadFromOffset();

    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      return new LogFileSlice(0, string.Empty);

    try
    {
      using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite);
      var length = stream.Length;
      if (startOffset < 0 || startOffset > length) startOffset = 0;
      stream.Seek(startOffset, SeekOrigin.Begin);
      using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
      var text = reader.ReadToEnd();
      return new LogFileSlice(length, text);
    }
    catch
    {
      return new LogFileSlice(0, string.Empty);
    }
  }

  public static LogFileSlice ReadRecentTail(string path, int maxBytes = DefaultMaxBytes)
  {
    LogTailReaderTestMetrics.MaybeDelayForTests();

    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      return new LogFileSlice(0, string.Empty);

    if (maxBytes <= 0)
      return ReadFromOffset(path, 0);

    try
    {
      using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite);
      var length = stream.Length;
      if (length <= maxBytes)
        return ReadFromOffset(path, 0);

      var startOffset = length - maxBytes;
      stream.Seek(startOffset, SeekOrigin.Begin);
      using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
      var text = reader.ReadToEnd();
      var newline = text.IndexOf('\n');
      if (newline >= 0 && newline + 1 < text.Length)
        text = text[(newline + 1)..];

      return new LogFileSlice(length, text);
    }
    catch
    {
      return new LogFileSlice(0, string.Empty);
    }
  }

  public static LogInitialView ReadInitialView(
    string logPath,
    string? fallbackLogPath,
    int maxTailBytes = DefaultMaxBytes)
  {
    var primary = ReadRecentHistory(logPath, maxTailBytes);
    string? fallbackTail = null;
    if (!string.IsNullOrWhiteSpace(fallbackLogPath) && File.Exists(fallbackLogPath))
      fallbackTail = ReadRecentTail(fallbackLogPath, Math.Min(maxTailBytes, 32_768)).Text;

    var merged = LogViewerComposer.MergeChronologically(primary.Text, fallbackTail);
    var mergedExceedsBudget = maxTailBytes > 0 && Encoding.UTF8.GetByteCount(merged) > maxTailBytes;
    return new LogInitialView(
      GetFileLength(logPath),
      merged,
      primary.IsTruncated || mergedExceedsBudget);
  }

  private static (string Text, bool IsTruncated) ReadRecentHistory(string logPath, int maxBytes)
  {
    var sources = LogSourceCatalog.EnumerateMainHistoryPathsNewestFirst(logPath);
    if (sources.Count == 0)
      return (string.Empty, false);

    if (maxBytes <= 0)
    {
      var allText = sources
        .AsEnumerable()
        .Reverse()
        .Select(path => ReadFromOffset(path, 0).Text);
      return (string.Join(Environment.NewLine, allText), false);
    }

    var remainingBytes = maxBytes;
    var selectedNewestFirst = new List<string>();
    var isTruncated = false;
    var processed = 0;

    foreach (var source in sources)
    {
      if (remainingBytes <= 0)
      {
        isTruncated = true;
        break;
      }

      var length = GetFileLength(source);
      var slice = ReadRecentTail(source, remainingBytes);
      if (!string.IsNullOrEmpty(slice.Text))
        selectedNewestFirst.Add(slice.Text);

      processed++;
      if (length > remainingBytes)
      {
        isTruncated = true;
        break;
      }

      remainingBytes -= (int)length;
    }

    if (processed < sources.Count)
      isTruncated = true;

    return (
      string.Join(Environment.NewLine, selectedNewestFirst.AsEnumerable().Reverse()),
      isTruncated);
  }

  public static string? ReadFullWithFallback(string logPath, string? fallbackLogPath)
  {
    var primary = ReadFromOffset(logPath, 0);
    string? fallbackText = null;
    if (!string.IsNullOrWhiteSpace(fallbackLogPath) && File.Exists(fallbackLogPath))
      fallbackText = ReadFromOffset(fallbackLogPath, 0).Text;

    var merged = LogViewerComposer.MergeChronologically(primary.Text, fallbackText);
    return string.IsNullOrEmpty(merged) ? null : merged;
  }
}

public static class LogViewerScrollState
{
  public const int TailThresholdChars = 96;

  public static bool IsNearTail(int textLength, int charIndexFromBottom)
  {
    if (textLength <= 0) return true;
    if (charIndexFromBottom < 0) return true;
    return textLength - charIndexFromBottom <= TailThresholdChars;
  }

  public static bool IsRichTextBoxAtTail(RichTextBox richTextBox)
  {
    if (richTextBox.IsDisposed) return true;
    if (richTextBox.TextLength <= 0) return true;
    var point = new Point(0, Math.Max(0, richTextBox.ClientSize.Height - 2));
    var index = richTextBox.GetCharIndexFromPosition(point);
    return IsNearTail(richTextBox.TextLength, index);
  }

  public static bool IsTextBoxAtTail(TextBox textBox)
  {
    if (textBox.IsDisposed) return true;
    if (textBox.TextLength <= 0) return true;
    var point = new Point(0, Math.Max(0, textBox.ClientSize.Height - 2));
    var index = textBox.GetCharIndexFromPosition(point);
    return IsNearTail(textBox.TextLength, index);
  }
}
