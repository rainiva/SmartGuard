using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings;

internal sealed class SettingsNavigationShell
{
  private readonly Window _window;
  private System.Windows.Threading.DispatcherTimer? _layoutStabilityTimer;
  private bool _layoutHooksAttached;

  internal SettingsNavigationShell(Window window) => _window = window;

  internal void Wire(
    ListBox navList,
    Action<bool> setLogPageActive,
    Action stabilizeLayout)
  {
    var pageGeneral = _window.FindName("pageGeneral") as StackPanel;
    var pageAdvanced = _window.FindName("pageAdvanced") as StackPanel;
    var pageNotifications = _window.FindName("pageNotifications") as StackPanel;
    var pageDisplay = _window.FindName("pageDisplay") as StackPanel;
    var pageLogs = _window.FindName("pageLogs") as UIElement;
    var pageAbout = _window.FindName("pageAbout") as StackPanel;
    var contentScrollViewer = _window.FindName("contentScrollViewer") as UIElement;
    var txtPageTitle = _window.FindName("txtPageTitle") as TextBlock;

    navList.SelectionChanged += (_, _) =>
    {
      var selected = navList.SelectedIndex;
      var isLogsPage = selected == 3;
      UpdatePageTitle(selected, txtPageTitle);

      if (pageGeneral != null) pageGeneral.Visibility = Visibility.Collapsed;
      if (pageAdvanced != null) pageAdvanced.Visibility = Visibility.Collapsed;
      if (pageNotifications != null) pageNotifications.Visibility = Visibility.Collapsed;
      if (pageDisplay != null) pageDisplay.Visibility = Visibility.Collapsed;
      if (pageLogs != null) pageLogs.Visibility = Visibility.Collapsed;
      if (pageAbout != null) pageAbout.Visibility = Visibility.Collapsed;

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
          if (pageDisplay != null) pageDisplay.Visibility = Visibility.Visible;
          break;
        case 5:
          if (pageAbout != null) pageAbout.Visibility = Visibility.Visible;
          break;
      }

      if (isLogsPage)
        stabilizeLayout();

      setLogPageActive(isLogsPage);
    };
  }

  internal void AttachLayoutTheme(Func<bool> isDarkThemeProvider, Action stabilizeLayout)
  {
    SettingsWindowPresentation.RegisterShowHooks(_window);
    _window.Loaded += (_, _) =>
    {
      if (_layoutHooksAttached)
        return;

      _layoutHooksAttached = true;
      SettingsWindowLayoutStability.Attach(
        _window,
        isDarkThemeProvider,
        stabilizeLayout,
        QueueLayoutStabilization);
    };
  }

  internal void NavigateTo(string page)
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
        "display" or "显示" => 4,
        "about" or "关于" => 5,
        _ => 0,
      };

      navList.SelectedIndex = targetIndex;
    });
  }

  internal void WireWindowState(ListBox navList, Action<bool, LogPageActivationReason> setLogPageActive)
  {
    _window.StateChanged += (_, _) =>
    {
      if (_window.WindowState == WindowState.Minimized)
        setLogPageActive(false, LogPageActivationReason.Navigation);
      else
        setLogPageActive(
          navList.SelectedIndex == 3,
          LogPageActivationReason.WindowRestored);
    };
  }

  internal void Dispose() => _layoutStabilityTimer?.Stop();

  private void QueueLayoutStabilization()
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
        SettingsWindowLayoutStability.StabilizeContentLayout(_window);
      };
    }

    _layoutStabilityTimer.Stop();
    _layoutStabilityTimer.Start();
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
      4 => "显示设置",
      5 => "关于",
      _ => "常规设置",
    };
  }
}
