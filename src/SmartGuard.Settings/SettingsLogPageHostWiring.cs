using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings;

internal static class SettingsLogPageHostWiring
{
  internal static void WireToolbarButtons(
    Window window,
    CheckBox? followTailCheckBox,
    SettingsLogFollowTailCoordinator followTailCoordinator,
    Action copyVisibleLog,
    Action exportVisibleLog,
    Action openLogFolder,
    Action forceRefreshLogView)
  {
    var btnLogCopy = window.FindName("btnLogCopy") as Button;
    var btnLogExport = window.FindName("btnLogExport") as Button;
    var btnLogOpenFolder = window.FindName("btnLogOpenFolder") as Button;
    var btnLogScrollTop = window.FindName("btnLogScrollTop") as Button;
    var btnLogScrollBottom = window.FindName("btnLogScrollBottom") as Button;
    var btnLogRefresh = window.FindName("btnLogRefresh") as Button;

    if (btnLogCopy is not null) btnLogCopy.Click += (_, _) => copyVisibleLog();
    if (btnLogExport is not null) btnLogExport.Click += (_, _) => exportVisibleLog();
    if (btnLogOpenFolder is not null) btnLogOpenFolder.Click += (_, _) => openLogFolder();
    if (btnLogScrollTop is not null) btnLogScrollTop.Click += (_, _) => followTailCoordinator.ScrollToTop();
    if (btnLogScrollBottom is not null) btnLogScrollBottom.Click += (_, _) => followTailCoordinator.ScrollToBottom();
    if (btnLogRefresh is not null) btnLogRefresh.Click += (_, _) => forceRefreshLogView();
    if (followTailCheckBox is not null)
    {
      followTailCheckBox.Checked += (_, _) => followTailCoordinator.SetFollowTail(true);
      followTailCheckBox.Unchecked += (_, _) => followTailCoordinator.SetFollowTail(false);
    }
  }

  internal static void WireSearchAndFilterEvents(
    LogSearchFilterBar filterBar,
    SettingsLogSearchCoordinator searchCoordinator,
    ComboBox? timeRangeComboBox,
    UIElement? customRangePanel,
    TextBox? rangeStart,
    TextBox? rangeEnd,
    CheckBox? caseSensitiveCheckBox,
    Action refreshLogView)
  {
    filterBar.FiltersChanged += (_, _) => searchCoordinator.QueueSearchRefresh();
    if (timeRangeComboBox is not null)
    {
      timeRangeComboBox.SelectionChanged += (_, _) =>
      {
        SettingsLogSearchCoordinator.SyncCustomRangePanelVisibility(customRangePanel, timeRangeComboBox);
        refreshLogView();
      };
    }

    if (rangeStart is not null)
      rangeStart.TextChanged += (_, _) => searchCoordinator.QueueCustomRangeRefresh();
    if (rangeEnd is not null)
      rangeEnd.TextChanged += (_, _) => searchCoordinator.QueueCustomRangeRefresh();
    if (caseSensitiveCheckBox is not null)
    {
      caseSensitiveCheckBox.Checked += (_, _) => refreshLogView();
      caseSensitiveCheckBox.Unchecked += (_, _) => refreshLogView();
    }
  }
}
