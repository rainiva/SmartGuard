using System.Threading.Tasks;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Tray.Toast;

namespace SmartGuard.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
  private readonly string _root;
  private readonly GuardConfigRepository _configRepository;
  private readonly ConfigMutationService _configMutations;
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
  private bool? _statusPaused;

  public TrayApplicationContext(string root)
  {
    _root = root;
    var configPath = SmartGuardPaths.ConfigFile(root);
    var statusPath = SmartGuardPaths.StatusFile(root);
    var statusStore = new StatusStore(statusPath);
    _configRepository = new GuardConfigRepository(configPath);
    _configMutations = new ConfigMutationService(_configRepository);
    var notificationPresenter = new TrayNotificationPresenter(new WinRtToastNotifier(root));
    var displaySettingsCache = new TrayDisplaySettingsCache(_configRepository, _root);

    _notifyIcon = new NotifyIcon
    {
      Icon = TrayIconLoader.Load(root),
      Visible = true,
      Text = "智能电源守护",
    };

    var menuParts = TrayContextMenuFactory.Create(
      OnPauseClick,
      OnSwitchHighPerformanceClick,
      (_, _) => OpenLogViewer(),
      OnOpenSettings,
      (_, _) => ExitTray());
    _statusItem = menuParts.StatusItem;
    _pauseItem = menuParts.PauseItem;
    _notifyIcon.ContextMenuStrip = menuParts.Menu;
    _notifyIcon.DoubleClick += OnOpenSettings;

    _invokeSink = new Control();
    _invokeSink.CreateControl();
    _refreshScheduler = new TrayRefreshScheduler(
      root,
      statusStore,
      displaySettingsCache,
      notificationPresenter,
      _invokeSink,
      ApplyRefreshUi);

    menuParts.Menu.Opening += (_, _) =>
    {
      _refreshScheduler.ContextMenuOpen = true;
      _statusItem.Text = _displayState.StatusLine;
    };
    menuParts.Menu.Closed += (_, _) => _refreshScheduler.OnMenuClosed();

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
      _missedStatusReads = TrayGuardianRecoveryHandler.RegisterMissedStatusRead(
        _missedStatusReads,
        ref _guardianRecoveryAttempted,
        _root);
    }
    else
    {
      _missedStatusReads = TrayGuardianRecoveryHandler.ResetMissedStatusReads();
    }

    if (_displayState.Apply(update.Status))
    {
      _notifyIcon.Text = _displayState.Tooltip;
      if (!_refreshScheduler.ContextMenuOpen)
        _statusItem.Text = _displayState.StatusLine;
    }

    if (update.Status is not null)
    {
      _statusPaused = update.Status.paused;
      _pauseItem.Text = TrayPauseState.MenuText(_statusPaused);
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
        var value = TrayPauseState.ToggleTarget(_statusPaused);
        _configMutations.SetPaused(value, _root, SmartGuardPaths.StartupLogFile(_root));
        return value;
      });

      _statusPaused = next;
      _pauseItem.Text = TrayPauseState.MenuText(_statusPaused);
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
