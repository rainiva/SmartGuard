using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

public sealed class SettingsThemeCoordinator
{
  private readonly Window _window;
  private readonly SettingsThemeState _state;
  private readonly CheckBox _tglThemeFollowSystem;
  private readonly CheckBox _tglThemeLight;
  private readonly CheckBox _tglThemeDark;
  private readonly Border _rowThemeFollowSystem;
  private readonly Border _rowThemeLight;
  private readonly Border _rowThemeDark;
  private readonly ToastNotificationService _toastService;
  private readonly Func<GuardConfig> _readConfigFromUi;
  private readonly Action<GuardConfig> _commitSavedConfig;
  private readonly Action _onAfterThemeApply;
  private bool _suppressThemeEvents;
  private SystemThemeWatcher? _systemThemeWatcher;

  public SettingsThemeCoordinator(
    Window window,
    SettingsThemeState state,
    CheckBox tglThemeFollowSystem,
    CheckBox tglThemeLight,
    CheckBox tglThemeDark,
    Border rowThemeFollowSystem,
    Border rowThemeLight,
    Border rowThemeDark,
    ToastNotificationService toastService,
    Func<GuardConfig> readConfigFromUi,
    Action<GuardConfig> commitSavedConfig,
    Action onAfterThemeApply)
  {
    _window = window;
    _state = state;
    _tglThemeFollowSystem = tglThemeFollowSystem;
    _tglThemeLight = tglThemeLight;
    _tglThemeDark = tglThemeDark;
    _rowThemeFollowSystem = rowThemeFollowSystem;
    _rowThemeLight = rowThemeLight;
    _rowThemeDark = rowThemeDark;
    _toastService = toastService;
    _readConfigFromUi = readConfigFromUi;
    _commitSavedConfig = commitSavedConfig;
    _onAfterThemeApply = onAfterThemeApply;
  }

  public SettingsThemeState State => _state;

  public bool IsDarkThemeEnabled => _state.IsDarkThemeApplied;

  public void Initialize(Window window)
  {
    _suppressThemeEvents = true;
    _tglThemeFollowSystem.IsChecked = _state.FollowSystem;
    UpdateThemeControlsVisibility();
    _suppressThemeEvents = false;

    RefreshThemeFromSource(window);

    _systemThemeWatcher?.Dispose();
    _systemThemeWatcher = new SystemThemeWatcher();
    _systemThemeWatcher.SystemThemeChanged += (_, _) =>
    {
      window.Dispatcher.Invoke(() =>
      {
        if (_state.FollowSystem)
          RefreshThemeFromSource(window);
      });
    };
  }

  public void OnThemeFollowSystemChanged(bool enabled)
  {
    if (_suppressThemeEvents)
      return;

    _state.FollowSystem = enabled;
    UpdateThemeControlsVisibility();
    RefreshThemeFromSource(_window);
    SaveThemePreferences();
  }

  public void OnThemeLightChanged(bool enabled)
  {
    if (_suppressThemeEvents || _state.FollowSystem)
      return;

    SetManualTheme(isDark: !enabled);
  }

  public void OnThemeDarkChanged(bool enabled)
  {
    if (_suppressThemeEvents || _state.FollowSystem)
      return;

    SetManualTheme(isDark: enabled);
  }

  public void SaveThemePreferences()
  {
    var merged = _readConfigFromUi();
    merged.ThemeFollowSystem = _state.FollowSystem;
    merged.ThemeIsDark = _state.IsDark;
    _commitSavedConfig(merged);
  }

  private void SetManualTheme(bool isDark)
  {
    _state.IsDark = isDark;
    ApplyTheme(_window, isDark);
    _suppressThemeEvents = true;
    _tglThemeLight.IsChecked = !isDark;
    _tglThemeDark.IsChecked = isDark;
    _suppressThemeEvents = false;
    SaveThemePreferences();
  }

  private void RefreshThemeFromSource(Window window)
  {
    var isDark = _state.FollowSystem ? SystemThemeWatcher.IsSystemDarkMode() : _state.IsDark;
    ApplyTheme(window, isDark);
  }

  private void UpdateThemeControlsVisibility()
  {
    var manualVisible = _state.FollowSystem ? Visibility.Collapsed : Visibility.Visible;
    _rowThemeLight.Visibility = manualVisible;
    _rowThemeDark.Visibility = manualVisible;
    _rowThemeFollowSystem.BorderThickness = _state.FollowSystem
      ? new Thickness(0)
      : new Thickness(0, 0, 0, 1);

    if (_state.FollowSystem)
      return;

    _suppressThemeEvents = true;
    _tglThemeLight.IsChecked = !_state.IsDark;
    _tglThemeDark.IsChecked = _state.IsDark;
    _suppressThemeEvents = false;
  }

  private void ApplyTheme(Window window, bool isDark)
  {
    _state.IsDarkThemeApplied = isDark;
    _toastService.IsDarkMode = isDark;
    var resources = window.Resources;

    void FinishThemeApply()
    {
      LogViewTagPalette.ConfigureForDarkMode(isDark);
      _onAfterThemeApply();
    }

    if (!_state.Initialized || SettingsUiTestMode.IsEnabled)
    {
      WindowTitleBarTheme.Apply(window, isDark);
      ThemeTransitionAnimator.ApplyImmediate(resources, isDark);
      _state.Initialized = true;
      FinishThemeApply();
      return;
    }

    ThemeTransitionAnimator.AnimateTransition(
      window,
      resources,
      isDark,
      FinishThemeApply,
      onMidpoint: () => WindowTitleBarTheme.Apply(window, isDark));
  }

  public void Dispose()
  {
    _systemThemeWatcher?.Dispose();
  }
}
