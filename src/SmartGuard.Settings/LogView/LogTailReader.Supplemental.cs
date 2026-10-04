using SmartGuard.Configuration;
using System.IO;

namespace SmartGuard.Settings.LogView;

public sealed partial class LogTailReader
{
  // Only the two visible page boundaries are retained, never a page history.
  // Per-source offsets let sequential paging cross independent main/fallback
  // histories without rereading an ever-growing prefix or losing older rows.
  private LogReadCursor? _mergedOlderCursor;
  private LogReadCursor? _mergedNewerCursor;
  private LogEvent? _mergedOlderAnchor;
  private LogEvent? _mergedNewerAnchor;
  private Dictionary<LogSourceIdentity, long> _mergedOlderEnds = new();
  private Dictionary<LogSourceIdentity, long> _mergedNewerStarts = new();
  private LogSourceCatalog? _mergedCatalog;

  private LogEventPage ReadMergedHistory(LogReadRequest request, CancellationToken token)
  {
    if (token.IsCancellationRequested) return LogEventPage.Empty;
    var target = NormalizeTargetCount(request.TargetEventCount);
    var budget = NormalizeMaxBytes(request.MaxBytes);
    // Like the main reader, keep source identities stable during append. A
    // newly created fallback may join once; rotation is resolved by the
    // existing fresh-reader/snapshot lifecycle, not a second identity scheme.
    var catalog = _mergedCatalog ?? _catalog;
    if (catalog.StartupHistoryOldestFirst.Count == 0 && File.Exists(_startupFallbackPath))
    {
      var fresh = LogSourceCatalog.Build(_configuredLogPath, _startupFallbackPath);
      catalog = new LogSourceCatalog(_configuredLogPath, catalog.MainHistoryOldestFirst, fresh.StartupFallback, fresh.StartupHistoryOldestFirst);
      _mergedCatalog = catalog;
    }
    var allSources = EnumerateCatalogSources(catalog).ToArray();
    var continuingOlder = request.Direction == LogReadDirection.Older && request.Cursor is not null && request.Cursor == _mergedOlderCursor;
    var continuingNewer = request.Direction == LogReadDirection.Newer && request.Cursor is not null && request.Cursor == _mergedNewerCursor;
    LogEvent? anchor = null;
    if (request.Cursor is { } cursor)
    {
      anchor = continuingOlder ? _mergedOlderAnchor : continuingNewer ? _mergedNewerAnchor : null;
      var source = allSources.FirstOrDefault(item => item.Identity == cursor.Source);
      if (source is null) return LogEventPage.Empty;
      if (anchor is null)
      {
        var end = Math.Min(LogUtf8LineScanner.GetFileLength(source.FilePath), cursor.ByteOffset + Math.Min(budget, 4096));
        var line = LogUtf8LineScanner.ReadLinesForward(source, cursor.ByteOffset, end, token).FirstOrDefault();
        if (line is null || !line.IsComplete) return LogEventPage.Empty;
        anchor = DecodeHistoryLine(source, line);
      }
    }

    var collected = new List<(LogEvent Event, LogPhysicalLine Line)>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var scanned = new List<(LogEvent Event, LogPhysicalLine Line)>();
    var olderEnds = allSources.ToDictionary(source => source.Identity, source => LogUtf8LineScanner.GetFileLength(source.FilePath));
    var newerStarts = new Dictionary<LogSourceIdentity, long>();
    var truncated = false;
    // Share the request byte budget between the two histories. A missing
    // history contributes no IO and leaves the full budget to the other one.
    var groups = new[] { catalog.MainHistoryNewestFirst, catalog.StartupHistoryOldestFirst.Reverse().ToArray() }.Where(group => group.Count > 0).ToArray();
    foreach (var history in groups)
    {
      var remaining = budget / groups.Length;
      foreach (var source in history)
      {
        if (remaining <= 0) { truncated = true; break; }
        var length = LogUtf8LineScanner.GetFileLength(source.FilePath);
        var end = continuingOlder && _mergedOlderEnds.TryGetValue(source.Identity, out var savedEnd) ? Math.Min(length, savedEnd)
          : request.Direction == LogReadDirection.Older && request.Cursor?.Source == source.Identity
          ? request.Cursor.ByteOffset : length;
        olderEnds[source.Identity] = end;
        var start = continuingNewer && _mergedNewerStarts.TryGetValue(source.Identity, out var savedStart) ? Math.Min(length, savedStart) : 0;
        var window = (int)Math.Min(remaining, continuingNewer ? length - start : end);
        var lines = continuingNewer
          ? LogUtf8LineScanner.ReadLinesForward(source, start, start + window, token)
          : LogUtf8LineScanner.ReadLinesBackward(source, end, window, token);
        foreach (var line in lines)
        {
          if (token.IsCancellationRequested) return LogEventPage.Empty;
          if (!line.IsComplete) continue;
          var decoded = DecodeHistoryLine(source, line);
          scanned.Add((decoded, line));
          if (anchor is not null)
          {
            var comparison = CompareEventsChronologically(decoded, anchor);
            if (request.Direction == LogReadDirection.Older && comparison >= 0) continue;
            if (request.Direction == LogReadDirection.Newer && comparison <= 0) continue;
          }
          if (seen.Add(decoded.EventId)) collected.Add((decoded, line));
        }
        remaining -= window;
        truncated |= continuingNewer ? length - start > window : end > window;
        newerStarts[source.Identity] = continuingNewer ? start : length;
      }
    }
    collected.Sort((a, b) => CompareEventsChronologically(a.Event, b.Event));
    var more = collected.Count > target || truncated;
    var page = request.Direction == LogReadDirection.Newer ? collected.Take(target).ToArray() : collected.TakeLast(target).ToArray();
    if (page.Length == 0) return LogEventPage.Empty;
    var first = page[0]; var last = page[^1];
    var olderCursor = new LogReadCursor(first.Line.Source, first.Line.ByteOffset, first.Event.EventId);
    var newerCursor = new LogReadCursor(last.Line.Source, last.Line.ByteOffset, last.Event.EventId);
    var advanced = new Dictionary<LogSourceIdentity, long>();
    foreach (var item in scanned)
    {
      if (CompareEventsChronologically(item.Event, first.Event) >= 0)
        olderEnds[item.Line.Source] = Math.Min(olderEnds[item.Line.Source], item.Line.ByteOffset);
      if (CompareEventsChronologically(item.Event, last.Event) > 0)
        newerStarts[item.Line.Source] = Math.Min(newerStarts[item.Line.Source], item.Line.ByteOffset);
      else if (continuingNewer)
        advanced[item.Line.Source] = Math.Max(advanced.GetValueOrDefault(item.Line.Source), item.Line.ByteOffset);
    }
    foreach (var pair in advanced)
      newerStarts[pair.Key] = Math.Max(newerStarts[pair.Key],
        LogUtf8LineScanner.GetLineEndOffset(allSources.First(source => source.Identity == pair.Key).FilePath, pair.Value));
    _mergedOlderCursor = olderCursor; _mergedOlderAnchor = first.Event; _mergedOlderEnds = olderEnds;
    _mergedNewerCursor = newerCursor; _mergedNewerAnchor = last.Event; _mergedNewerStarts = newerStarts;
    return new LogEventPage(page.Select(item => item.Event).ToArray(),
      olderCursor, newerCursor,
      request.Direction != LogReadDirection.Newer && more, request.Direction == LogReadDirection.Newer && more);
  }

  private static LogEvent DecodeHistoryLine(LogSourceDescriptor source, LogPhysicalLine line)
  {
    var decoded = LogEventDecoder.Decode(line);
    return source.Kind == LogSourceKind.StartupFallback && !LogJsonlFormatter.TryParseLine(line.Text, out _)
      ? decoded with { Timestamp = ResolveStartupTimestamp(decoded) } : decoded;
  }
}
