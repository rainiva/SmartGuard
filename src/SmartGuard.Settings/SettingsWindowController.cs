using System.IO;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

public sealed class SettingsWindowController
{
  private readonly string _root;
  private readonly Window _window;
  private readonly SettingsPolicyCoordinator _policyCoordinator;
  private readonly SettingsThemeCoordinator _themeCoordinator;
  private readonly SettingsAboutCoordinator _aboutCoordinator;
  private readonly SettingsNavigationShell _navigationShell;
  private readonly SettingsLogPageHost _logPageHost;
  private readonly ToastNotificationService _toastService;

  private string? _initialPage;

  internal bool IsDarkThemeEnabled => _themeCoordinator.IsDarkThemeEnabled;

  internal bool IsLogViewInitializedForTests => _logPageHost.IsLogViewInitializedForTests;

  internal static int ForceRefreshLogViewCountForTests =>
    SettingsLogPageHost.ForceRefreshLogViewCountForTests;

  internal static void ResetTestMetricsForTests() => SettingsLogPageHost.ResetTestMetricsForTests();

  private SettingsWindowController(
    string root,
    Window window,
    SettingsPolicyCoordinator policyCoordinator,
    SettingsThemeCoordinator themeCoordinator,
    SettingsAboutCoordinator aboutCoordinator,
    SettingsNavigationShell navigationShell,
    SettingsLogPageHost logPageHost,
    ToastNotificationService toastService)
  {
    _root = root;
    _window = window;
    _policyCoordinator = policyCoordinator;
    _themeCoordinator = themeCoordinator;
    _aboutCoordinator = aboutCoordinator;
    _navigationShell = navigationShell;
    _logPageHost = logPageHost;
    _toastService = toastService;
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
    try
    {
      var window = SettingsXamlLoader.TryLoadEmbeddedWindow(out var embeddedError);
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
    var toastContainer = Require<Border>(window, "toastContainer");
    var toastService = new ToastNotificationService(
      window,
      TimeSpan.FromSeconds(3),
      (message, isError, isDarkMode, _) => new InlineToastNotification(message, isError, isDarkMode, toastContainer));

    var policyCoordinator = new SettingsPolicyCoordinator(
      root,
      repository,
      config,
      window,
      toastService,
      Require<NumberBox>(window, "sldBalanced"),
      Require<NumberBox>(window, "sldSaver"),
      Require<NumberBox>(window, "sldBattery"),
      Require<NumberBox>(window, "sldPoll"),
      Require<NumberBox>(window, "sldBrightMs"),
      Require<NumberBox>(window, "sldHeartbeat"),
      Require<ComboBox>(window, "cmbActivePlan"),
      Require<ComboBox>(window, "cmbBalancedPlan"),
      Require<ComboBox>(window, "cmbPowerSaverPlan"),
      window.FindName("lblPlanMappingStatus") as TextBlock,
      Require<CheckBox>(window, "tglPaused"),
      Require<CheckBox>(window, "tglNotify"),
      Require<CheckBox>(window, "tglNotifyExternal"),
      Require<CheckBox>(window, "tglAutoStart"));

    var logPageHost = new SettingsLogPageHost(
      window,
      root,
      toastService,
      SmartGuardPaths.ResolveLogFile(config, root),
      SmartGuardPaths.StartupLogFile(root));

    var themeState = new SettingsThemeState
    {
      FollowSystem = config.ThemeFollowSystem,
      IsDark = config.ThemeIsDark,
    };
    toastService.IsDarkMode = themeState.IsDarkThemeApplied;

    var themeCoordinator = new SettingsThemeCoordinator(
      window,
      themeState,
      Require<CheckBox>(window, "tglThemeFollowSystem"),
      Require<CheckBox>(window, "tglThemeLight"),
      Require<CheckBox>(window, "tglThemeDark"),
      Require<Border>(window, "rowThemeFollowSystem"),
      Require<Border>(window, "rowThemeLight"),
      Require<Border>(window, "rowThemeDark"),
      toastService,
      () => policyCoordinator.ReadConfigFromUi(),
      policyCoordinator.CommitSavedConfig,
      () => logPageHost.RefreshLogViewForThemeChange());

    var aboutCoordinator = new SettingsAboutCoordinator();
    var navigationShell = new SettingsNavigationShell(window);

    var controller = new SettingsWindowController(
      root,
      window,
      policyCoordinator,
      themeCoordinator,
      aboutCoordinator,
      navigationShell,
      logPageHost,
      toastService);

    policyCoordinator.ApplyInitialValues(config);
    policyCoordinator.WireInstantApply();
    if (window.FindName("lblPlanMappingStatus") is TextBlock planStatus)
      planStatus.Text = "正在加载电源计划...";
    policyCoordinator.BeginLoadPlanCatalogAsync();

    SettingsControlLabels.RegisterNumberBoxLabels(window);
    aboutCoordinator.WireAboutPage(
      window,
      Require<TextBlock>(window, "txtVersion"),
      () => policyCoordinator.ReadConfigFromUi().GitHubToken);

    WireThemeToggles(window, themeCoordinator);
    themeCoordinator.Initialize(window);

    var navList = Require<ListBox>(window, "navList");
    navigationShell.Wire(
      navList,
      active => controller.SetLogPageActive(active),
      () => SettingsWindowLayoutStability.StabilizeContentLayout(window));
    navigationShell.AttachLayoutTheme(
      () => controller.IsDarkThemeEnabled,
      () => SettingsWindowLayoutStability.StabilizeContentLayout(window));
    navigationShell.WireWindowState(navList, controller.SetLogPageActive);

    window.Closing += (_, _) => controller.Dispose();

    return controller;
  }

  private static void WireThemeToggles(Window window, SettingsThemeCoordinator themeCoordinator)
  {
    var tglThemeFollowSystem = Require<CheckBox>(window, "tglThemeFollowSystem");
    var tglThemeLight = Require<CheckBox>(window, "tglThemeLight");
    var tglThemeDark = Require<CheckBox>(window, "tglThemeDark");

    tglThemeFollowSystem.Checked += (_, _) => themeCoordinator.OnThemeFollowSystemChanged(true);
    tglThemeFollowSystem.Unchecked += (_, _) => themeCoordinator.OnThemeFollowSystemChanged(false);
    tglThemeLight.Checked += (_, _) => themeCoordinator.OnThemeLightChanged(true);
    tglThemeLight.Unchecked += (_, _) => themeCoordinator.OnThemeLightChanged(false);
    tglThemeDark.Checked += (_, _) => themeCoordinator.OnThemeDarkChanged(true);
    tglThemeDark.Unchecked += (_, _) => themeCoordinator.OnThemeDarkChanged(false);
  }

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
      AppBrandIcon.ApplyTo(_window, _root);
      if (!_window.IsVisible)
        _window.Show();
      _window.WindowState = WindowState.Normal;
      SettingsWindowPresentation.BringToForeground(_window);
    });
  }

  public void NavigateTo(string page) => _navigationShell.NavigateTo(page);

  public void SetLogPageActive(bool active, LogPageActivationReason reason = LogPageActivationReason.Navigation)
    => _logPageHost.SetLogPageActive(active, reason);

  internal void AddLogTagFilter(string tag) => _logPageHost.AddLogTagFilter(tag);

  internal void EnsureLogScrollViewerHooked() => _logPageHost.EnsureLogScrollViewerHooked();

  internal void OnPauseToggled() => _policyCoordinator.OnPauseToggled();

  internal GuardConfig ReadConfigFromUi() => _policyCoordinator.ReadConfigFromUi();

  public void Dispose()
  {
    _policyCoordinator.Dispose();
    _logPageHost.Dispose();
    _navigationShell.Dispose();
    _themeCoordinator.Dispose();
    _toastService.Dispose();
  }

  private static T Require<T>(Window window, string name) where T : class
  {
    return window.FindName(name) as T
      ?? throw new InvalidOperationException($"Missing control: {name}");
  }
}
