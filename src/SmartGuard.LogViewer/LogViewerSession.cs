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

    var snapshot = LogTailReader.ReadFromOffset(LogPath, 0);
    if (snapshot.Length <= 0 && string.IsNullOrEmpty(snapshot.Text))
    {
      if (LogView.TextLength > 0)
        LogViewerRichTextRenderer.SetText(LogView, string.Empty, scrollToTail: false);
      PrimaryFileLength = 0;
      LineCount = 0;
      StatusLabel.Text = $"暂无日志 | {LogPath}";
      return;
    }

    var changed = false;
    var scrollTail = FollowTail;

    if (PrimaryFileLength < 0 || snapshot.Length < PrimaryFileLength)
    {
      var initial = LogTailReader.ReadInitialView(LogPath, FallbackLogPath);
      LogViewerRichTextRenderer.SetText(
        LogView,
        LogLineDisplayFormatter.FormatText(initial.Text),
        scrollTail);
      PrimaryFileLength = snapshot.Length > 0 ? snapshot.Length : initial.FileLength;
      changed = true;
    }
    else if (snapshot.Length > PrimaryFileLength)
    {
      var delta = LogTailReader.ReadFromOffset(LogPath, PrimaryFileLength);
      var formattedDelta = LogLineDisplayFormatter.FormatText(
        LogViewerDelta.PrepareForAppend(LogView.Text, delta.Text));
      LogViewerRichTextRenderer.AppendText(LogView, formattedDelta, scrollTail);
      PrimaryFileLength = snapshot.Length;
      changed = true;
    }

    if (changed)
      LineCount = LogView.Text.Split(['\r', '\n'], StringSplitOptions.None).Length;

    var now = DateTime.Now;
    if (changed || LastStatusRefresh is null || (now - LastStatusRefresh.Value).TotalSeconds >= 5)
    {
      LastStatusRefresh = now;
      StatusLabel.Text = $"刷新: {now:HH:mm:ss} | {LineCount} 行 | {LogPath}";
    }
  }
}
