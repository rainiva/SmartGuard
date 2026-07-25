using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SmartGuard.Settings;

internal sealed record SettingsLogPageControls(
  LogViewController Controller,
  LogSearchFilterBar SearchFilterBar,
  ListBox ListView,
  LogViewListPresenter ListPresenter,
  TextBlock StatusLabel,
  CheckBox? FollowTailCheckBox,
  ComboBox? TimeRangeComboBox,
  UIElement? CustomRangePanel,
  TextBox? RangeStartTextBox,
  TextBox? RangeEndTextBox,
  CheckBox? CaseSensitiveCheckBox,
  DispatcherTimer RefreshTimer);

internal static class SettingsLogPageInitializer
{
  internal static SettingsLogPageControls Initialize(
    Window window,
    string logPath,
    string? fallbackLogPath,
    Action<string> addTagFilter,
    Action onListLoaded,
    Action refreshLogView)
  {
    var controller = new LogViewController(logPath, fallbackLogPath);
    var searchFilterBar = new LogSearchFilterBar();
    Require<ContentControl>(window, "logSearchFilterHost").Content = searchFilterBar;

    var tagFilterLinksPanel = Require<WrapPanel>(window, "logTagFilterLinksPanel");
    foreach (var tag in LogTagFilterCatalog.SelectableTags)
      tagFilterLinksPanel.Children.Add(LogTagFilterLinkFactory.CreateClickableTag(tag, addTagFilter));

    var listView = Require<ListBox>(window, "lstLogView");
    var listPresenter = new LogViewListPresenter();
    listPresenter.Attach(listView);
    var statusLabel = Require<TextBlock>(window, "lblLogStatus");
    listView.Loaded += (_, _) => onListLoaded();

    var followTailCheckBox = window.FindName("chkLogFollowTail") as CheckBox;
    var timeRangeComboBox = window.FindName("cmbLogTimeRange") as ComboBox;
    var customRangePanel = window.FindName("panelLogCustomRange") as UIElement;
    var rangeStartTextBox = window.FindName("txtLogRangeStart") as TextBox;
    var rangeEndTextBox = window.FindName("txtLogRangeEnd") as TextBox;
    var caseSensitiveCheckBox = window.FindName("chkLogSearchCaseSensitive") as CheckBox;

    SettingsLogSearchCoordinator.SyncCustomRangePanelVisibility(customRangePanel, timeRangeComboBox);

    var refreshTimer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
    {
      Interval = TimeSpan.FromSeconds(2),
    };
    refreshTimer.Tick += (_, _) => refreshLogView();

    return new SettingsLogPageControls(
      controller,
      searchFilterBar,
      listView,
      listPresenter,
      statusLabel,
      followTailCheckBox,
      timeRangeComboBox,
      customRangePanel,
      rangeStartTextBox,
      rangeEndTextBox,
      caseSensitiveCheckBox,
      refreshTimer);
  }

  private static T Require<T>(Window window, string name) where T : class
    => window.FindName(name) as T
      ?? throw new InvalidOperationException($"Missing control: {name}");
}
