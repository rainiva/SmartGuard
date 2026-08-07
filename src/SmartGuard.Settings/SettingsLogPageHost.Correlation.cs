using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

internal sealed partial class SettingsLogPageHost
{
  private readonly LogCorrelationCoordinator _correlationCoordinator = new();
  private Border? _logCorrelationBanner;
  private TextBlock? _lblLogCorrelationTitle;
  private Button? _btnLogCorrelationBack;

  internal LogCorrelationCoordinator CorrelationCoordinatorForTests => _correlationCoordinator;

  private void EnsureCorrelationControls()
  {
    _logCorrelationBanner ??= _window.FindName("logCorrelationBanner") as Border;
    _lblLogCorrelationTitle ??= _window.FindName("lblLogCorrelationTitle") as TextBlock;
    _btnLogCorrelationBack ??= _window.FindName("btnLogCorrelationBack") as Button;
  }

  private void WireCorrelationUi()
  {
    EnsureCorrelationControls();
    if (_btnLogCorrelationBack is not null)
      _btnLogCorrelationBack.Click += (_, _) => ExitCorrelationView();
  }

  private LogCorrelationMainViewState CaptureMainViewState()
  {
    var controller = _logController
      ?? throw new InvalidOperationException("Log controller is not initialized.");

    SettingsLogSearchCoordinator.ApplyFilters(
      controller,
      _logSearchFilterBar,
      _chkLogSearchCaseSensitive,
      _cmbLogTimeRange,
      _txtLogRangeStart,
      _txtLogRangeEnd);

    var filterState = LogViewFilterState.FromLegacyFields(
      controller.SearchKeyword,
      controller.SearchCaseSensitive,
      controller.ActiveTagFilters,
      controller.TimeRange,
      controller.CustomRangeStart,
      controller.CustomRangeEnd,
      controller.OperationIdFilter,
      controller.EventNameFilter,
      controller.ProcessIdFilter,
      controller.ThreadIdFilter,
      controller.NowProvider);

    string? selectedEventId = null;
    if (_lstLogView?.SelectedItem is LogLineDisplayItem selectedItem)
      selectedEventId = selectedItem.EventId;

    return new LogCorrelationMainViewState(
      filterState.QueryExpression,
      _logSearchFilterBar?.Keyword ?? string.Empty,
      _logSearchFilterBar?.ActiveTags ?? [],
      controller.TimeRange,
      controller.CustomRangeStart,
      controller.CustomRangeEnd,
      controller.SearchCaseSensitive,
      _logViewport.CaptureAnchor(),
      selectedEventId);
  }

  private async void EnterCorrelationView(LogEvent anchorEvent)
  {
    if (_disposed || _logController is null || _logListPresenter is null || _lblLogStatus is null)
      return;

    EnsureCorrelationControls();
    if (!_correlationCoordinator.TryEnterCorrelationView(anchorEvent, CaptureMainViewState()))
      return;

    LogStatusLiveRegion.Set(_lblLogStatus, "正在加载关联事件…");
    UpdateCorrelationBannerVisibility(true);

    try
    {
      var query = _correlationCoordinator.BuildActiveQuery();
      var service = new LogQueryService();
      var correlatedEvents = await service.QueryMatchingEventsAsync(
        _logController.EventSourceForCorrelation,
        query,
        CancellationToken.None);

      _logController.LoadCorrelatedEvents(correlatedEvents);
      var refreshResult = _logController.CreateRefreshResult();
      ApplyRefreshResult(refreshResult, forceRedraw: true);

      if (!string.IsNullOrWhiteSpace(_correlationCoordinator.ActiveTitle)
          && _lblLogCorrelationTitle is not null)
        _lblLogCorrelationTitle.Text = _correlationCoordinator.ActiveTitle;
    }
    catch (Exception ex)
    {
      _correlationCoordinator.ExitCorrelationView();
      UpdateCorrelationBannerVisibility(false);
      LogStatusLiveRegion.Set(_lblLogStatus, $"{LoadFailedStatusText}：{ex.Message}");
    }
  }

  private void ExitCorrelationView()
  {
    if (_disposed)
      return;

    var restored = _correlationCoordinator.ExitCorrelationView();
    UpdateCorrelationBannerVisibility(false);
    if (restored is null || _logController is null || _logSearchFilterBar is null)
      return;

    RestoreMainViewState(restored);
    ForceRefreshLogView();

    if (restored.Anchor is not null)
      _logViewport.RestoreAnchor(restored.Anchor);

    if (!string.IsNullOrWhiteSpace(restored.SelectedEventId) && _lstLogView is not null)
    {
      var index = _logListPresenter?.FindIndexByEventId(restored.SelectedEventId) ?? -1;
      if (index >= 0)
        _lstLogView.SelectedIndex = index;
    }
  }

  private void RestoreMainViewState(LogCorrelationMainViewState state)
  {
    if (_logController is null || _logSearchFilterBar is null)
      return;

    _logSearchFilterBar.SetKeyword(state.SearchKeyword);
    _logSearchFilterBar.ClearTagFilters();
    foreach (var tag in state.ActiveTags)
      _logSearchFilterBar.AddTagFilter(tag);

    _logController.SearchKeyword = state.SearchKeyword;
    _logController.ActiveTagFilters = state.ActiveTags;
    _logController.SearchCaseSensitive = state.SearchCaseSensitive;
    _logController.TimeRange = state.TimeRange;
    _logController.CustomRangeStart = state.CustomRangeStart;
    _logController.CustomRangeEnd = state.CustomRangeEnd;

    if (_cmbLogTimeRange is not null)
    {
      _cmbLogTimeRange.SelectedIndex = state.TimeRange switch
      {
        LogViewTimeRange.Today => 1,
        LogViewTimeRange.LastHour => 2,
        LogViewTimeRange.Custom => 3,
        _ => 0,
      };
    }

    if (_txtLogRangeStart is not null && state.CustomRangeStart is DateTime start)
      _txtLogRangeStart.Text = start.ToString("yyyy-MM-dd HH:mm:ss");

    if (_txtLogRangeEnd is not null && state.CustomRangeEnd is DateTime end)
      _txtLogRangeEnd.Text = end.ToString("yyyy-MM-dd HH:mm:ss");

    if (_chkLogSearchCaseSensitive is not null)
      _chkLogSearchCaseSensitive.IsChecked = state.SearchCaseSensitive;

    SettingsLogSearchCoordinator.SyncCustomRangePanelVisibility(_panelLogCustomRange, _cmbLogTimeRange);
  }

  private void UpdateCorrelationBannerVisibility(bool visible)
  {
    EnsureCorrelationControls();
    if (_logCorrelationBanner is null)
      return;

    _logCorrelationBanner.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
  }
}
