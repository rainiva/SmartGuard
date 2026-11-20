using System.Text;

namespace SmartGuard.Configuration;

internal enum DiagnosticReadStatus { Included, Missing, Unreadable, TooLarge, RedactionFailed }

internal sealed record DiagnosticTailReadResult(string? Text, DiagnosticReadStatus Status,
  bool Truncated, long? SourceBytes, int IncludedBytes, int ReadBytes = 0);

/// <summary>Supplemental text only. Main events remain owned by ILogEventSource.</summary>
internal static class DiagnosticLogTailReader
{
  internal const int MaxBytes = 256 * 1024;
  private const int BoundaryBytes = 4096;

  internal static DiagnosticTailReadResult ReadTail(string path, int maxBytes, CancellationToken token)
  {
    var selected = new List<string>();
    var readBudget = maxBytes + BoundaryBytes;
    var outputBudget = maxBytes;
    var sourceBytes = 0L;
    var readBytes = 0;
    var truncated = false;
    var unreadable = false;
    var paths = LogSourceCatalog.EnumerateSupplementalHistoryPathsNewestFirst(path);
    if (paths.Count == 0) return new(null, DiagnosticReadStatus.Missing, false, null, 0);
    foreach (var source in paths)
    {
      token.ThrowIfCancellationRequested();
      try
      {
        using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        sourceBytes += stream.Length;
        if (readBudget <= 0 || outputBudget <= 0) { truncated |= stream.Length > 0; continue; }
        var count = (int)Math.Min(stream.Length, readBudget);
        var start = stream.Length - count;
        stream.Seek(start, SeekOrigin.Begin);
        var bytes = new byte[count];
        var received = 0;
        while (received < count)
        {
          token.ThrowIfCancellationRequested();
          var read = stream.Read(bytes, received, Math.Min(8192, count - received));
          if (read == 0) { truncated = true; break; }
          received += read; readBytes += read; readBudget -= read;
        }
        // A single boundary allowance is shared by the whole kind, not each
        // archive. Both cut prefixes and unterminated suffixes are discarded.
        var beginsAtBoundary = start == 0;
        var first = beginsAtBoundary ? 0 : Array.IndexOf(bytes, (byte)'\n', 0, received) + 1;
        if (!beginsAtBoundary && first == 0) first = received;
        var last = received == 0 ? -1 : Array.LastIndexOf(bytes, (byte)'\n', received - 1, received);
        truncated |= start > 0 || last != received - 1;
        var lines = new List<string>();
        for (var offset = first; offset <= last;)
        {
          var end = Array.IndexOf(bytes, (byte)'\n', offset, last + 1 - offset);
          if (end < 0) break;
          try
          {
            var length = end - offset;
            if (length > 0 && bytes[end - 1] == '\r') length--;
            var text = new UTF8Encoding(false, true).GetString(bytes, offset, length);
            if (text.IndexOf('\0') >= 0) truncated = true;
            else if (length > 0) lines.Add(text);
          }
          catch (DecoderFallbackException) { truncated = true; }
          offset = end + 1;
        }
        var accepted = new List<string>();
        for (var i = lines.Count - 1; i >= 0; i--)
        {
          var bytesNeeded = Encoding.UTF8.GetByteCount(lines[i]) + 1;
          if (bytesNeeded > outputBudget) { truncated = true; continue; }
          accepted.Add(lines[i]); outputBudget -= bytesNeeded;
        }
        accepted.Reverse(); selected.InsertRange(0, accepted);
      }
      catch (OperationCanceledException) { throw; }
      catch { unreadable = true; }
    }
    var result = string.Join("\n", selected) + (selected.Count > 0 ? "\n" : "");
    var status = selected.Count > 0 ? DiagnosticReadStatus.Included : unreadable ? DiagnosticReadStatus.Unreadable
      : truncated ? DiagnosticReadStatus.TooLarge : DiagnosticReadStatus.Included;
    return new(result, status, truncated || unreadable, sourceBytes, Encoding.UTF8.GetByteCount(result), readBytes);
  }
}
