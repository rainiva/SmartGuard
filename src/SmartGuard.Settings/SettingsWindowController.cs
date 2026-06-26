using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

public sealed class SettingsWindowController
{
  private readonly string _root;
  private readonly GuardConfigRepository _repository;
  private GuardConfig _originalConfig;
  private readonly Window _window;
  private readonly Border _toastContainer;
  private readonly ToastNotificationService _toastService;
  private readonly NumberBox _sldBalanced;
  private readonly NumberBox _sldSaver;
  private readonly NumberBox _sldBattery;
  private readonly NumberBox _sldPoll;
  private readonly NumberBox _sldBrightMs;
  private readonly NumberBox _sldHeartbeat;
  private readonly ComboBox _cmbActivePlan;
  private readonly ComboBox _cmbBalancedPlan;
  private readonly ComboBox _cmbPowerSaverPlan;
  private readonly TextBlock? _lblPlanMappingStatus;
  private readonly CheckBox _tglPaused;
  private readonly CheckBox _tglNotify;
  private readonly CheckBox _tglAutoStart;
  private bool _isDarkTheme;

  internal bool IsDarkThemeEnabled => _isDarkTheme;

  private System.Windows.Threading.DispatcherTimer? _layoutStabilityTimer;
  private LogViewController? _logController;
  private System.Windows.Threading.DispatcherTimer? _logTimer;
  private System.Windows.Threading.DispatcherTimer? _logSearchDebounceTimer;
  private System.Windows.Threading.DispatcherTimer? _logCustomRangeDebounceTimer;
  private System.Windows.Threading.DispatcherTimer? _saveDebounceTimer;
  private LogSearchFilterBar? _logSearchFilterBar;
  private WrapPanel? _logTagFilterLinksPanel;
  private ListBox? _lstLogView;
  private LogViewListPresenter? _logListPresenter;
  private IReadOnlyList<string> _lastDisplayedLines = Array.Empty<string>();
  private int? _logIdleSeconds;
  private bool _logStatusMayBeStale;
  private TextBlock? _lblLogStatus;
  private ScrollViewer? _logScrollViewer;
  private string? _logPath;
  private string? _fallbackLogPath;
  private CheckBox? _chkLogFollowTail;
  private bool _suppressFollowTailAutoSync;
  private ComboBox? _cmbLogTimeRange;
  private UIElement? _panelLogCustomRange;
  private TextBox? _txtLogRangeStart;
  private TextBox? _txtLogRangeEnd;
  private CheckBox? _chkLogSearchCaseSensitive;
  private DateTime _lastUpdateCheckTime = DateTime.MinValue;
  private bool _lastUpdateCheckNoUpdate;
  private CancellationTokenSource? _saveCts;
  private CancellationTokenSource? _planCatalogLoadCts;
  private int _planCatalogLoadGeneration;
  private IReadOnlyDictionary<Guid, string> _planCatalog = new Dictionary<Guid, string>();
  private bool _suppressPlanComboEvents;
  private bool _logViewInitialized;
  private bool _layoutHooksAttached;

  internal bool IsLogViewInitializedForTests => _logViewInitialized;

  private SettingsWindowController(
    string root,
    GuardConfigRepository repository,
    GuardConfig originalConfig,
    Window window,
    Border toastContainer,
    NumberBox sldBalanced,
    NumberBox sldSaver,
    NumberBox sldBattery,
    NumberBox sldPoll,
    NumberBox sldBrightMs,
    NumberBox sldHeartbeat,
    ComboBox cmbActivePlan,
    ComboBox cmbBalancedPlan,
    ComboBox cmbPowerSaverPlan,
    TextBlock? lblPlanMappingStatus,
    CheckBox tglPaused,
    CheckBox tglNotify,
    CheckBox tglAutoStart)
  {
    _root = root;
    _repository = repository;
    _originalConfig = originalConfig;
    _window = window;
    _toastContainer = toastContainer;
    _toastService = new ToastNotificationService(
      window,
      TimeSpan.FromSeconds(3),
      (message, isError, isDarkMode, _) => new InlineToastNotification(message, isError, isDarkMode, toastContainer))
    {
      IsDarkMode = _isDarkTheme
    };
    _sldBalanced = sldBalanced;
    _sldSaver = sldSaver;
    _sldBattery = sldBattery;
    _sldPoll = sldPoll;
    _sldBrightMs = sldBrightMs;
    _sldHeartbeat = sldHeartbeat;
    _cmbActivePlan = cmbActivePlan;
    _cmbBalancedPlan = cmbBalancedPlan;
    _cmbPowerSaverPlan = cmbPowerSaverPlan;
    _lblPlanMappingStatus = lblPlanMappingStatus;
    _tglPaused = tglPaused;
    _tglNotify = tglNotify;
    _tglAutoStart = tglAutoStart;
  }

  public static SettingsWindowController? TryCreate(string root, GuardConfigRepository repository, GuardConfig config)
    => TryCreate(root, repository, config, out _);

  public static SettingsWindowController? TryCreate(
    string root,
    GuardConfigRepository repository,
    GuardConfig config,
    out string? loadError)
  {
    loadError = null;
    Window? window = null;

    try
    {
      window = SettingsXamlLoader.TryLoadEmbeddedWindow(out var embeddedError);
      if (window is null)
      {
        var xamlPath = Path.Combine(root, "lib", "SmartGuard.Settings.xaml");
        window = SettingsXamlLoader.TryLoadLooseWindowFromFile(xamlPath, out var fileError);
        if (window is null)
        {
          loadError = fileError ?? embeddedError ?? "Unknown settings layout load failure.";
          return null;
        }
      }

      AppBrandIcon.ApplyTo(window, root);

      return BuildController(root, repository, config, window);
    }
    catch (Exception ex)
    {
      loadError = ex.Message;
      return null;
    }
  }

