using System.Text;

namespace SmartGuard.Configuration;

public static class LogUtf8LineScanner
{
  public static long GetFileLength(string path)
  {
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      return 0;

    try
    {
      return new FileInfo(path).Length;
    }
    catch
    {
      return 0;
    }
  }

  public static IEnumerable<LogPhysicalLine> ReadLinesForward(
    LogSourceDescriptor source,
    long startOffset,
    long? endOffsetExclusive,
    CancellationToken cancellationToken)
  {
    if (!File.Exists(source.FilePath))
      yield break;

    using var stream = OpenReadStream(source.FilePath);
    var fileLength = stream.Length;
    if (startOffset < 0 || startOffset > fileLength)
      startOffset = 0;

    var end = endOffsetExclusive ?? fileLength;
    if (end > fileLength)
      end = fileLength;
    if (startOffset >= end)
      yield break;

    stream.Seek(startOffset, SeekOrigin.Begin);
    var remaining = end - startOffset;
    var buffer = new byte[Math.Min(remaining, 8192)];
    var carry = new List<byte>();
    var offset = startOffset;
    var ordinalTracker = new Dictionary<(long Offset, string Text), int>();

    while (remaining > 0)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var toRead = (int)Math.Min(buffer.Length, remaining);
      var read = stream.Read(buffer, 0, toRead);
      if (read <= 0)
        break;

      remaining -= read;
      AppendBytes(carry, buffer.AsSpan(0, read));

      while (TryTakeCompleteLine(carry, out var lineBytes, out var consumed))
      {
        carry.RemoveRange(0, consumed);
        var text = DecodeUtf8(lineBytes);
        var lineOffset = offset;
        offset += consumed;
        var ordinal = NextOrdinal(ordinalTracker, lineOffset, text);
        yield return new LogPhysicalLine(source.Identity, lineOffset, text, true, ordinal);
      }
    }

