namespace SmartGuard.LogViewer;

public sealed class LogViewerForm : Form
{
  private readonly LogViewerSession _session;
  private readonly System.Windows.Forms.Timer _timer;

  public LogViewerForm(string root, string logPath, string? fallbackLogPath)
  {
    Text = "智能电源守护 - 日志（实时）";
    Size = new Size(780, 520);
    StartPosition = FormStartPosition.CenterScreen;
    MinimumSize = new Size(480, 320);
    ShowInTaskbar = true;
    AutoScaleMode = AutoScaleMode.Dpi;

    var iconPath = Path.Combine(root, "lib", "SmartGuard.ico");
    if (File.Exists(iconPath))
    {
      try { Icon = new Icon(iconPath); } catch { /* use default */ }
    }

    var statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
    var statusLabel = new ToolStripStatusLabel
    {
      Spring = true,
      TextAlign = ContentAlignment.MiddleLeft,
    };
    statusStrip.Items.Add(statusLabel);

    var logView = new RichTextBox
    {
      Dock = DockStyle.Fill,
      ReadOnly = true,
      BorderStyle = BorderStyle.None,
      BackColor = Color.FromArgb(252, 252, 252),
      Font = LogViewerFonts.Body,
      WordWrap = false,
      HideSelection = false,
      DetectUrls = false,
    };
    Controls.Add(logView);
    logView.HandleCreated += (_, _) => LogViewerRichTextRenderer.ApplyTextMargins(logView);

    Controls.Add(statusStrip);

    _session = new LogViewerSession
    {
      LogPath = logPath,
      FallbackLogPath = fallbackLogPath,
      LogView = logView,
      StatusLabel = statusLabel,
    };
    Tag = _session;

    void DisableFollowTail(object? sender, EventArgs e)
    {
      _session.FollowTail = false;
    }

    logView.MouseWheel += DisableFollowTail;
    logView.KeyDown += (_, e) =>
    {
      if (e.KeyCode is Keys.PageUp or Keys.PageDown or Keys.Up or Keys.Down or Keys.Home)
        _session.FollowTail = false;
    };

    _timer = new System.Windows.Forms.Timer { Interval = 2000 };
    _timer.Tick += (_, _) => _session.RefreshView();
    Shown += (_, _) =>
    {
      LogViewerRichTextRenderer.ApplyTextMargins(logView);
      BeginInvoke(() =>
      {
        _session.RefreshView();
        _timer.Start();
      });
    };
    FormClosed += (_, _) =>
    {
      _timer.Stop();
      _timer.Dispose();
    };
  }

  public void ActivateWindow()
  {
    if (IsDisposed) return;
    WindowState = FormWindowState.Normal;
    Show();
    Activate();
    BringToFront();
    Focus();
  }
}
