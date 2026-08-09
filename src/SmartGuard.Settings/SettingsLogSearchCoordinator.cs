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
  private int _matchGeneration;

  internal SettingsLogSearchCoordinator(Window window, Action refreshLogView)
  {
    _window = window;
    _refreshLogView = refreshLogView;
  }

  internal LogMatchNavigator MatchNavigator { get; } = new();

  internal int MatchGeneration => _matchGeneration;

  internal event Action<int>? MatchGenerationChanged;

  internal System.Windows.Threading.DispatcherTimer? SearchDebounceTimerForTests => _searchDebounceTimer;

  internal System.Windows.Threading.DispatcherTimer? CustomRangeDebounceTimerForTests => _customRangeDebounceTimer;

  internal void Dispose()
  {
    _searchDebounceTimer?.Stop();
    _customRangeDebounceTimer?.Stop();
  }

  internal int NotifyMatchGenerationChanged()
  {
    _matchGeneration = unchecked(_matchGeneration + 1);
    MatchNavigator.NotifyQueryGenerationChanged(_matchGeneration);
    MatchGenerationChanged?.Invoke(_matchGeneration);
    return _matchGeneration;
  }

  internal void QueueSearchRefresh()
  {
    NotifyMatchGenerationChanged();
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
    MatchNavigator.SetMatches(_matchGeneration, MatchNavigator.CurrentMatches, scanComplete: true, visibleIds);

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
    => MatchNavigator.SetMatches(_matchGeneration, matches, scanComplete, visibleEventIds);

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
    TextBox? rangeEnd,
    ComboBox? levelFilterComboBox = null,
    ComboBox? componentFilterComboBox = null)
  {
    controller.SearchKeyword = filterBar?.Keyword ?? string.Empty;
    if (levelFilterComboBox is not null)
    {
      var level = ReadFilterValue(levelFilterComboBox);
      controller.ActiveTagFilters = string.IsNullOrWhiteSpace(level)
        ? filterBar?.ActiveTags ?? []
        : [level];
    }
    else if (filterBar?.ActiveTags.Count > 0)
    {
      controller.ActiveTagFilters = filterBar.ActiveTags;
    }

    if (componentFilterComboBox is not null)
      controller.ComponentFilter = ReadFilterValue(componentFilterComboBox);
    controller.SearchCaseSensitive = caseSensitiveCheckBox?.IsChecked == true;
    controller.TimeRange = ReadTimeRange(timeRangeComboBox);
    controller.CustomRangeStart = TryReadCustomRangeStart(rangeStart);
    controller.CustomRangeEnd = TryReadCustomRangeEnd(rangeEnd);
  }

  internal static string ReadFilterValue(ComboBox? comboBox)
    => comboBox?.SelectedItem is ComboBoxItem item
      ? item.Tag?.ToString() ?? string.Empty
      : string.Empty;

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
