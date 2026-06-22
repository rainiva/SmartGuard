namespace SmartGuard.LogViewer;

internal sealed class LogViewerSession
{
  public required string LogPath { get; init; }
  public string? FallbackLogPath { get; init; }
  public required RichTextBox LogView { get; init; }
  public required ToolStripStatusLabel StatusLabel { get; init; }
  public long PrimaryFileLength { get; set; } = -1;
  public int LineCount { get; set; }
  public bool FollowTail { get; set; } = true;
  public DateTime? LastStatusRefresh { get; set; }

  public void RefreshView()
  {
    if (!FollowTail && LogViewerScrollState.IsRichTextBoxAtTail(LogView))
      FollowTail = true;

    var length = LogTailReader.GetFileLength(LogPath);
    if (length <= 0)
    {
      if (LogView.TextLength > 0)
        LogViewerRichTextRenderer.SetText(LogView, string.Empty, scrollToTail: false);
      PrimaryFileLength = 0;
      LineCount = 0;
      StatusLabel.Text = $"暂无日志 | {LogPath}";
      return;
    }

    if (PrimaryFileLength >= 0 && length == PrimaryFileLength)
    {
      var statusNow = DateTime.Now;
      if (LastStatusRefresh is null || (statusNow - LastStatusRefresh.Value).TotalSeconds >= 5)
      {
        LastStatusRefresh = statusNow;
        StatusLabel.Text = $"刷新: {statusNow:HH:mm:ss} | {LineCount} 行 | {LogPath}";
      }

      return;
    }

    var changed = false;
    var scrollTail = FollowTail;

    if (PrimaryFileLength < 0 || length < PrimaryFileLength)
    {
      var initial = LogTailReader.ReadInitialView(LogPath, FallbackLogPath);
      LogViewerRichTextRenderer.SetText(
        LogView,
        LogLineDisplayFormatter.FormatText(initial.Text),
        scrollTail);
      PrimaryFileLength = length > 0 ? length : initial.FileLength;
      changed = true;
    }
    else if (length > PrimaryFileLength)
    {
      var delta = LogTailReader.ReadFromOffset(LogPath, PrimaryFileLength);
      var formattedDelta = LogLineDisplayFormatter.FormatText(
        LogViewerDelta.PrepareForAppend(LogView.Text, delta.Text));
      LogViewerRichTextRenderer.AppendText(LogView, formattedDelta, scrollTail);
      PrimaryFileLength = length;
      changed = true;
    }

    if (changed)
    {
      TrimDisplayedTextIfNeeded();
      LineCount = LogView.Text.Split(['\r', '\n'], StringSplitOptions.None).Length;
    }

    var now = DateTime.Now;
    if (changed || LastStatusRefresh is null || (now - LastStatusRefresh.Value).TotalSeconds >= 5)
    {
      LastStatusRefresh = now;
      StatusLabel.Text = $"刷新: {now:HH:mm:ss} | {LineCount} 行 | {LogPath}";
    }
  }

  private void TrimDisplayedTextIfNeeded()
  {
    if (LogView.TextLength <= LogViewerTextTrimmer.DefaultMaxCachedBytes)
      return;

    var trimmed = LogViewerTextTrimmer.TrimToMaxBytes(LogView.Text);
    LogViewerRichTextRenderer.SetText(
      LogView,
      LogLineDisplayFormatter.FormatText(trimmed),
      FollowTail);
  }
}
