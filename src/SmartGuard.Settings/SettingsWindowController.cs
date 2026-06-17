using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

public sealed class SettingsWindowController
{
  private readonly string _root;
  private readonly GuardConfigRepository _repository;
  private readonly GuardConfig _originalConfig;
  private readonly Window _window;
  private readonly Slider _sldBalanced;
  private readonly Slider _sldSaver;
  private readonly Slider _sldBattery;
  private readonly Slider _sldPoll;
  private readonly Slider _sldBrightMs;
  private readonly CheckBox _tglPaused;
  private readonly CheckBox _tglNotify;
  private readonly CheckBox _tglAutoStart;
  private GuardConfig? _pendingSave;

  private SettingsWindowController(
    string root,
    GuardConfigRepository repository,
    GuardConfig originalConfig,
    Window window,
    Slider sldBalanced,
    Slider sldSaver,
    Slider sldBattery,
    Slider sldPoll,
    Slider sldBrightMs,
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
    Window? window = TryLoadEmbeddedWindow();
    if (window is null)
    {
      var xamlPath = Path.Combine(root, "lib", "SmartGuard.Settings.xaml");
      if (!File.Exists(xamlPath))
        return null;

      try
      {
        var xaml = File.ReadAllText(xamlPath);
        window = (Window)XamlReader.Parse(xaml);
      }
      catch
      {
        return null;
      }
    }

    ApplyWindowIcon(window, root);

    var sldBalanced = Require<Slider>(window, "sldBalanced");
    var sldSaver = Require<Slider>(window, "sldSaver");
    var sldBattery = Require<Slider>(window, "sldBattery");
    var sldPoll = Require<Slider>(window, "sldPoll");
    var sldBrightMs = Require<Slider>(window, "sldBrightMs");
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

    sldBalanced.Value = SettingsInitialValues.BalancedThresholdMinutes(config);
    sldSaver.Value = SettingsInitialValues.PowerSaverThresholdMinutes(config);
    sldBattery.Value = config.LowBatteryPercent;
    sldPoll.Value = config.CheckIntervalSec;
    sldBrightMs.Value = config.BrightnessRestoreMs;
    tglPaused.IsChecked = config.Paused;
    tglNotify.IsChecked = config.NotifyOnPlanChange;
    tglAutoStart.IsChecked = config.AutoStartEnabled;

    RegisterSliderLabel(sldBalanced, lblBalanced, "{0} 分钟");
    RegisterSliderLabel(sldSaver, lblSaver, "{0} 分钟");
    RegisterSliderLabel(sldBattery, lblBattery, "{0}%");
    RegisterSliderLabel(sldPoll, lblPoll, "{0} 秒");
    RegisterSliderLabel(sldBrightMs, lblBrightMs, "{0} 毫秒");

    btnCancel.Click += (_, _) =>
    {
      window.DialogResult = false;
      window.Close();
    };

    btnSave.Click += (_, _) => controller.OnSaveClicked();

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

  private void OnSaveClicked()
  {
    var newConfig = SettingsSnapshotMapper.ApplyTraySettings(
      _originalConfig,
      balancedThresholdMin: (int)_sldBalanced.Value,
      powerSaverThresholdMin: (int)_sldSaver.Value,
      lowBatteryPercent: (int)_sldBattery.Value,
      checkIntervalSec: (int)_sldPoll.Value,
      brightnessRestoreMs: (int)_sldBrightMs.Value,
      paused: _tglPaused.IsChecked == true,
      notifyOnPlanChange: _tglNotify.IsChecked == true,
      autoStartEnabled: _tglAutoStart.IsChecked == true);

    var errors = GuardConfigValidator.Validate(newConfig);
    if (errors.Count > 0)
    {
      MessageBox.Show(
        string.Join(Environment.NewLine, errors),
        "配置无效",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
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
        new Uri("/SmartGuard.Settings.xaml;component", UriKind.Relative));
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

  private static void RegisterSliderLabel(Slider slider, TextBlock label, string format)
  {
    void Update(object? sender, RoutedPropertyChangedEventArgs<double> e)
      => label.Text = string.Format(format, (int)slider.Value);

    slider.ValueChanged += Update;
    label.Text = string.Format(format, (int)slider.Value);
  }
}
