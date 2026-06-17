using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Tray.Toast;

namespace SmartGuard.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
  private readonly string _root;
  private readonly StatusStore _statusStore;
  private readonly GuardConfigRepository _configRepository;
  private readonly NotifyIcon _notifyIcon;
  private readonly ToolStripMenuItem _statusItem;
  private readonly ToolStripMenuItem _pauseItem;
  private readonly System.Windows.Forms.Timer _timer;
  private readonly TrayNotificationPresenter _notificationPresenter;
  private readonly Control _invokeSink;
  private readonly StatusFileWatcher _statusWatcher;
  private string? _lastNotifiedEventId;
  private string? _lastLegacyPlan;
  private int _missedStatusReads;
  private bool _guardianRecoveryAttempted;

  public TrayApplicationContext(string root)
  {
    _root = root;
    var configPath = Path.Combine(root, "SmartGuard.config.json");
    var statusPath = Path.Combine(root, "SmartGuard.status.json");
    _statusStore = new StatusStore(statusPath);
    _configRepository = new GuardConfigRepository(configPath);
    _notificationPresenter = new TrayNotificationPresenter(new WinRtToastNotifier(root));

    _notifyIcon = new NotifyIcon
    {
      Icon = TrayIconLoader.Load(root),
      Visible = true,
      Text = "智能电源守护",
    };

    var menu = new ContextMenuStrip { ShowImageMargin = false, AutoSize = true };
    menu.Font = SystemInformation.MenuFont;

    _statusItem = new ToolStripMenuItem("加载中…") { Enabled = false, AutoSize = true };
    menu.Items.Add(_statusItem);
    menu.Items.Add(new ToolStripSeparator());
    _pauseItem = new ToolStripMenuItem("暂停守护") { AutoSize = true };
    _pauseItem.Click += OnPauseClick;
    menu.Items.Add(_pauseItem);
    var logItem = new ToolStripMenuItem("打开日志") { AutoSize = true };
    logItem.Click += (_, _) => OpenLogViewer();
    menu.Items.Add(logItem);
    var settingsItem = new ToolStripMenuItem("设置…") { AutoSize = true };
    settingsItem.Click += OnOpenSettings;
    menu.Items.Add(settingsItem);
    menu.Items.Add(new ToolStripSeparator());
    var exitItem = new ToolStripMenuItem("退出") { AutoSize = true };
    exitItem.Click += (_, _) => ExitTray();
    menu.Items.Add(exitItem);

    _notifyIcon.ContextMenuStrip = menu;
    _notifyIcon.DoubleClick += OnOpenSettings;

    var config = _configRepository.LoadOrDefault(_root);
    _pauseItem.Text = config.Paused ? "恢复守护" : "暂停守护";

    _invokeSink = new Control();
    _invokeSink.CreateControl();
    _statusWatcher = new StatusFileWatcher(statusPath, () =>
    {
      if (_invokeSink.IsHandleCreated)
        _invokeSink.BeginInvoke(UpdateDisplay);
    });

    _timer = new System.Windows.Forms.Timer { Interval = 1500 };
    _timer.Tick += (_, _) => UpdateDisplay();
    _timer.Start();

    UpdateDisplay();
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _statusWatcher.Dispose();
      _invokeSink.Dispose();
      _timer.Stop();
      _timer.Dispose();
      _notifyIcon.Visible = false;
      _notifyIcon.Dispose();
    }

    base.Dispose(disposing);
  }

  private void OnPauseClick(object? sender, EventArgs e)
  {
    try
    {
      var previous = _configRepository.TryLoad()?.Paused;
      var next = !(previous ?? false);
      _configRepository.UpdatePaused(next);
      var msg = PauseGuardMessages.GetLogMessage(previous, next);
      if (msg is not null)
      {
        var fallback = Path.Combine(_root, "SmartGuard.startup.log");
        _configRepository.AppendInfoLog(msg, fallback);
      }

      _pauseItem.Text = next ? "恢复守护" : "暂停守护";
      UpdateDisplay();
    }
    catch (Exception ex)
    {
      MessageBox.Show($"操作失败：\n{ex.Message}", "智能电源守护", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private void OnOpenSettings(object? sender, EventArgs e)
  {
    try
    {
      ExternalToolLauncher.OpenSettings(_root);
    }
    catch (Exception ex)
    {
      MessageBox.Show($"打开设置失败：\n{ex.Message}", "智能电源守护", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private void OpenLogViewer()
  {
    try
    {
      ExternalToolLauncher.OpenLogViewer(_root);
    }
    catch (Exception ex)
    {
      MessageBox.Show($"打开日志失败：\n{ex.Message}", "智能电源守护", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private void ExitTray()
  {
    _notifyIcon.Visible = false;
    ExitThread();
  }

  private void UpdateDisplay()
  {
    var status = _statusStore.Read();
    if (status is null)
    {
      _missedStatusReads++;
      if (!_guardianRecoveryAttempted && GuardianRecovery.ShouldAttemptStart(_missedStatusReads))
      {
        _guardianRecoveryAttempted = true;
        GuardianRecovery.TryStartGuardian(_root);
      }
    }
    else
    {
      _missedStatusReads = 0;
    }

    var config = _configRepository.LoadOrDefault(_root);
    var notifyOnPlanChange = config.NotifyOnPlanChange;
    _notifyIcon.Text = TrayStatusFormatter.FormatTooltip(status);
    _statusItem.Text = TrayStatusFormatter.FormatStatusLine(status);
    TryShowNotification(status, notifyOnPlanChange);
  }

  private void TryShowNotification(StatusPayload? status, bool notifyOnPlanChange)
  {
    if (!notifyOnPlanChange || status is null) return;

    var evt = status.notificationEvent;
    if (evt is null || string.IsNullOrEmpty(evt.id))
    {
      if (TrayNotificationHelper.PlanChangedForNotification(_lastLegacyPlan, status.currentPlan))
      {
        var body = TrayNotificationHelper.FormatPlanChangeBalloon(status.currentPlan, status.brightness);
        _notifyIcon.ShowBalloonTip(5000, "智能电源守护", body, ToolTipIcon.Warning);
        _lastLegacyPlan = status.currentPlan;
      }

      return;
    }

    if (!NotificationDeduper.ShouldShow(_lastNotifiedEventId, evt)) return;
    var title = string.IsNullOrEmpty(evt.title) ? "智能电源守护" : evt.title;
    var text = string.IsNullOrEmpty(evt.body) ? status.currentPlan : evt.body;
    _notificationPresenter.Show(title, text, evt.id, (t, b) =>
      _notifyIcon.ShowBalloonTip(8000, t, b, ToolTipIcon.Info));
    _lastNotifiedEventId = evt.id;
    _lastLegacyPlan = status.currentPlan;
  }
}

public static class TrayNotificationHelper
{
  public static bool PlanChangedForNotification(string? previousPlan, string? currentPlan)
  {
    if (string.IsNullOrWhiteSpace(currentPlan)) return false;
    if (string.IsNullOrWhiteSpace(previousPlan)) return false;
    return !string.Equals(previousPlan, currentPlan, StringComparison.Ordinal);
  }

  public static string FormatPlanChangeBalloon(string planName, int brightness)
    => $"已切换至 {planName}（亮度 {brightness}%）";
}
