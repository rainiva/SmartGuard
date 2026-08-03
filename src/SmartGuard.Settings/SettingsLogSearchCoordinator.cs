using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed class SettingsLogSearchCoordinator
{
  private readonly Window _window;
  private readonly Action _refreshLogView;
  private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;
  private System.Windows.Threading.DispatcherTimer? _customRangeDebounceTimer;
  private int _queryGeneration;

  internal SettingsLogSearchCoordinator(Window window, Action refreshLogView)
  {
    _window = window;
    _refreshLogView = refreshLogView;
  }

  internal LogMatchNavigator MatchNavigator { get; } = new();

  internal int QueryGeneration => _queryGeneration;

  internal System.Windows.Threading.DispatcherTimer? SearchDebounceTimerForTests => _searchDebounceTimer;

  internal System.Windows.Threading.DispatcherTimer? CustomRangeDebounceTimerForTests => _customRangeDebounceTimer;

  internal void Dispose()
  {
    _searchDebounceTimer?.Stop();
    _customRangeDebounceTimer?.Stop();
  }

  internal int NotifyQueryGenerationChanged()
  {
    _queryGeneration = unchecked(_queryGeneration + 1);
    MatchNavigator.NotifyQueryGenerationChanged(_queryGeneration);
    return _queryGeneration;
  }

  internal void QueueSearchRefresh()
  {
    NotifyQueryGenerationChanged();
    QueueDebouncedRefresh(ref _searchDebounceTimer);
  }

  internal void QueueCustomRangeRefresh() => QueueDebouncedRefresh(ref _customRangeDebounceTimer);

  internal void NavigateMatch(
    LogMatchNavigationDirection direction,
    LogViewListPresenter? presenter,
    LogViewportController? viewport,
    Action<LogQueryCursor>? loadPage)
  {
    var visibleIds = presenter?.Items.Select(item => item.EventId) ?? Array.Empty<string>();
    MatchNavigator.SetMatches(_queryGeneration, MatchNavigator.CurrentMatches, scanComplete: true, visibleIds);

    var reverse = direction == LogMatchNavigationDirection.Backward;
    var result = MatchNavigator.NavigateNext(reverse);
    if (result.TargetEventId is null)
      return;

    if (result.RequiresPageLoad)
    {
      if (result.TargetCursor is not null)
        loadPage?.Invoke(result.TargetCursor);
      return;
    }

    viewport?.ScrollToEvent(result.TargetEventId);
  }

  internal void UpdateMatchCatalog(IReadOnlyList<LogMatch> matches, bool scanComplete, IEnumerable<string> visibleEventIds)
    => MatchNavigator.SetMatches(_queryGeneration, matches, scanComplete, visibleEventIds);

  internal static void SyncCustomRangePanelVisibility(UIElement? panel, ComboBox? comboBox)
  {
    if (panel is null || comboBox is null)
      return;

    panel.Visibility = comboBox.SelectedIndex == 3
      ? Visibility.Visible
      : Visibility.Collapsed;
  }

  internal static LogViewTimeRange ReadTimeRange(ComboBox? comboBox)
    => comboBox?.SelectedIndex switch
    {
      1 => LogViewTimeRange.Today,
      2 => LogViewTimeRange.LastHour,
      3 => LogViewTimeRange.Custom,
      _ => LogViewTimeRange.All,
    };

  internal static DateTime? TryReadCustomRangeStart(TextBox? textBox)
    => LogViewCustomRangeParser.TryParse(textBox?.Text, out var value) ? value : null;

  internal static DateTime? TryReadCustomRangeEnd(TextBox? textBox)
    => LogViewCustomRangeParser.TryParse(textBox?.Text, out var value) ? value : null;

  internal static void ApplyFilters(
    LogViewController controller,
    LogSearchFilterBar? filterBar,
    CheckBox? caseSensitiveCheckBox,
    ComboBox? timeRangeComboBox,
    TextBox? rangeStart,
    TextBox? rangeEnd)
  {
    controller.SearchKeyword = filterBar?.Keyword ?? string.Empty;
    controller.ActiveTagFilters = filterBar?.ActiveTags ?? [];
    controller.SearchCaseSensitive = caseSensitiveCheckBox?.IsChecked == true;
    controller.TimeRange = ReadTimeRange(timeRangeComboBox);
    controller.CustomRangeStart = TryReadCustomRangeStart(rangeStart);
    controller.CustomRangeEnd = TryReadCustomRangeEnd(rangeEnd);
  }

  private void QueueDebouncedRefresh(ref System.Windows.Threading.DispatcherTimer? timer)
  {
    if (timer is null)
    {
      var created = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        _window.Dispatcher)
      {
        Interval = TimeSpan.FromMilliseconds(300),
      };
      created.Tick += (_, _) =>
      {
        created.Stop();
        _refreshLogView();
      };
      timer = created;
    }

    timer.Stop();
    timer.Start();
  }
}
