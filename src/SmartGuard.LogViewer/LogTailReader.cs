using System.Text;

namespace SmartGuard.LogViewer;

public sealed record LogFileSlice(long Length, string Text);

public static class LogTailReader
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

  public static LogFileSlice ReadFromOffset(string path, long startOffset)
  {
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

  public static LogFileSlice ReadRecentTail(string path, int maxBytes = 262_144)
  {
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

  public static (long FileLength, string Text) ReadInitialView(
    string logPath,
    string? fallbackLogPath,
    int maxTailBytes = 262_144)
  {
    var primary = ReadRecentTail(logPath, maxTailBytes);
    string? fallbackTail = null;
    if (!string.IsNullOrWhiteSpace(fallbackLogPath) && File.Exists(fallbackLogPath))
      fallbackTail = ReadRecentTail(fallbackLogPath, Math.Min(maxTailBytes, 32_768)).Text;

    var merged = LogViewerComposer.MergeChronologically(primary.Text, fallbackTail);
    return (primary.Length, merged);
  }

  public static string? ReadFullWithFallback(string logPath, string? fallbackLogPath)
  {
    var primary = ReadFromOffset(logPath, 0);
    string? fallbackText = null;
    if (!string.IsNullOrWhiteSpace(fallbackLogPath) && File.Exists(fallbackLogPath))
    {
      fallbackText = ReadFromOffset(fallbackLogPath, 0).Text;
    }

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
