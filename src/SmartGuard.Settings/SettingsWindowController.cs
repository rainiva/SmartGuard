using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

public sealed class SettingsWindowController
{
  private readonly string _root;
  private readonly GuardConfigRepository _repository;
  private readonly GuardConfig _originalConfig;
  private readonly Window _window;
  private readonly NumberBox _sldBalanced;
  private readonly NumberBox _sldSaver;
  private readonly NumberBox _sldBattery;
  private readonly NumberBox _sldPoll;
  private readonly NumberBox _sldBrightMs;
  private readonly CheckBox _tglPaused;
  private readonly CheckBox _tglNotify;
  private readonly CheckBox _tglAutoStart;
  private GuardConfig? _pendingSave;
  private bool _isDarkTheme;

  private SettingsWindowController(
    string root,
    GuardConfigRepository repository,
    GuardConfig originalConfig,
    Window window,
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
    var btnSave = Require<Button>(window, "btnSave");
    var btnCancel = Require<Button>(window, "btnCancel");
    var navList = Require<ListBox>(window, "navList");
    var btnThemeToggle = Require<Button>(window, "btnThemeToggle");
    var infoBar = Require<Border>(window, "infoBar");
    var txtInfoBar = Require<TextBlock>(window, "txtInfoBar");

    var controller = new SettingsWindowController(
      root,
      repository,
      config,
      window,
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

    // Register label updates for NumberBox
    RegisterNumberBoxLabel(sldBalanced, lblBalanced, "{0} 分钟");
    RegisterNumberBoxLabel(sldSaver, lblSaver, "{0} 分钟");
    RegisterNumberBoxLabel(sldBattery, lblBattery, "{0}%");
    RegisterNumberBoxLabel(sldPoll, lblPoll, "{0} 秒");
    RegisterNumberBoxLabel(sldBrightMs, lblBrightMs, "{0} 毫秒");

    // Navigation
    controller.SetupNavigation(navList, window);

    // Theme toggle
    btnThemeToggle.Click += (_, _) => controller.ToggleTheme(window);

    // InfoBar default message
    txtInfoBar.Text = "保存后立即生效";
    infoBar.Visibility = Visibility.Collapsed;

    btnCancel.Click += (_, _) =>
    {
      window.DialogResult = false;
      window.Close();
    };

    btnSave.Click += (_, _) => controller.OnSaveClicked(infoBar, txtInfoBar);

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

  public void CommitPendingSave()
  {
    if (_pendingSave is null) return;
    SettingsSaveCoordinator.Save(_pendingSave, _originalConfig, _root, _repository);
  }

  private void SetupNavigation(ListBox navList, Window window)
  {
    var pageGeneral = window.FindName("pageGeneral") as StackPanel;
    var pageAdvanced = window.FindName("pageAdvanced") as StackPanel;
    var pageNotifications = window.FindName("pageNotifications") as StackPanel;

    navList.SelectionChanged += (_, e) =>
    {
      if (pageGeneral != null) pageGeneral.Visibility = Visibility.Collapsed;
      if (pageAdvanced != null) pageAdvanced.Visibility = Visibility.Collapsed;
      if (pageNotifications != null) pageNotifications.Visibility = Visibility.Collapsed;

      var selected = navList.SelectedIndex;
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
      }
    };
  }

  private void ToggleTheme(Window window)
  {
    _isDarkTheme = !_isDarkTheme;
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

  private void OnSaveClicked(Border infoBar, TextBlock txtInfoBar)
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
      txtInfoBar.Text = string.Join("; ", errors);
      infoBar.Background = new SolidColorBrush(Color.FromRgb(253, 231, 233));
      infoBar.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 165, 169));
      var txtInfoBarForeground = infoBar.FindName("txtInfoBar") as TextBlock;
      if (txtInfoBarForeground != null)
        txtInfoBarForeground.Foreground = new SolidColorBrush(Color.FromRgb(197, 54, 59));
      infoBar.Visibility = Visibility.Visible;
      return;
    }

    _pendingSave = newConfig;
    _window.DialogResult = true;
    _window.Close();
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
}
