using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
  private readonly CheckBox _tglPaused;
  private readonly CheckBox _tglNotify;
  private readonly CheckBox _tglAutoStart;
  private bool _isDarkTheme;
  private LogViewController? _logController;
  private System.Windows.Threading.DispatcherTimer? _logTimer;
  private System.Windows.Threading.DispatcherTimer? _saveDebounceTimer;

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
    _tglPaused = tglPaused;
    _tglNotify = tglNotify;
    _tglAutoStart = tglAutoStart;
  }

  public static SettingsWindowController? TryCreate(string root, GuardConfigRepository repository, GuardConfig config)
  {
    // 1. Try embedded resource first (works for single-file publish)
    Window? window = TryLoadEmbeddedWindow();

    // 2. Fallback to file system (development / loose file mode)
    if (window is null)
    {
      var xamlPath = Path.Combine(root, "lib", "SmartGuard.Settings.xaml");
      if (File.Exists(xamlPath))
      {
        try
        {
          var xaml = File.ReadAllText(xamlPath);
          window = (Window)XamlReader.Parse(xaml);
        }
        catch
        {
          // Parse failed, window remains null
        }
      }
    }

    if (window is null)
      return null;

    ApplyWindowIcon(window, root);

    var sldBalanced = Require<NumberBox>(window, "sldBalanced");
    var sldSaver = Require<NumberBox>(window, "sldSaver");
    var sldBattery = Require<NumberBox>(window, "sldBattery");
    var sldPoll = Require<NumberBox>(window, "sldPoll");
    var sldBrightMs = Require<NumberBox>(window, "sldBrightMs");
    var lblBalanced = Require<TextBlock>(window, "lblBalanced");
    var lblSaver = Require<TextBlock>(window, "lblSaver");
    var lblBattery = Require<TextBlock>(window, "lblBattery");
    var lblPoll = Require<TextBlock>(window, "lblPoll");
    var lblBrightMs = Require<TextBlock>(window, "lblBrightMs");
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
      tglPaused,
      tglNotify,
      tglAutoStart);

    // Initialize values
    sldBalanced.Value = SettingsInitialValues.BalancedThresholdMinutes(config);
    sldSaver.Value = SettingsInitialValues.PowerSaverThresholdMinutes(config);
    sldBattery.Value = config.LowBatteryPercent;
    sldPoll.Value = config.CheckIntervalSec;
    sldBrightMs.Value = config.BrightnessRestoreMs;
    tglPaused.IsChecked = config.Paused;
    tglNotify.IsChecked = config.NotifyOnPlanChange;
    tglAutoStart.IsChecked = config.AutoStartEnabled;

    // Sync displayed version with the actual assembly / installer version
    txtVersion.Text = GetDisplayVersion();

    // Register label updates for NumberBox
    RegisterNumberBoxLabel(sldBalanced, lblBalanced, "{0} 分钟");
    RegisterNumberBoxLabel(sldSaver, lblSaver, "{0} 分钟");
    RegisterNumberBoxLabel(sldBattery, lblBattery, "{0}%");
    RegisterNumberBoxLabel(sldPoll, lblPoll, "{0} 秒");
    RegisterNumberBoxLabel(sldBrightMs, lblBrightMs, "{0} 毫秒");

    // Instant-apply: queue a save when any setting changes.
    void QueueSave() => controller.QueueSave();
    sldBalanced.ValueChanged += (_, _) => QueueSave();
    sldSaver.ValueChanged += (_, _) => QueueSave();
    sldBattery.ValueChanged += (_, _) => QueueSave();
    sldPoll.ValueChanged += (_, _) => QueueSave();
    sldBrightMs.ValueChanged += (_, _) => QueueSave();

    tglPaused.Checked += (_, _) => QueueSave();
    tglPaused.Unchecked += (_, _) => QueueSave();
    tglNotify.Checked += (_, _) => QueueSave();
    tglNotify.Unchecked += (_, _) => QueueSave();
    tglAutoStart.Checked += (_, _) => QueueSave();
    tglAutoStart.Unchecked += (_, _) => QueueSave();

    // Navigation
    controller.SetupNavigation(navList, window);

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
        await controller.CheckForUpdateAsync(window);
      }
      catch (Exception ex)
      {
        MessageBox.Show(
          $"检查更新时发生错误：{ex.Message}",
          "检查更新",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      }
      finally
      {
        btnCheckUpdate.Content = "检查更新";
        btnCheckUpdate.IsEnabled = true;
      }
    };

    // Log view initialization
    var logPath = Path.Combine(root, "SmartGuard.log");
    var fallbackLogPath = Path.Combine(root, "SmartGuard.startup.log");
    if (File.Exists(logPath) || File.Exists(fallbackLogPath))
    {
      var logController = new LogViewController(logPath, fallbackLogPath);
      var txtLogSearch = Require<TextBox>(window, "txtLogSearch");
      var chkInfo = Require<CheckBox>(window, "chkInfo");
      var chkWarn = Require<CheckBox>(window, "chkWarn");
      var chkError = Require<CheckBox>(window, "chkError");
      var chkHeart = Require<CheckBox>(window, "chkHeart");
      var txtLogView = Require<TextBox>(window, "txtLogView");
      var lblLogStatus = Require<TextBlock>(window, "lblLogStatus");

      void RefreshLogView()
      {
        logController.SearchKeyword = txtLogSearch.Text;
        logController.ShowInfo = chkInfo.IsChecked == true;
        logController.ShowWarn = chkWarn.IsChecked == true;
        logController.ShowError = chkError.IsChecked == true;
        logController.ShowHeart = chkHeart.IsChecked == true;

        var lines = logController.GetFilteredLines();
        txtLogView.Text = string.Join(Environment.NewLine, lines);
        lblLogStatus.Text = $"{lines.Count} 行 | 刷新: {DateTime.Now:HH:mm:ss}";
      }

      txtLogSearch.TextChanged += (_, _) => RefreshLogView();
      chkInfo.Checked += (_, _) => RefreshLogView();
      chkInfo.Unchecked += (_, _) => RefreshLogView();
      chkWarn.Checked += (_, _) => RefreshLogView();
      chkWarn.Unchecked += (_, _) => RefreshLogView();
      chkError.Checked += (_, _) => RefreshLogView();
      chkError.Unchecked += (_, _) => RefreshLogView();
      chkHeart.Checked += (_, _) => RefreshLogView();
      chkHeart.Unchecked += (_, _) => RefreshLogView();

      var logTimer = new System.Windows.Threading.DispatcherTimer
      {
        Interval = TimeSpan.FromSeconds(2),
      };
      logTimer.Tick += (_, _) => RefreshLogView();
      logTimer.Start();

      controller._logController = logController;
      controller._logTimer = logTimer;

      // Initial refresh
      RefreshLogView();
    }

    return controller;
  }

  public bool? ShowDialog()
  {
    _window.Topmost = true;
    try
    {
      return _window.ShowDialog();
    }
    finally
    {
      _window.Topmost = false;
    }
  }

  public void Activate()
  {
    _window.Dispatcher.Invoke(() =>
    {
      if (!_window.IsVisible)
        _window.Show();
      _window.WindowState = WindowState.Normal;
      _window.Activate();
      _window.Topmost = true;
      _window.Topmost = false;
      _window.Focus();
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

  private void SaveCurrentSettings()
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
        paused: _tglPaused.IsChecked == true,
        notifyOnPlanChange: _tglNotify.IsChecked == true,
        autoStartEnabled: _tglAutoStart.IsChecked == true);

      var errors = GuardConfigValidator.Validate(newConfig);
      if (errors.Count > 0)
      {
        _toastService.Show("保存失败：" + string.Join("；", errors), isError: true);
        return;
      }

      SettingsSaveCoordinator.Save(newConfig, _originalConfig, _root, _repository);
      _originalConfig = newConfig;
      _toastService.Show("设置已保存", isError: false);
    }
    catch (Exception ex)
    {
      _toastService.Show($"保存失败：{ex.Message}", isError: true);
    }
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

    navList.SelectionChanged += (_, e) =>
    {
      var selected = navList.SelectedIndex;
      var isLogsPage = selected == 3;

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

    var txtTheme = window.FindName("txtTheme") as TextBlock;
    var iconTheme = window.FindName("iconTheme") as TextBlock;
    if (txtTheme != null)
      txtTheme.Text = _isDarkTheme ? "浅色模式" : "深色模式";
    if (iconTheme != null)
      iconTheme.Text = _isDarkTheme ? "\uE706" : "\uE708";
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
  {
    try
    {
      return (Window)Application.LoadComponent(
        new Uri("/SmartGuard.Settings;component/SmartGuard.Settings.xaml", UriKind.Relative));
    }
    catch
    {
      return null;
    }
  }

  private static void ApplyWindowIcon(Window window, string root)
  {
    var iconPath = Path.Combine(root, "lib", "SmartGuard.ico");
    if (!File.Exists(iconPath)) return;

    try
    {
      window.Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
    }
    catch
    {
      // keep default icon
    }
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

  private static (Window Window, ProgressBar Bar, TextBlock Status, CancellationTokenSource Cts) CreateDownloadProgressWindow(Window owner)
  {
    var cts = new CancellationTokenSource();

    var grid = new Grid { Margin = new Thickness(20) };
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    var statusText = new TextBlock
    {
      Text = "正在下载更新...",
      FontSize = 13,
      Foreground = new SolidColorBrush(System.Windows.Media.Colors.Black)
    };
    Grid.SetRow(statusText, 0);

    var progressBar = new ProgressBar
    {
      Minimum = 0,
      Maximum = 100,
      Height = 18,
      IsIndeterminate = false
    };
    Grid.SetRow(progressBar, 2);

    grid.Children.Add(statusText);
    grid.Children.Add(progressBar);

    var window = new Window
    {
      Title = "下载更新",
      Width = 360,
      Height = 130,
      WindowStartupLocation = WindowStartupLocation.CenterOwner,
      Owner = owner,
      ResizeMode = ResizeMode.NoResize,
      Content = grid,
      Background = new SolidColorBrush(System.Windows.Media.Colors.White)
    };

    window.Closing += (_, _) =>
    {
      if (!cts.IsCancellationRequested)
        cts.Cancel();
    };

    return (window, progressBar, statusText, cts);
  }

  private async Task CheckForUpdateAsync(Window owner)
  {
    const string repoOwner = "rainiva";
    const string repoName = "SmartGuard";
    var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    try
    {
      using var handler = new System.Net.Http.HttpClientHandler { UseProxy = true };
      using var client = new System.Net.Http.HttpClient(handler);
      client.DefaultRequestHeaders.Add("User-Agent", "SmartGuard-UpdateChecker");
      client.Timeout = TimeSpan.FromSeconds(30);

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
        MessageBox.Show(
          "无法解析最新版本号。",
          "检查更新",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
        return;
      }

      var comparison = currentVersion.CompareTo(latestVersion);
      if (comparison < 0)
      {
        var result = MessageBox.Show(
          $"发现新版本：{latestVersion}\n当前版本：{currentVersion}\n\n是否下载并安装更新？",
          "发现新版本",
          MessageBoxButton.YesNo,
          MessageBoxImage.Information);
        if (result == MessageBoxResult.Yes)
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
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "SmartGuard-UpdateDownloader");
            httpClient.Timeout = TimeSpan.FromMinutes(10);
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
              MessageBox.Show(
                "下载已取消。",
                "检查更新",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            }
          }
          catch (Exception ex)
          {
            progressWindow.Close();
            MessageBox.Show(
              $"下载更新失败：{ex.Message}",
              "检查更新",
              MessageBoxButton.OK,
              MessageBoxImage.Error);
          }
        }
      }
      else
      {
        MessageBox.Show(
          "当前已是最新版本。",
          "检查更新",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
      }
    }
    catch (System.Net.Http.HttpRequestException ex)
    {
      var statusCode = ex.StatusCode;
      var message = statusCode == System.Net.HttpStatusCode.NotFound
        ? "未找到发布版本，请确认仓库地址正确。"
        : "网络连接失败，请检查网络后重试。";
      MessageBox.Show(
        message,
        "检查更新",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
    }
    catch (TaskCanceledException)
    {
      MessageBox.Show(
        "连接超时，请检查网络后重试。",
        "检查更新",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
    }
    catch (Exception ex)
    {
      MessageBox.Show(
        $"检查更新时发生错误：{ex.Message}",
        "检查更新",
        MessageBoxButton.OK,
        MessageBoxImage.Error);
    }
  }
}