    if (carry.Count > 0)
    {
      var text = DecodeUtf8(carry.ToArray());
      var ordinal = NextOrdinal(ordinalTracker, offset, text);
      var isComplete = end >= fileLength && EndsWithNewline(carry);
      yield return new LogPhysicalLine(source.Identity, offset, text, isComplete, ordinal);
    }
  }

  public static IEnumerable<LogPhysicalLine> ReadLinesBackward(
    LogSourceDescriptor source,
    long endOffsetExclusive,
    int maxBytes,
    CancellationToken cancellationToken)
  {
    if (!File.Exists(source.FilePath))
      yield break;

    using var stream = OpenReadStream(source.FilePath);
    var fileLength = stream.Length;
    if (fileLength <= 0)
      yield break;

    if (endOffsetExclusive <= 0)
      yield break;

    if (endOffsetExclusive > fileLength)
      endOffsetExclusive = fileLength;

    var scanStart = Math.Max(0, endOffsetExclusive - Math.Max(maxBytes, 1));
    scanStart = AlignUtf8Boundary(stream, scanStart);
    stream.Seek(scanStart, SeekOrigin.Begin);

    var bytesToRead = endOffsetExclusive - scanStart;
    var chunk = new byte[bytesToRead];
    var readTotal = 0;
    while (readTotal < bytesToRead)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var read = stream.Read(chunk, readTotal, (int)(bytesToRead - readTotal));
      if (read <= 0)
        break;
      readTotal += read;
    }

    if (readTotal <= 0)
      yield break;

    var window = chunk.AsSpan(0, readTotal).ToArray();
    var relativeEnd = endOffsetExclusive - scanStart;
    if (relativeEnd > window.Length)
      relativeEnd = window.Length;

    var lineEnds = new List<int>();
    for (var i = 0; i < relativeEnd; i++)
    {
      if (window[i] == (byte)'\n')
        lineEnds.Add(i);
    }

    var ordinalTracker = new Dictionary<(long Offset, string Text), int>();
    for (var i = lineEnds.Count - 1; i >= 0; i--)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var lineEnd = lineEnds[i];
      var prevNewline = i > 0 ? lineEnds[i - 1] + 1 : 0;
      var lineStart = prevNewline;
      var lineLength = lineEnd - lineStart;
      if (lineLength <= 0)
        continue;

      var lineBytes = new byte[lineLength];
      Array.Copy(window, lineStart, lineBytes, 0, lineLength);
      if (lineBytes.Length > 0 && lineBytes[^1] == (byte)'\r')
        lineBytes = lineBytes[..^1];

      var text = DecodeUtf8(lineBytes);
      var lineOffset = scanStart + lineStart;
      var ordinal = NextOrdinal(ordinalTracker, lineOffset, text);
      yield return new LogPhysicalLine(source.Identity, lineOffset, text, true, ordinal);
    }

    if (scanStart > 0 && lineEnds.Count > 0 && lineEnds[0] > 0 && lineEnds[0] < relativeEnd)
    {
      var firstLineBytes = new byte[lineEnds[0]];
      Array.Copy(window, 0, firstLineBytes, 0, lineEnds[0]);
      if (firstLineBytes.Length > 0 && firstLineBytes[^1] == (byte)'\r')
        firstLineBytes = firstLineBytes[..^1];

      var text = DecodeUtf8(firstLineBytes);
      if (!string.IsNullOrEmpty(text))
      {
        var ordinal = NextOrdinal(ordinalTracker, scanStart, text);
        yield return new LogPhysicalLine(source.Identity, scanStart, text, true, ordinal);
      }
    }
  }

  public static long GetLineEndOffset(string path, long lineStartOffset)
  {
    if (!File.Exists(path))
      return lineStartOffset;

    using var stream = OpenReadStream(path);
    if (lineStartOffset < 0 || lineStartOffset >= stream.Length)
      return lineStartOffset;

    stream.Seek(lineStartOffset, SeekOrigin.Begin);
    var offset = lineStartOffset;
    var buffer = new byte[4096];
    while (offset < stream.Length)
    {
      var read = stream.Read(buffer, 0, buffer.Length);
      if (read <= 0)
        break;

      for (var i = 0; i < read; i++)
      {
        offset++;
        if (buffer[i] == (byte)'\n')
          return offset;
      }
    }

    return stream.Length;
  }

  private static FileStream OpenReadStream(string path)
    => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

  private static long AlignUtf8Boundary(FileStream stream, long offset)
  {
    if (offset <= 0)
      return 0;

    stream.Seek(offset, SeekOrigin.Begin);
    var probe = new byte[4];
    var read = stream.Read(probe, 0, Math.Min(probe.Length, (int)(stream.Length - offset)));
    if (read <= 0)
      return offset;

    for (var shift = 0; shift < read; shift++)
    {
      if ((probe[shift] & 0xC0) != 0x80)
        return offset + shift;
    }

    return offset;
  }

  private static void AppendBytes(List<byte> carry, ReadOnlySpan<byte> bytes)
  {
    foreach (var b in bytes)
      carry.Add(b);
  }

  private static bool TryTakeCompleteLine(List<byte> carry, out byte[] lineBytes, out int consumed)
  {
    lineBytes = Array.Empty<byte>();
    consumed = 0;

    for (var i = 0; i < carry.Count; i++)
    {
      if (carry[i] != (byte)'\n')
        continue;

      var lineLength = i + 1;
      lineBytes = carry.Take(lineLength).ToArray();
      if (lineBytes.Length >= 2 && lineBytes[^2] == (byte)'\r')
        lineBytes = lineBytes[..^2];
      else
        lineBytes = lineBytes[..^1];
      consumed = i + 1;
      return true;
    }

    return false;
  }

  private static bool EndsWithNewline(List<byte> carry)
    => carry.Count > 0 && carry[^1] == (byte)'\n';

  private static string DecodeUtf8(ReadOnlySpan<byte> bytes)
    => bytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bytes);

  private static int NextOrdinal(Dictionary<(long Offset, string Text), int> tracker, long offset, string text)
  {
    var key = (offset, text);
    if (!tracker.TryGetValue(key, out var ordinal))
    {
      tracker[key] = 0;
      return 0;
    }

    ordinal++;
    tracker[key] = ordinal;
    return ordinal;
  }
}