  private static SettingsWindowController BuildController(
    string root,
    GuardConfigRepository repository,
    GuardConfig config,
    Window window)
  {

    var sldBalanced = Require<NumberBox>(window, "sldBalanced");
    var sldSaver = Require<NumberBox>(window, "sldSaver");
    var sldBattery = Require<NumberBox>(window, "sldBattery");
    var sldPoll = Require<NumberBox>(window, "sldPoll");
    var sldBrightMs = Require<NumberBox>(window, "sldBrightMs");
    var sldHeartbeat = Require<NumberBox>(window, "sldHeartbeat");
    var lblBalanced = Require<TextBlock>(window, "lblBalanced");
    var lblSaver = Require<TextBlock>(window, "lblSaver");
    var lblBattery = Require<TextBlock>(window, "lblBattery");
    var lblPoll = Require<TextBlock>(window, "lblPoll");
    var lblBrightMs = Require<TextBlock>(window, "lblBrightMs");
    var lblHeartbeat = Require<TextBlock>(window, "lblHeartbeat");
    var cmbActivePlan = Require<ComboBox>(window, "cmbActivePlan");
    var cmbBalancedPlan = Require<ComboBox>(window, "cmbBalancedPlan");
    var cmbPowerSaverPlan = Require<ComboBox>(window, "cmbPowerSaverPlan");
    var lblPlanMappingStatus = window.FindName("lblPlanMappingStatus") as TextBlock;
    var tglPaused = Require<CheckBox>(window, "tglPaused");
    var tglNotify = Require<CheckBox>(window, "tglNotify");
    var tglAutoStart = Require<CheckBox>(window, "tglAutoStart");
    var txtVersion = Require<TextBlock>(window, "txtVersion");
    var navList = Require<ListBox>(window, "navList");
    var btnThemeToggle = Require<Button>(window, "btnThemeToggle");
    var toastContainer = Require<Border>(window, "toastContainer");

    var controller = new SettingsWindowController(
      root,
      repository,
      config,
      window,
      toastContainer,
      sldBalanced,
      sldSaver,
      sldBattery,
      sldPoll,
      sldBrightMs,
      sldHeartbeat,
      cmbActivePlan,
      cmbBalancedPlan,
      cmbPowerSaverPlan,
      lblPlanMappingStatus,
      tglPaused,
      tglNotify,
      tglAutoStart);

    // Initialize values
    sldBalanced.Value = SettingsInitialValues.BalancedThresholdMinutes(config);
    sldSaver.Value = SettingsInitialValues.PowerSaverThresholdMinutes(config);
    sldBattery.Value = config.LowBatteryPercent;
    sldPoll.Value = config.CheckIntervalSec;
    sldBrightMs.Value = config.BrightnessRestoreMs;
    sldHeartbeat.Value = config.HeartbeatIntervalMin;
    tglPaused.IsChecked = config.Paused;
    tglNotify.IsChecked = config.NotifyOnPlanChange;
    tglAutoStart.IsChecked = config.AutoStartEnabled;

    if (controller._lblPlanMappingStatus is not null)
      controller._lblPlanMappingStatus.Text = "正在加载电源计划...";
    controller.BeginLoadPlanCatalogAsync();

    // Sync displayed version with the actual assembly / installer version
    txtVersion.Text = GetDisplayVersion();

    // Register label updates for NumberBox
    RegisterNumberBoxLabel(sldBalanced, lblBalanced, "{0} 分钟");
    RegisterNumberBoxLabel(sldSaver, lblSaver, "{0} 分钟");
    RegisterNumberBoxLabel(sldBattery, lblBattery, "{0}%");
    RegisterNumberBoxLabel(sldPoll, lblPoll, "{0} 秒");
    RegisterNumberBoxLabel(sldBrightMs, lblBrightMs, "{0} 毫秒");
    RegisterHeartbeatLabel(sldHeartbeat, lblHeartbeat);

    // Instant-apply: queue a save when any setting changes.
    void QueueSave() => controller.QueueSave();
    sldBalanced.ValueChanged += (_, _) => QueueSave();
    sldSaver.ValueChanged += (_, _) => QueueSave();
    sldBattery.ValueChanged += (_, _) => QueueSave();
    sldPoll.ValueChanged += (_, _) => QueueSave();
    sldBrightMs.ValueChanged += (_, _) => QueueSave();
    sldHeartbeat.ValueChanged += (_, _) => QueueSave();

    void QueueSaveAndRefreshPlanStatus()
    {
      controller.UpdatePlanMappingStatus();
      QueueSave();
    }

    cmbActivePlan.SelectionChanged += (_, _) =>
    {
      if (controller._suppressPlanComboEvents) return;
      QueueSaveAndRefreshPlanStatus();
    };
    cmbBalancedPlan.SelectionChanged += (_, _) =>
    {
      if (controller._suppressPlanComboEvents) return;
      QueueSaveAndRefreshPlanStatus();
    };
    cmbPowerSaverPlan.SelectionChanged += (_, _) =>
    {
      if (controller._suppressPlanComboEvents) return;
      QueueSaveAndRefreshPlanStatus();
    };

    tglPaused.Checked += (_, _) => QueueSave();
    tglPaused.Unchecked += (_, _) => QueueSave();
    tglNotify.Checked += (_, _) => QueueSave();
    tglNotify.Unchecked += (_, _) => QueueSave();
    tglAutoStart.Checked += (_, _) => QueueSave();
    tglAutoStart.Unchecked += (_, _) => QueueSave();

    // Navigation
    controller.SetupNavigation(navList, window);

    SettingsWindowPresentation.RegisterShowHooks(window);
    window.Loaded += (_, _) =>
    {
      if (controller._layoutHooksAttached)
        return;

      controller._layoutHooksAttached = true;
      SettingsWindowLayoutStability.Attach(
        window,
        () => controller.IsDarkThemeEnabled,
        controller.StabilizeLayout,
        controller.QueueLayoutStabilization);
    };

    // Theme toggle
    btnThemeToggle.Click += (_, _) => controller.ToggleTheme(window);

    // Repository link
    var lnkRepo = window.FindName("lnkRepo") as Hyperlink;
    if (lnkRepo != null)
    {
      lnkRepo.RequestNavigate += (_, e) =>
      {
        e.Handled = true;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
        {
          UseShellExecute = true
        });
      };
    }

    // Check update button
    var btnCheckUpdate = Require<Button>(window, "btnCheckUpdate");
    btnCheckUpdate.Click += async (_, _) =>
    {
      btnCheckUpdate.Content = "检查中...";
      btnCheckUpdate.IsEnabled = false;
      try
      {
        await controller.CheckForUpdateAsync(window).ConfigureAwait(true);
      }
      catch (Exception ex)
      {
        window.Dispatcher.Invoke(() =>
          AppDialog.ShowAlert(window, "检查更新", $"检查更新时发生错误：{ex.Message}", AppDialogSeverity.Error));
      }
      finally
      {
        window.Dispatcher.Invoke(() =>
        {
          btnCheckUpdate.Content = "检查更新";
          btnCheckUpdate.IsEnabled = true;
        });
      }
    };

    var btnResetDefaults = window.FindName("btnResetDefaults") as Button;
    if (btnResetDefaults is not null)
      btnResetDefaults.Click += (_, _) => controller.ResetToDefaults();

    // Defer log subsystem wiring until the user opens the logs page.
    controller._logPath = Path.Combine(root, "SmartGuard.log");
    controller._fallbackLogPath = Path.Combine(root, "SmartGuard.startup.log");

    window.Closing += (_, _) => controller.Dispose();
    window.StateChanged += (_, _) =>
    {
      if (window.WindowState == WindowState.Minimized)
        controller.SetLogPageActive(false);
      else
        controller.SetLogPageActive(navList.SelectedIndex == 3);
    };

