namespace SmartGuard.LogViewer;

public static class LogViewerDelta
{
  public static string PrepareForAppend(string existingText, string delta)
  {
    if (string.IsNullOrEmpty(delta)) return delta;
    if (string.IsNullOrEmpty(existingText)) return delta;
    if (delta.StartsWith('\n') || delta.StartsWith("\r\n", StringComparison.Ordinal)) return delta;
    if (existingText.EndsWith('\n') || existingText.EndsWith("\r\n", StringComparison.Ordinal)) return delta;
    return Environment.NewLine + delta;
  }
}
