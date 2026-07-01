using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings;

internal sealed class SettingsLogSearchCoordinator
{
  private readonly Window _window;
  private readonly Action _refreshLogView;
  private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;
  private System.Windows.Threading.DispatcherTimer? _customRangeDebounceTimer;

  internal SettingsLogSearchCoordinator(Window window, Action refreshLogView)
  {
    _window = window;
    _refreshLogView = refreshLogView;
  }

  internal System.Windows.Threading.DispatcherTimer? SearchDebounceTimerForTests => _searchDebounceTimer;

  internal System.Windows.Threading.DispatcherTimer? CustomRangeDebounceTimerForTests => _customRangeDebounceTimer;

  internal void Dispose()
  {
    _searchDebounceTimer?.Stop();
    _customRangeDebounceTimer?.Stop();
  }

  internal void QueueSearchRefresh() => QueueDebouncedRefresh(ref _searchDebounceTimer);

  internal void QueueCustomRangeRefresh() => QueueDebouncedRefresh(ref _customRangeDebounceTimer);

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
