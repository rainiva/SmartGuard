using System.Runtime.InteropServices;

namespace SmartGuard.LogViewer;

internal static class LogViewerRichTextRenderer
{
  private const int WmSetRedraw = 0x000B;
  private const int EmSetMargins = 0x00D3;
  private const int EcLeftMargin = 0x1;
  private const int EcRightMargin = 0x2;
  private const int EcTopMargin = 0x4;
  private const int EcBottomMargin = 0x8;

  [DllImport("user32.dll", CharSet = CharSet.Auto)]
  private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

  public static void ApplyTextMargins(RichTextBox richTextBox)
  {
    if (!richTextBox.IsHandleCreated) richTextBox.CreateControl();
    if (richTextBox.Handle == IntPtr.Zero) return;

    SendMessage(richTextBox.Handle, EmSetMargins, (IntPtr)EcLeftMargin, (IntPtr)LogViewerLayout.TextLeftMargin);
    SendMessage(richTextBox.Handle, EmSetMargins, (IntPtr)EcRightMargin, (IntPtr)LogViewerLayout.TextRightMargin);
    SendMessage(richTextBox.Handle, EmSetMargins, (IntPtr)EcTopMargin, (IntPtr)LogViewerLayout.TextTopMargin);
    SendMessage(richTextBox.Handle, EmSetMargins, (IntPtr)EcBottomMargin, (IntPtr)LogViewerLayout.TextBottomMargin);
  }

  public static void SetText(RichTextBox richTextBox, string text, bool scrollToTail)
  {
    SetRedraw(richTextBox, false);
    try
    {
      richTextBox.Clear();
      AppendLines(richTextBox, text);
      ApplyUniformFont(richTextBox);
      if (scrollToTail) ScrollToTail(richTextBox);
    }
    finally
    {
      SetRedraw(richTextBox, true);
      richTextBox.Refresh();
    }
  }

  public static void AppendText(RichTextBox richTextBox, string text, bool scrollToTail)
  {
    if (string.IsNullOrEmpty(text)) return;

    SetRedraw(richTextBox, false);
    try
    {
      AppendLines(richTextBox, text);
      ApplyUniformFont(richTextBox);
      if (scrollToTail) ScrollToTail(richTextBox);
    }
    finally
    {
      SetRedraw(richTextBox, true);
      richTextBox.Refresh();
    }
  }

  private static void AppendLines(RichTextBox richTextBox, string text)
  {
    if (string.IsNullOrEmpty(text)) return;

    var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    var lines = text.Split(newline, StringSplitOptions.None);
    for (var i = 0; i < lines.Length; i++)
    {
      var line = lines[i];
      if (line.Length == 0 && i == lines.Length - 1) continue;
      AppendLine(richTextBox, line);
      if (i < lines.Length - 1)
        richTextBox.AppendText(Environment.NewLine);
    }
  }

  private static void AppendLine(RichTextBox richTextBox, string line)
  {
    if (LogLineTagParser.TryParse(line, out var tag, out var body))
    {
      richTextBox.SelectionColor = LogViewerTagPalette.GetTagColor(tag);
      richTextBox.AppendText($"[{tag}]");
      richTextBox.SelectionColor = LogViewerTagPalette.BodyColor;
      richTextBox.AppendText($" {body}");
      return;
    }

    richTextBox.SelectionColor = LogViewerTagPalette.BodyColor;
    richTextBox.AppendText(line);
  }

  private static void ApplyUniformFont(RichTextBox richTextBox)
  {
    if (richTextBox.TextLength <= 0) return;
    richTextBox.SelectAll();
    richTextBox.SelectionFont = LogViewerFonts.Body;
    richTextBox.Select(richTextBox.TextLength, 0);
  }

  private static void ScrollToTail(RichTextBox richTextBox)
  {
    richTextBox.SelectionStart = richTextBox.TextLength;
    richTextBox.SelectionLength = 0;
    richTextBox.ScrollToCaret();
  }

  private static void SetRedraw(Control control, bool enable)
  {
    if (!control.IsHandleCreated) control.CreateControl();
    if (control.Handle == IntPtr.Zero) return;
    SendMessage(control.Handle, WmSetRedraw, enable ? 1 : IntPtr.Zero, IntPtr.Zero);
  }
}
