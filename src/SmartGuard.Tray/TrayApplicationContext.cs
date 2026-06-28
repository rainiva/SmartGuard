using System.Threading.Tasks;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Tray.Toast;

namespace SmartGuard.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
  private readonly string _root;
  private readonly GuardConfigRepository _configRepository;
  private readonly NotifyIcon _notifyIcon;
  private readonly ToolStripMenuItem _statusItem;
  private readonly ToolStripMenuItem _pauseItem;
  private readonly System.Windows.Forms.Timer _timer;
  private readonly TrayDisplayState _displayState = new();
  private readonly Control _invokeSink;
  private readonly StatusFileWatcher _statusWatcher;
  private readonly TrayRefreshScheduler _refreshScheduler;
  private int _missedStatusReads;
  private bool _guardianRecoveryAttempted;

  public TrayApplicationContext(string root)
  {
    _root = root;
    var configPath = Path.Combine(root, "SmartGuard.config.json");
    var statusPath = Path.Combine(root, "SmartGuard.status.json");
    var statusStore = new StatusStore(statusPath);
    _configRepository = new GuardConfigRepository(configPath);
    var notificationPresenter = new TrayNotificationPresenter(new WinRtToastNotifier(root));
    var config = _configRepository.LoadOrDefault(_root);
    var displaySettingsCache = new TrayDisplaySettingsCache(
      new TrayNotificationPreferences(config.NotifyOnPlanChange, config.NotifyOnExternalChange),
      () =>
      {
        var loaded = _configRepository.LoadOrDefault(_root);
        return new TrayNotificationPreferences(loaded.NotifyOnPlanChange, loaded.NotifyOnExternalChange);
      });

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
    var highPerfItem = new ToolStripMenuItem(TrayContextMenuTexts.SwitchHighPerformance) { AutoSize = true };
    highPerfItem.Click += OnSwitchHighPerformanceClick;
    menu.Items.Add(highPerfItem);
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
    TrayContextMenuPrewarmer.WarmUp(menu);

    _pauseItem.Text = config.Paused ? "恢复守护" : "暂停守护";

    _invokeSink = new Control();
    _invokeSink.CreateControl();
    _refreshScheduler = new TrayRefreshScheduler(
      root,
      statusStore,
      displaySettingsCache,
      notificationPresenter,
      _invokeSink,
      ApplyRefreshUi);

    menu.Opening += (_, _) =>
    {
      _refreshScheduler.ContextMenuOpen = true;
      _statusItem.Text = _displayState.StatusLine;
    };
    menu.Closed += (_, _) => _refreshScheduler.OnMenuClosed();

    _statusWatcher = new StatusFileWatcher(statusPath, () =>
    {
      if (_invokeSink.IsHandleCreated)
        _invokeSink.BeginInvoke(ScheduleRefresh);
    });

    _timer = new System.Windows.Forms.Timer { Interval = 5000 };
    _timer.Tick += (_, _) => ScheduleRefresh();
    _timer.Start();

    ScheduleRefresh();
    _ = Task.Run(() => ToastAumidRegistrar.EnsureRegistered(_root));
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

  private void ScheduleRefresh() => _refreshScheduler.ScheduleRefresh();

  private void ApplyRefreshUi(TrayRefreshUiUpdate update)
  {
    if (update.StatusWasMissing)
    {
      _missedStatusReads++;
      if (!_guardianRecoveryAttempted && GuardianRecovery.ShouldAttemptStart(_missedStatusReads))
      {
        _guardianRecoveryAttempted = true;
        _ = Task.Run(() => GuardianRecovery.TryStartGuardian(_root));
      }
    }
    else
    {
      _missedStatusReads = 0;
    }

    if (_displayState.Apply(update.Status))
    {
      _notifyIcon.Text = _displayState.Tooltip;
      if (!_refreshScheduler.ContextMenuOpen)
        _statusItem.Text = _displayState.StatusLine;
    }

    if (update.Notification is not { UseBalloonFallback: true } notification)
      return;

    var icon = string.IsNullOrEmpty(notification.Tag) ? ToolTipIcon.Warning : ToolTipIcon.Info;
    var timeout = string.IsNullOrEmpty(notification.Tag) ? 5000 : 8000;
    _notifyIcon.ShowBalloonTip(timeout, notification.Title, notification.Body, icon);
  }

  private async void OnPauseClick(object? sender, EventArgs e)
  {
    _pauseItem.Enabled = false;
    try
    {
      var next = await Task.Run(() =>
      {
        var previous = _configRepository.TryLoad()?.Paused;
        var value = !(previous ?? false);
        _configRepository.UpdatePaused(value);
        var msg = PauseGuardMessages.GetLogMessage(previous, value);
        if (msg is not null)
        {
          var fallback = Path.Combine(_root, "SmartGuard.startup.log");
          _configRepository.AppendInfoLog(msg, fallback);
        }
        return value;
      });

      _pauseItem.Text = next ? "恢复守护" : "暂停守护";
      ScheduleRefresh();
    }
    catch (Exception ex)
    {
      MessageBox.Show($"操作失败：\n{ex.Message}", "智能电源守护", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    finally
    {
      _pauseItem.Enabled = true;
    }
  }

  private async void OnSwitchHighPerformanceClick(object? sender, EventArgs e)
  {
    var item = sender as ToolStripMenuItem;
    if (item is not null) item.Enabled = false;
    try
    {
      await Task.Run(() => HighPerformanceBoost.Apply(_configRepository, _root, new PowerPlanActivator()));
      ScheduleRefresh();
    }
    catch (Exception ex)
    {
      MessageBox.Show($"切换失败：\n{ex.Message}", "智能电源守护", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    finally
    {
      if (item is not null) item.Enabled = true;
    }
  }

  private async void OnOpenSettings(object? sender, EventArgs e)
  {
    try
    {
      await Task.Run(() => ExternalToolLauncher.OpenSettings(_root));
    }
    catch (Exception ex)
    {
      MessageBox.Show($"打开设置失败：\n{ex.Message}", "智能电源守护", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }

  private async void OpenLogViewer()
  {
    try
    {
      await Task.Run(() => ExternalToolLauncher.OpenLogViewer(_root));
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
