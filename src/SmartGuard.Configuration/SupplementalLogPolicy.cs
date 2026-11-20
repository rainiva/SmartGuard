using System.Text;

namespace SmartGuard.Configuration;

/// <summary>Fixed personal-project budgets, independent of user main-log retention.</summary>
internal static class SupplementalLogPolicy
{
  internal const int BatchMaxBytes = 128 * 1024;
  internal const int RingEntryMaxBytes = 4 * 1024;
  internal const int RingTotalMaxBytes = 2 * 1024 * 1024;
  internal const int MaintenanceCandidateLimit = 32;
  internal static readonly LogRetentionPolicy Retention = new(1024 * 1024, 7, 3, 4 * 1024 * 1024);
  private const int MarkerReserveBytes = 4096;

  internal readonly record struct Selection(IReadOnlyList<string> Lines, int OriginalCount);

  internal static Selection SelectBatch(IReadOnlyList<string> lines)
  {
    var selected = new List<string>();
    var remaining = BatchMaxBytes - MarkerReserveBytes;
    var originals = 0;
    var changed = false;
    for (var i = lines.Count - 1; i >= 0; i--)
    {
      var line = lines[i];
      var bytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
      var original = bytes <= BatchMaxBytes - MarkerReserveBytes;
      if (!original) { line = BoundRingLine(line); bytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length; changed = true; }
      if (bytes > remaining) { changed = true; continue; }
      remaining -= bytes;
      selected.Add(line);
      if (original) originals++;
    }
    selected.Reverse();
    if (changed) selected.Insert(0, TruncationMarker($"附加日志批次已截断；原记录保留 {originals}/{lines.Count}"));
    return new(selected, originals);
  }

  internal static string BoundRingLine(string line)
    => Encoding.UTF8.GetByteCount(line) <= RingEntryMaxBytes ? line
      : TruncationMarker("原记录已截断：" + Utf8Prefix(line, 512));

  internal static string TruncationMarker(string message)
    => LogJsonlFormatter.FormatLine(LogStructuredSink.CreateComponentEvent(LogSeverity.Warn,
      LogComponentRegistry.Configuration, LogChannelRegistry.Diagnostics, EventNameRegistry.LogDeduplicationEmitted, message));

  private static string Utf8Prefix(string text, int maxBytes)
  {
    var result = new StringBuilder();
    var used = 0;
    foreach (var rune in text.EnumerateRunes())
    {
      if (used + rune.Utf8SequenceLength > maxBytes) break;
      result.Append(rune.ToString());
      used += rune.Utf8SequenceLength;
    }
    return result.ToString();
  }
}