    return controller;
  }

  private void EnsureLogViewInitialized()
  {
    if (_logViewInitialized || _logPath is null)
      return;

    _logViewInitialized = true;
    var fallbackLogPath = _fallbackLogPath;
    var logController = new LogViewController(_logPath, fallbackLogPath);
    _logController = logController;
    _logSearchFilterBar = new LogSearchFilterBar();
    var logSearchFilterHost = Require<ContentControl>(_window, "logSearchFilterHost");
    logSearchFilterHost.Content = _logSearchFilterBar;
    _logTagFilterLinksPanel = Require<WrapPanel>(_window, "logTagFilterLinksPanel");
    foreach (var tag in LogTagFilterCatalog.SelectableTags)
    {
      _logTagFilterLinksPanel.Children.Add(
        LogTagFilterLinkFactory.CreateClickableTag(tag, AddLogTagFilter));
    }

    _lstLogView = Require<ListBox>(_window, "lstLogView");
    _logListPresenter = new LogViewListPresenter();
    _logListPresenter.Attach(_lstLogView);
    _lblLogStatus = Require<TextBlock>(_window, "lblLogStatus");
    _lstLogView.Loaded += (_, _) => EnsureLogScrollViewerHooked();
    _chkLogFollowTail = _window.FindName("chkLogFollowTail") as CheckBox;
    _cmbLogTimeRange = _window.FindName("cmbLogTimeRange") as ComboBox;
    _panelLogCustomRange = _window.FindName("panelLogCustomRange") as UIElement;
    _txtLogRangeStart = _window.FindName("txtLogRangeStart") as TextBox;
    _txtLogRangeEnd = _window.FindName("txtLogRangeEnd") as TextBox;
    _chkLogSearchCaseSensitive = _window.FindName("chkLogSearchCaseSensitive") as CheckBox;

    SyncCustomRangePanelVisibility();

    var btnLogCopy = _window.FindName("btnLogCopy") as Button;
    var btnLogExport = _window.FindName("btnLogExport") as Button;
    var btnLogOpenFolder = _window.FindName("btnLogOpenFolder") as Button;
    var btnLogScrollTop = _window.FindName("btnLogScrollTop") as Button;
    var btnLogScrollBottom = _window.FindName("btnLogScrollBottom") as Button;
    var btnLogRefresh = _window.FindName("btnLogRefresh") as Button;

    if (btnLogCopy is not null) btnLogCopy.Click += (_, _) => CopyVisibleLog();
    if (btnLogExport is not null) btnLogExport.Click += (_, _) => ExportVisibleLog();
    if (btnLogOpenFolder is not null) btnLogOpenFolder.Click += (_, _) => OpenLogFolder();
    if (btnLogScrollTop is not null) btnLogScrollTop.Click += (_, _) => ScrollLogToTop();
    if (btnLogScrollBottom is not null) btnLogScrollBottom.Click += (_, _) => ScrollLogToBottom();
    if (btnLogRefresh is not null) btnLogRefresh.Click += (_, _) => ForceRefreshLogView();
    if (_chkLogFollowTail is not null)
    {
      _chkLogFollowTail.Checked += (_, _) => SetFollowTail(true);
      _chkLogFollowTail.Unchecked += (_, _) => SetFollowTail(false);
    }

    _logSearchFilterBar.FiltersChanged += (_, _) => QueueLogSearchRefresh();
    if (_cmbLogTimeRange is not null)
    {
      _cmbLogTimeRange.SelectionChanged += (_, _) =>
      {
        SyncCustomRangePanelVisibility();
        RefreshLogView();
      };
    }

    if (_txtLogRangeStart is not null)
      _txtLogRangeStart.TextChanged += (_, _) => QueueLogCustomRangeRefresh();
    if (_txtLogRangeEnd is not null)
      _txtLogRangeEnd.TextChanged += (_, _) => QueueLogCustomRangeRefresh();
    if (_chkLogSearchCaseSensitive is not null)
    {
      _chkLogSearchCaseSensitive.Checked += (_, _) => RefreshLogView();
      _chkLogSearchCaseSensitive.Unchecked += (_, _) => RefreshLogView();
    }

    var logTimer = new System.Windows.Threading.DispatcherTimer(
      System.Windows.Threading.DispatcherPriority.Background,
      _window.Dispatcher)
    {
      Interval = TimeSpan.FromSeconds(2),
    };
    logTimer.Tick += (_, _) => RefreshLogView();
    _logTimer = logTimer;
  }

  private string? _initialPage;

  public void SetInitialPage(string page) => _initialPage = page;

  public bool? ShowDialog()
  {
    if (!string.IsNullOrWhiteSpace(_initialPage))
    {
      var page = _initialPage;
      _initialPage = null;
      _window.Loaded += (_, _) => NavigateTo(page);
    }

    return _window.ShowDialog();
  }

  public void Activate()
  {
    _window.Dispatcher.Invoke(() =>
    {
      if (!_window.IsVisible)
        _window.Show();
      _window.WindowState = WindowState.Normal;
      SettingsWindowPresentation.BringToForeground(_window);
    });
  }

  private void QueueSave()
  {
    if (_saveDebounceTimer is null)
    {
      _saveDebounceTimer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        _window.Dispatcher)
      {
        Interval = TimeSpan.FromMilliseconds(300)
      };
      _saveDebounceTimer.Tick += (_, _) =>
      {
        _saveDebounceTimer.Stop();
        SaveCurrentSettings();
      };
    }

    _saveDebounceTimer.Stop();
    _saveDebounceTimer.Start();
  }

  private async void SaveCurrentSettings()
  {
    try
    {
      var newConfig = SettingsSnapshotMapper.ApplyTraySettings(
        _originalConfig,
        balancedThresholdMin: _sldBalanced.Value,
        powerSaverThresholdMin: _sldSaver.Value,
        lowBatteryPercent: _sldBattery.Value,
        checkIntervalSec: _sldPoll.Value,
        brightnessRestoreMs: _sldBrightMs.Value,
        heartbeatIntervalMin: _sldHeartbeat.Value,
        activePlanGuid: ReadSelectedPlanGuid(_cmbActivePlan),
        balancedPlanGuid: ReadSelectedPlanGuid(_cmbBalancedPlan),
        powerSaverPlanGuid: ReadSelectedPlanGuid(_cmbPowerSaverPlan),
        paused: _tglPaused.IsChecked == true,
        notifyOnPlanChange: _tglNotify.IsChecked == true,
        autoStartEnabled: _tglAutoStart.IsChecked == true);

      var errors = GuardConfigValidator.Validate(newConfig);
      errors = errors.Concat(PowerPlanMappingValidator.Validate(newConfig)).ToList();
      if (errors.Count > 0)
      {
        _toastService.Show("保存失败：" + string.Join("；", errors), isError: true);
        return;
      }

      _saveCts?.Cancel();
      _saveCts?.Dispose();
      _saveCts = new CancellationTokenSource();
      var token = _saveCts.Token;

      await Task.Run(() =>
      {
        token.ThrowIfCancellationRequested();
        SettingsSaveCoordinator.Save(newConfig, _originalConfig, _root, _repository);
      }, token);
      _originalConfig = newConfig;
      UpdatePlanMappingStatus(newConfig);
      _toastService.Show("设置已保存", isError: false);
    }
    catch (OperationCanceledException)
    {
      // 窗口关闭或新保存触发时忽略已取消的旧任务
    }
    catch (Exception ex)
    {
      _toastService.Show($"保存失败：{ex.Message}", isError: true);
    }
  }

  private void RefreshLogView(bool forceRedraw = false)
  {
    if (_logController is null || _logListPresenter is null || _lblLogStatus is null) return;

    var idleRead = LogViewIdleReader.TryRead(_root);
    if (idleRead.Seconds is not null)
      _logIdleSeconds = idleRead.Seconds;
    _logStatusMayBeStale = idleRead.StatusMayBeStale;

    var snapshot = BuildLogSnapshot();
    if (snapshot is null) return;

    var statusText = LogViewStatusTextBuilder.Build(snapshot, DateTime.Now, _logIdleSeconds, _logStatusMayBeStale);
    var plan = LogViewUpdatePlanner.CreatePlan(_lastDisplayedLines, snapshot.DisplayLines, forceRedraw);

    if (!forceRedraw && !snapshot.ContentChanged && plan.Mode == LogViewUpdateMode.NoChange)
    {
      _lblLogStatus.Text = statusText;
      return;
    }

    var scrollViewer = ResolveLogScrollViewer();
    var savedOffset = scrollViewer?.VerticalOffset ?? 0;
    var wasAtTail = scrollViewer is null || LogViewScrollState.IsAtTail(scrollViewer);
    var scrollToTail = _logController.FollowTail && wasAtTail;

    _logListPresenter.Apply(plan);
    _lastDisplayedLines = snapshot.DisplayLines.ToArray();
    _lblLogStatus.Text = statusText;

    if (scrollViewer is null)
      return;

    scrollViewer.UpdateLayout();
    if (scrollToTail)
      ScrollLogViewToTail(deferred: true);
    else if (!wasAtTail)
      scrollViewer.ScrollToVerticalOffset(savedOffset);
  }

  private bool _logScrollHooked;

  internal void EnsureLogScrollViewerHooked()
  {
    if (_lstLogView is null || _logScrollHooked)
      return;

    _logListPresenter?.EnsureScrollViewerResolved();
    _logScrollViewer = _logListPresenter?.ScrollViewer ?? LogViewListPresenter.FindScrollViewer(_lstLogView);
    if (_logScrollViewer is null)
      return;

    ScrollBarAutoHide.Attach(_logScrollViewer);
    _logScrollViewer.ScrollChanged += (_, _) =>
    {
      if (_logController is null || _logScrollViewer is null)
        return;
      if (_suppressFollowTailAutoSync)
        return;
      _logController.FollowTail = LogViewScrollState.IsAtTail(_logScrollViewer);
      SyncFollowTailToggle();
    };
    _logScrollHooked = true;
  }

  private ScrollViewer? ResolveLogScrollViewer()
  {
    if (_logScrollViewer is not null)
      return _logScrollViewer;

    if (_lstLogView is null)
      return null;

    _logListPresenter?.EnsureScrollViewerResolved();
    _logScrollViewer = _logListPresenter?.ScrollViewer ?? LogViewListPresenter.FindScrollViewer(_lstLogView);
    return _logScrollViewer;
  }

  private LogViewSnapshot? BuildLogSnapshot()
  {
    if (_logController is null) return null;

    _logController.SearchKeyword = _logSearchFilterBar?.Keyword ?? string.Empty;
    _logController.ActiveTagFilters = _logSearchFilterBar?.ActiveTags ?? [];
    _logController.SearchCaseSensitive = _chkLogSearchCaseSensitive?.IsChecked == true;
    _logController.TimeRange = ReadTimeRange(_cmbLogTimeRange);
    _logController.CustomRangeStart = TryReadCustomRangeStart();
    _logController.CustomRangeEnd = TryReadCustomRangeEnd();
    _logController.RefreshFromDisk();
    return _logController.GetSnapshot();
  }

  private void SyncCustomRangePanelVisibility()
  {
    if (_panelLogCustomRange is null || _cmbLogTimeRange is null)
      return;

    _panelLogCustomRange.Visibility = _cmbLogTimeRange.SelectedIndex == 3
      ? Visibility.Visible
      : Visibility.Collapsed;
  }

  private static LogViewTimeRange ReadTimeRange(ComboBox? comboBox)
  {
    return comboBox?.SelectedIndex switch
    {
      1 => LogViewTimeRange.Today,
      2 => LogViewTimeRange.LastHour,
      3 => LogViewTimeRange.Custom,
      _ => LogViewTimeRange.All,
    };
  }

  private DateTime? TryReadCustomRangeStart()
  {
    return LogViewCustomRangeParser.TryParse(_txtLogRangeStart?.Text, out var value)
      ? value
      : null;
  }

  private DateTime? TryReadCustomRangeEnd()
  {
    return LogViewCustomRangeParser.TryParse(_txtLogRangeEnd?.Text, out var value)
      ? value
      : null;
  }

  internal void ForceRefreshLogView()
  {
    _logController?.ForceRefresh();
    _lastDisplayedLines = Array.Empty<string>();
    RefreshLogView(forceRedraw: true);
  }

  private void CopyVisibleLog()
  {
    var snapshot = BuildLogSnapshot();
    if (snapshot is null) return;

    try
    {
      Clipboard.SetText(LogViewToolbarActions.BuildVisibleText(snapshot));
      _toastService.Show("已复制到剪贴板", isError: false);
    }
    catch (Exception ex)
    {
      _toastService.Show($"复制失败：{ex.Message}", isError: true);
    }
  }

  private void ExportVisibleLog()
  {
    var snapshot = BuildLogSnapshot();
    if (snapshot is null) return;

    var dialog = new Microsoft.Win32.SaveFileDialog
    {
      Filter = "文本文件 (*.txt)|*.txt",
      FileName = "SmartGuard.log.txt",
      DefaultExt = ".txt",
    };

    if (dialog.ShowDialog(_window) != true)
      return;

    ExportVisibleLogToPath(dialog.FileName);
  }

  private void ExportVisibleLogToPath(string destinationPath)
  {
    var snapshot = BuildLogSnapshot();
    if (snapshot is null) return;

    try
    {
      LogViewToolbarActions.ExportVisibleText(
        LogViewToolbarActions.BuildVisibleText(snapshot),
        destinationPath);
      _toastService.Show("日志已导出", isError: false);
    }
    catch (Exception ex)
    {
      _toastService.Show($"导出失败：{ex.Message}", isError: true);
    }
  }

  private void OpenLogFolder()
  {
    if (string.IsNullOrWhiteSpace(_logPath)) return;

    var logFilePath = LogViewToolbarActions.ResolveLogFilePath(_logPath, _fallbackLogPath);
    try
    {
      System.Diagnostics.Process.Start(LogViewToolbarActions.CreateRevealLogFileProcessStartInfo(logFilePath));
    }
    catch (Exception ex)
    {
      _toastService.Show($"打开目录失败：{ex.Message}", isError: true);
    }
  }

  private void ScrollLogToTop()
  {
    _suppressFollowTailAutoSync = true;
    try
    {
      if (_logController is not null)
        _logController.FollowTail = false;
      SyncFollowTailToggle();
      ResolveLogScrollViewer()?.ScrollToVerticalOffset(0);
    }
    finally
    {
      ReleaseFollowTailAutoSyncSuppression();
    }
  }

  private void ScrollLogToBottom()
  {
    _suppressFollowTailAutoSync = true;
    try
    {
      ScrollLogViewToTail();
      if (_logController is not null && _chkLogFollowTail?.IsChecked != true)
        _logController.FollowTail = false;
      SyncFollowTailToggle();
    }
    finally
    {
      ReleaseFollowTailAutoSyncSuppression();
    }
  }

  private void ScrollLogViewToTail(bool deferred = false)
  {
    void ScrollNow()
    {
      var scrollViewer = ResolveLogScrollViewer();
      if (scrollViewer is null)
        return;

      scrollViewer.UpdateLayout();

      if (_lstLogView is not null && _lstLogView.Items.Count > 0)
        _lstLogView.ScrollIntoView(_lstLogView.Items[_lstLogView.Items.Count - 1]);

      scrollViewer.UpdateLayout();
      scrollViewer.ScrollToEnd();
    }

    ScrollNow();
    if (!deferred)
      return;

    _window.Dispatcher.BeginInvoke(
      ScrollNow,
      System.Windows.Threading.DispatcherPriority.Loaded);
  }

  private void ReleaseFollowTailAutoSyncSuppression()
  {
    _window.Dispatcher.BeginInvoke(
      () => _suppressFollowTailAutoSync = false,
      System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  }

  private void SetFollowTail(bool enabled)
  {
    if (_logController is null) return;

    _logController.FollowTail = enabled;
    if (enabled)
    {
      RefreshLogView(forceRedraw: true);
      _suppressFollowTailAutoSync = true;
      try
      {
        ScrollLogViewToTail(deferred: true);
      }
      finally
      {
        ReleaseFollowTailAutoSyncSuppression();
      }
    }
  }

  private void SyncFollowTailToggle()
  {
    if (_logController is null || _chkLogFollowTail is null) return;

    _chkLogFollowTail.IsChecked = _logController.FollowTail;
  }

  private void QueueLogSearchRefresh()
  {
    if (_logSearchDebounceTimer is null)
    {
      _logSearchDebounceTimer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        _window.Dispatcher)
      {
        Interval = TimeSpan.FromMilliseconds(300),
      };
      _logSearchDebounceTimer.Tick += (_, _) =>
      {
        _logSearchDebounceTimer.Stop();
        RefreshLogView();
      };
    }

    _logSearchDebounceTimer.Stop();
    _logSearchDebounceTimer.Start();
  }

  private void QueueLogCustomRangeRefresh()
  {
    if (_logCustomRangeDebounceTimer is null)
    {
      _logCustomRangeDebounceTimer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        _window.Dispatcher)
      {
        Interval = TimeSpan.FromMilliseconds(300),
      };
      _logCustomRangeDebounceTimer.Tick += (_, _) =>
      {
        _logCustomRangeDebounceTimer.Stop();
        RefreshLogView();
      };
    }

    _logCustomRangeDebounceTimer.Stop();
    _logCustomRangeDebounceTimer.Start();
  }

  internal void BeginLoadPlanCatalogAsync()
  {
    var generation = Interlocked.Increment(ref _planCatalogLoadGeneration);
    _planCatalogLoadCts?.Cancel();
    _planCatalogLoadCts?.Dispose();
    _planCatalogLoadCts = new CancellationTokenSource();

    if (_lblPlanMappingStatus is not null)
      _lblPlanMappingStatus.Text = "正在加载电源计划...";

    _ = Task.Run(() => PowerPlanCatalogProvider.LoadWithRetry())
      .ContinueWith(task =>
      {
        _window.Dispatcher.BeginInvoke(() =>
        {
          if (generation != Volatile.Read(ref _planCatalogLoadGeneration))
            return;

          if (task.IsFaulted)
          {
            if (_lblPlanMappingStatus is not null)
              _lblPlanMappingStatus.Text = "无法读取电源计划，请关闭设置后重试";
            return;
          }

          ApplyPlanCatalog(task.Result);
        });
      }, TaskScheduler.Default);
  }

  private void ApplyPlanCatalog(IReadOnlyDictionary<Guid, string> catalog)
  {
    _planCatalog = catalog;
    RepopulatePlanCombos();
    UpdatePlanMappingStatus(_originalConfig);
  }

  private void RepopulatePlanCombos()
  {
    PopulatePlanCombo(_cmbActivePlan, _originalConfig.ActivePlanGuid, "高性能");
    PopulatePlanCombo(_cmbBalancedPlan, _originalConfig.BalancedPlanGuid, "平衡");
    PopulatePlanCombo(_cmbPowerSaverPlan, _originalConfig.PowerSaverPlanGuid, "节能");
  }

  internal void AddLogTagFilter(string tag)
  {
    _logSearchFilterBar?.AddTagFilter(tag);
  }

  public void SetLogPageActive(bool active)
  {
    if (!active)
    {
      _logTimer?.Stop();
      return;
    }

    EnsureLogViewInitialized();
    if (_logTimer is null)
      return;

    _logTimer.Start();
    RefreshLogView();
    EnsureLogScrollViewerHooked();
  }

  internal void StabilizeLayout()
  {
    SettingsWindowLayoutStability.StabilizeContentLayout(_window);
  }

  internal void QueueLayoutStabilization()
  {
    if (_layoutStabilityTimer is null)
    {
      _layoutStabilityTimer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        _window.Dispatcher)
      {
        Interval = TimeSpan.FromMilliseconds(50),
      };
      _layoutStabilityTimer.Tick += (_, _) =>
      {
        _layoutStabilityTimer.Stop();
        StabilizeLayout();
      };
    }

    _layoutStabilityTimer.Stop();
    _layoutStabilityTimer.Start();
  }

  public void Dispose()
  {
    _saveDebounceTimer?.Stop();
    _logTimer?.Stop();
    _logSearchDebounceTimer?.Stop();
    _logCustomRangeDebounceTimer?.Stop();
    _layoutStabilityTimer?.Stop();
    _saveCts?.Cancel();
    _saveCts?.Dispose();
    _planCatalogLoadCts?.Cancel();
    _planCatalogLoadCts?.Dispose();
    _toastService?.Dispose();
  }

  public void NavigateTo(string page)
  {
    _window.Dispatcher.Invoke(() =>
    {
      var navList = _window.FindName("navList") as ListBox;
      if (navList is null) return;

      var targetIndex = page.ToLowerInvariant() switch
      {
        "general" or "常规" => 0,
        "advanced" or "高级" => 1,
        "notifications" or "通知" => 2,
        "logs" or "日志" => 3,
        "about" or "关于" => 4,
        _ => 0,
      };

      navList.SelectedIndex = targetIndex;
    });
  }

  private void SetupNavigation(ListBox navList, Window window)
  {
    var pageGeneral = window.FindName("pageGeneral") as StackPanel;
    var pageAdvanced = window.FindName("pageAdvanced") as StackPanel;
    var pageNotifications = window.FindName("pageNotifications") as StackPanel;
    var pageLogs = window.FindName("pageLogs") as UIElement;
    var pageAbout = window.FindName("pageAbout") as StackPanel;
    var contentScrollViewer = window.FindName("contentScrollViewer") as UIElement;
    var txtPageTitle = window.FindName("txtPageTitle") as TextBlock;

    navList.SelectionChanged += (_, e) =>
    {
      var selected = navList.SelectedIndex;
      var isLogsPage = selected == 3;
      this.SetLogPageActive(isLogsPage);
      UpdatePageTitle(selected, txtPageTitle);

      if (pageGeneral != null) pageGeneral.Visibility = Visibility.Collapsed;
      if (pageAdvanced != null) pageAdvanced.Visibility = Visibility.Collapsed;
      if (pageNotifications != null) pageNotifications.Visibility = Visibility.Collapsed;
      if (pageLogs != null) pageLogs.Visibility = Visibility.Collapsed;
      if (pageAbout != null) pageAbout.Visibility = Visibility.Collapsed;

      // The logs page is placed outside contentScrollViewer to avoid nested scrollbars.
      // When logs are active we hide the outer ScrollViewer; otherwise we show it.
      if (contentScrollViewer != null)
        contentScrollViewer.Visibility = isLogsPage ? Visibility.Collapsed : Visibility.Visible;

      switch (selected)
      {
        case 0:
          if (pageGeneral != null) pageGeneral.Visibility = Visibility.Visible;
          break;
        case 1:
          if (pageAdvanced != null) pageAdvanced.Visibility = Visibility.Visible;
          break;
        case 2:
          if (pageNotifications != null) pageNotifications.Visibility = Visibility.Visible;
          break;
        case 3:
          if (pageLogs != null) pageLogs.Visibility = Visibility.Visible;
          break;
        case 4:
          if (pageAbout != null) pageAbout.Visibility = Visibility.Visible;
          break;
      }

      if (isLogsPage)
        StabilizeLayout();
    };
  }

  private static void UpdatePageTitle(int selectedIndex, TextBlock? txtPageTitle)
  {
    if (txtPageTitle is null)
      return;

    txtPageTitle.Text = selectedIndex switch
    {
      0 => "常规设置",
      1 => "高级设置",
      2 => "通知设置",
      3 => "日志",
      4 => "关于",
      _ => "常规设置",
    };
  }

  private void ToggleTheme(Window window)
  {
    _isDarkTheme = !_isDarkTheme;
    _toastService.IsDarkMode = _isDarkTheme;
    var resources = window.Resources;

    if (_isDarkTheme)
    {
      SetResource(resources, "WindowBackground", "#202020");
      SetResource(resources, "WindowForeground", "#FFFFFF");
      SetResource(resources, "NavigationBackground", "#1C1C1C");
      SetResource(resources, "NavigationItemForeground", "#FFFFFF");
      SetResource(resources, "NavigationItemSelectedBackground", "#2D2D2D");
      SetResource(resources, "NavigationItemSelectedForeground", "#4CC2FF");
      SetResource(resources, "NavigationItemHoverBackground", "#2A2A2A");
      SetResource(resources, "NavigationBorderBrush", "#3A3A3A");
      SetResource(resources, "CardBackground", "#2D2D2D");
      SetResource(resources, "CardBorderBrush", "#3A3A3A");
      SetResource(resources, "CardShadowColor", "#40000000");
      SetResource(resources, "TextPrimary", "#FFFFFF");
      SetResource(resources, "TextSecondary", "#B0B0B0");
      SetResource(resources, "TextTertiary", "#8A8A8A");
      SetResource(resources, "TextAccent", "#4CC2FF");
      SetResource(resources, "PrimaryButtonBackground", "#4CC2FF");
      SetResource(resources, "PrimaryButtonForeground", "#1A1A1A");
      SetResource(resources, "PrimaryButtonHoverBackground", "#3AA8E0");
      SetResource(resources, "SecondaryButtonBackground", "#2D2D2D");
      SetResource(resources, "SecondaryButtonForeground", "#FFFFFF");
      SetResource(resources, "SecondaryButtonBorderBrush", "#5A5A5A");
      SetResource(resources, "SecondaryButtonHoverBackground", "#3A3A3A");
      SetResource(resources, "ToggleTrackOff", "#5A5A5A");
      SetResource(resources, "ToggleTrackOn", "#4CC2FF");
      SetResource(resources, "ToggleThumb", "#FFFFFF");
      SetResource(resources, "NumberBoxBackground", "#2D2D2D");
      SetResource(resources, "NumberBoxBorderBrush", "#5A5A5A");
      SetResource(resources, "NumberBoxButtonBackground", "#3A3A3A");
      SetResource(resources, "NumberBoxButtonHoverBackground", "#4A4A4A");
      SetResource(resources, "InfoBarBackground", "#1A3A5C");
      SetResource(resources, "InfoBarForeground", "#4CC2FF");
      SetResource(resources, "InfoBarBorderBrush", "#2A5A8A");
      SetResource(resources, "DividerBrush", "#3A3A3A");
    }
    else
    {
      SetResource(resources, "WindowBackground", "#F3F3F3");
      SetResource(resources, "WindowForeground", "#1A1A1A");
      SetResource(resources, "NavigationBackground", "#F9F9F9");
      SetResource(resources, "NavigationItemForeground", "#1A1A1A");
      SetResource(resources, "NavigationItemSelectedBackground", "#E5E5E5");
      SetResource(resources, "NavigationItemSelectedForeground", "#005FB8");
      SetResource(resources, "NavigationItemHoverBackground", "#EEEEEE");
      SetResource(resources, "NavigationBorderBrush", "#E0E0E0");
      SetResource(resources, "CardBackground", "#FFFFFF");
      SetResource(resources, "CardBorderBrush", "#E5E5E5");
      SetResource(resources, "CardShadowColor", "#20000000");
      SetResource(resources, "TextPrimary", "#1A1A1A");
      SetResource(resources, "TextSecondary", "#5C5C5C");
      SetResource(resources, "TextTertiary", "#8A8A8A");
      SetResource(resources, "TextAccent", "#005FB8");
      SetResource(resources, "PrimaryButtonBackground", "#005FB8");
      SetResource(resources, "PrimaryButtonForeground", "#FFFFFF");
      SetResource(resources, "PrimaryButtonHoverBackground", "#004578");
      SetResource(resources, "SecondaryButtonBackground", "#FFFFFF");
      SetResource(resources, "SecondaryButtonForeground", "#1A1A1A");
      SetResource(resources, "SecondaryButtonBorderBrush", "#D1D1D1");
      SetResource(resources, "SecondaryButtonHoverBackground", "#F0F0F0");
      SetResource(resources, "ToggleTrackOff", "#C4C4C4");
      SetResource(resources, "ToggleTrackOn", "#005FB8");
      SetResource(resources, "ToggleThumb", "#FFFFFF");
      SetResource(resources, "NumberBoxBackground", "#FFFFFF");
      SetResource(resources, "NumberBoxBorderBrush", "#D1D1D1");
      SetResource(resources, "NumberBoxButtonBackground", "#F0F0F0");
      SetResource(resources, "NumberBoxButtonHoverBackground", "#E5E5E5");
      SetResource(resources, "InfoBarBackground", "#E8F3FF");
      SetResource(resources, "InfoBarForeground", "#005FB8");
      SetResource(resources, "InfoBarBorderBrush", "#B3D7F7");
      SetResource(resources, "DividerBrush", "#E5E5E5");
    }

    LogViewTagPalette.ConfigureForDarkMode(_isDarkTheme);
    WindowTitleBarTheme.Apply(window, _isDarkTheme);
    RefreshLogView(forceRedraw: true);

    var iconTheme = window.FindName("iconTheme") as TextBlock;
    if (iconTheme != null)
      iconTheme.Text = _isDarkTheme ? "\uE706" : "\uE708";

    var txtTheme = window.FindName("txtTheme") as TextBlock;
    if (txtTheme != null)
      txtTheme.Text = _isDarkTheme ? "浅色模式" : "深色模式";
  }

  private static string GetDisplayVersion()
  {
    var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
    if (version is null) return "1.0.0";
    // Show major.minor.build when build is non-zero; otherwise major.minor
    if (version.Build > 0)
      return $"{version.Major}.{version.Minor}.{version.Build}";
    return $"{version.Major}.{version.Minor}";
  }

  private static void SetResource(ResourceDictionary resources, string key, string colorHex)
  {
    if (resources.Contains(key))
    {
      var existing = resources[key];
      var newColor = (Color)ColorConverter.ConvertFromString(colorHex);
      if (existing is SolidColorBrush)
      {
        // Brushes loaded from XAML BAML are frozen; replace the entire instance
        resources[key] = new SolidColorBrush(newColor);
      }
      else if (existing is Color)
      {
        resources[key] = newColor;
      }
    }
  }

  private static Window? TryLoadEmbeddedWindow()
    => SettingsXamlLoader.TryLoadEmbeddedWindow(out _);

  private static void ApplyWindowIcon(Window window, string root)
    => AppBrandIcon.ApplyTo(window, root);

  private static void ApplyAppHeaderIcon(Window window, string root)
  {
    // kept for tests that reflect on private helpers
    AppBrandIcon.ApplyTo(window, root);
  }

  private static T Require<T>(Window window, string name) where T : class
  {
    return window.FindName(name) as T
      ?? throw new InvalidOperationException($"Missing control: {name}");
  }

  private static void RegisterNumberBoxLabel(NumberBox numberBox, TextBlock label, string format)
  {
    void Update(object? sender, RoutedPropertyChangedEventArgs<int> e)
      => label.Text = string.Format(format, numberBox.Value);

    numberBox.ValueChanged += Update;
    label.Text = string.Format(format, numberBox.Value);
  }

  private static void RegisterHeartbeatLabel(NumberBox numberBox, TextBlock label)
  {
    void Update(object? sender, RoutedPropertyChangedEventArgs<int> e)
      => label.Text = numberBox.Value == 0 ? "关闭" : $"{numberBox.Value} 分钟";

    numberBox.ValueChanged += Update;
    label.Text = numberBox.Value == 0 ? "关闭" : $"{numberBox.Value} 分钟";
  }

  private void PopulatePlanCombo(ComboBox combo, Guid selectedGuid, string orphanRoleLabel)
  {
    _suppressPlanComboEvents = true;
    try
    {
      combo.DisplayMemberPath = nameof(PowerPlanComboItem.DisplayName);
      combo.SelectedValuePath = nameof(PowerPlanComboItem.PlanGuid);
      var items = PowerPlanComboItemsBuilder.Build(_planCatalog, selectedGuid, orphanRoleLabel);
      combo.ItemsSource = items;
      combo.SelectedItem = PlanComboSelection.FindItem(items, selectedGuid);
      if (combo.SelectedItem is null && selectedGuid != Guid.Empty)
        combo.SelectedValue = selectedGuid;
    }
    finally
    {
      _suppressPlanComboEvents = false;
    }
  }

  private static Guid ReadSelectedPlanGuid(ComboBox combo)
  {
    if (combo.SelectedItem is PowerPlanComboItem item)
      return item.PlanGuid;
    if (combo.SelectedValue is Guid guid)
      return guid;
    return Guid.Empty;
  }

  private GuardConfig ReadConfigFromUi()
  {
    return SettingsSnapshotMapper.ApplyTraySettings(
      _originalConfig,
      balancedThresholdMin: _sldBalanced.Value,
      powerSaverThresholdMin: _sldSaver.Value,
      lowBatteryPercent: _sldBattery.Value,
      checkIntervalSec: _sldPoll.Value,
      brightnessRestoreMs: _sldBrightMs.Value,
      heartbeatIntervalMin: _sldHeartbeat.Value,
      activePlanGuid: ReadSelectedPlanGuid(_cmbActivePlan),
      balancedPlanGuid: ReadSelectedPlanGuid(_cmbBalancedPlan),
      powerSaverPlanGuid: ReadSelectedPlanGuid(_cmbPowerSaverPlan),
      paused: _tglPaused.IsChecked == true,
      notifyOnPlanChange: _tglNotify.IsChecked == true,
      autoStartEnabled: _tglAutoStart.IsChecked == true);
  }

  private void UpdatePlanMappingStatus(GuardConfig? config = null)
  {
    if (_lblPlanMappingStatus is null) return;

    config ??= ReadConfigFromUi();
    var messages = PowerPlanMappingValidator.Validate(config, _planCatalog);
    _lblPlanMappingStatus.Text = messages.Count == 0
      ? "三档计划映射正常"
      : string.Join("；", messages);
  }

  private void ApplyConfigToUi(GuardConfig config)
  {
    _sldBalanced.Value = SettingsInitialValues.BalancedThresholdMinutes(config);
    _sldSaver.Value = SettingsInitialValues.PowerSaverThresholdMinutes(config);
    _sldBattery.Value = config.LowBatteryPercent;
    _sldPoll.Value = config.CheckIntervalSec;
    _sldBrightMs.Value = config.BrightnessRestoreMs;
    _sldHeartbeat.Value = config.HeartbeatIntervalMin;
    _tglPaused.IsChecked = config.Paused;
    _tglNotify.IsChecked = config.NotifyOnPlanChange;
    _tglAutoStart.IsChecked = config.AutoStartEnabled;
    PopulatePlanCombo(_cmbActivePlan, config.ActivePlanGuid, "高性能");
    PopulatePlanCombo(_cmbBalancedPlan, config.BalancedPlanGuid, "平衡");
    PopulatePlanCombo(_cmbPowerSaverPlan, config.PowerSaverPlanGuid, "节能");
    UpdatePlanMappingStatus(config);
  }

  private void ResetToDefaults()
  {
    if (!AppDialog.ShowConfirm(
          _window,
          "恢复默认策略？",
          "将把守护策略恢复为默认值。\n\n日志文件路径与 GitHub Token 会保留，手动高性能接管会被清除。",
          AppDialogSeverity.Warning))
      return;

    try
    {
      var resetConfig = GuardConfigResetService.CreateResetConfig(_originalConfig, _root);
      SettingsSaveCoordinator.Save(resetConfig, _originalConfig, _root, _repository);
      _originalConfig = resetConfig;
      ApplyConfigToUi(resetConfig);
      _toastService.Show("已恢复默认策略", isError: false);
    }
    catch (Exception ex)
    {
      _toastService.Show($"恢复失败：{ex.Message}", isError: true);
    }
  }

  private static (Window Window, ProgressBar Bar, TextBlock Status, CancellationTokenSource Cts) CreateDownloadProgressWindow(Window owner)
  {
    var cts = new CancellationTokenSource();

    var statusText = new TextBlock
    {
      Text = "正在下载更新...",
      FontSize = 14,
      Foreground = new SolidColorBrush(System.Windows.Media.Colors.Black),
      Margin = new Thickness(0, 0, 0, 12),
    };

    var progressBar = new ProgressBar
    {
      Minimum = 0,
      Maximum = 100,
      Height = 6,
      IsIndeterminate = false,
    };

    var content = new StackPanel();
    content.Children.Add(statusText);
    content.Children.Add(progressBar);

    var surface = new Border
    {
      Background = new SolidColorBrush(System.Windows.Media.Colors.White),
      BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0xE5, 0xE5)),
      BorderThickness = new Thickness(1),
      CornerRadius = new CornerRadius(20),
      Padding = new Thickness(24, 20, 24, 20),
      MinWidth = 320,
      Child = content,
    };

    var window = new Window
    {
      Title = string.Empty,
      WindowStyle = WindowStyle.None,
      AllowsTransparency = true,
      Background = Brushes.Transparent,
      SizeToContent = SizeToContent.WidthAndHeight,
      WindowStartupLocation = WindowStartupLocation.CenterOwner,
      Owner = owner,
      ResizeMode = ResizeMode.NoResize,
      ShowInTaskbar = false,
      Content = surface,
    };

    window.Closing += (_, _) =>
    {
      if (!cts.IsCancellationRequested)
        cts.Cancel();
    };

    return (window, progressBar, statusText, cts);
  }

  private static void ShowUpdateAlert(Window owner, string message, AppDialogSeverity severity)
  {
    if (SettingsUiTestMode.IsEnabled)
      return;

    owner.Dispatcher.Invoke(() => AppDialog.ShowAlert(owner, "检查更新", message, severity));
  }

  private static bool ShowUpdateConfirm(Window owner, string title, string message, AppDialogSeverity severity)
  {
    if (SettingsUiTestMode.IsEnabled)
      return false;

    return owner.Dispatcher.Invoke(() => AppDialog.ShowConfirm(owner, title, message, severity));
  }

  private async Task CheckForUpdateAsync(Window owner)
  {
    if (SettingsUiTestMode.IsEnabled)
      return;

    const string repoOwner = "rainiva";
    const string repoName = "SmartGuard";
    var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    // Cache "no update" results for 5 minutes to avoid GitHub API rate limits.
    if (_lastUpdateCheckNoUpdate && DateTime.Now - _lastUpdateCheckTime < TimeSpan.FromMinutes(5))
    {
      ShowUpdateAlert(owner, "当前已是最新版本。", AppDialogSeverity.Information);
      return;
    }

    try
    {
      var proxy = System.Net.WebRequest.GetSystemWebProxy();
      proxy.Credentials = System.Net.CredentialCache.DefaultNetworkCredentials;
      using var handler = new System.Net.Http.HttpClientHandler
      {
        Proxy = proxy,
        UseProxy = true,
        DefaultProxyCredentials = System.Net.CredentialCache.DefaultNetworkCredentials
      };
      using var client = new System.Net.Http.HttpClient(handler);
      client.DefaultRequestHeaders.Add("User-Agent", "SmartGuard-UpdateChecker");
      client.Timeout = TimeSpan.FromSeconds(30);

      var token = _originalConfig.GitHubToken;
      if (!string.IsNullOrWhiteSpace(token))
      {
        client.DefaultRequestHeaders.Authorization =
          new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Trim());
      }

      var url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";

      const int maxAttempts = 3;
      Exception? lastException = null;
      System.Net.Http.HttpResponseMessage? response = null;
      for (var attempt = 1; attempt <= maxAttempts; attempt++)
      {
        try
        {
          response = await client.GetAsync(url);
          response.EnsureSuccessStatusCode();
          lastException = null;
          break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
          lastException = ex;
          await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
        }
      }

      if (lastException is not null)
        throw lastException;

      if (response is null)
        throw new InvalidOperationException("No response received from update server.");

      var json = await response.Content.ReadAsStringAsync();
      using var doc = System.Text.Json.JsonDocument.Parse(json);
      var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";

      // Parse version from tag (e.g., "v1.2.3" -> "1.2.3")
      var latestVersionString = tagName.TrimStart('v', 'V');
      if (!Version.TryParse(latestVersionString, out var latestVersion))
      {
        ShowUpdateAlert(owner, "无法解析最新版本号。", AppDialogSeverity.Warning);
        return;
      }

      var comparison = currentVersion.CompareTo(latestVersion);
      if (comparison < 0)
      {
        if (ShowUpdateConfirm(
              owner,
              "发现新版本",
              $"发现新版本：{latestVersion}\n当前版本：{currentVersion}\n\n是否下载并安装更新？",
              AppDialogSeverity.Information))
        {
          var asset = UpdateInstallerLauncher.ResolveAsset(doc.RootElement, latestVersion);
          if (string.IsNullOrEmpty(asset.AssetName) || string.IsNullOrEmpty(asset.DownloadUrl))
          {
            // Fallback to browser if the expected installer asset is missing.
            var releaseUrl = doc.RootElement.GetProperty("html_url").GetString()
              ?? $"https://github.com/{repoOwner}/{repoName}/releases";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(releaseUrl)
            {
              UseShellExecute = true
            });
            return;
          }

          var installerPath = UpdateInstallerLauncher.GetLocalInstallerPath(asset.AssetName);
          var (progressWindow, progressBar, statusText, cts) = CreateDownloadProgressWindow(owner);
          var downloadCompleted = false;
          progressWindow.Show();

          try
          {
            var downloadProxy = System.Net.WebRequest.GetSystemWebProxy();
            downloadProxy.Credentials = System.Net.CredentialCache.DefaultNetworkCredentials;
            using var downloadHandler = new System.Net.Http.HttpClientHandler
            {
              Proxy = downloadProxy,
              UseProxy = true,
              DefaultProxyCredentials = System.Net.CredentialCache.DefaultNetworkCredentials
            };
            using var httpClient = new System.Net.Http.HttpClient(downloadHandler);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "SmartGuard-UpdateDownloader");
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            var downloadToken = _originalConfig.GitHubToken;
            if (!string.IsNullOrWhiteSpace(downloadToken))
            {
              httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", downloadToken.Trim());
            }
            using var downloader = new HttpUpdateAssetDownloader(httpClient);

            var progress = new Progress<double>(value =>
            {
              progressBar.Value = value * 100;
              statusText.Text = $"已下载 {value:P0}";
            });

            await downloader.DownloadAsync(asset.DownloadUrl, installerPath, progress, cts.Token);
            downloadCompleted = true;
            progressWindow.Close();

            UpdateInstallerLauncher.StartInstaller(installerPath);
            owner.Close();
          }
          catch (OperationCanceledException)
          {
            progressWindow.Close();
            if (!downloadCompleted)
            {
              ShowUpdateAlert(owner, "下载已取消。", AppDialogSeverity.Information);
            }
          }
          catch (Exception ex)
          {
            progressWindow.Close();
            ShowUpdateAlert(owner, $"下载更新失败：{ex.Message}", AppDialogSeverity.Error);
          }
        }
      }
      else
      {
        _lastUpdateCheckNoUpdate = true;
        _lastUpdateCheckTime = DateTime.Now;
        ShowUpdateAlert(owner, "当前已是最新版本。", AppDialogSeverity.Information);
      }
    }
    catch (System.Net.Http.HttpRequestException ex)
    {
      var statusCode = ex.StatusCode;
      var detail = ex.InnerException?.Message ?? ex.Message;
      var tokenConfigured = !string.IsNullOrWhiteSpace(_originalConfig.GitHubToken) ? "已配置" : "未配置";
      string message;
      if (statusCode == System.Net.HttpStatusCode.NotFound)
        message = $"未找到发布版本，请确认仓库地址正确。\n\nToken 状态：{tokenConfigured}\n详情：{detail}";
      else if (statusCode == System.Net.HttpStatusCode.Forbidden)
        message = $"请求过于频繁，请稍后再试。\n\nToken 状态：{tokenConfigured}\n详情：{detail}";
      else
        message = $"网络连接失败，请检查网络后重试。\n\nToken 状态：{tokenConfigured}\n详情：{detail}";
      ShowUpdateAlert(owner, message, AppDialogSeverity.Warning);
    }
    catch (TaskCanceledException)
    {
      ShowUpdateAlert(owner, "连接超时，请检查网络后重试。", AppDialogSeverity.Warning);
    }
    catch (Exception ex)
    {
      ShowUpdateAlert(owner, $"检查更新时发生错误：{ex.Message}", AppDialogSeverity.Error);
    }
  }
}
