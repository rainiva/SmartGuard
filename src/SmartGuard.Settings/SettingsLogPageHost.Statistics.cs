using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

internal sealed partial class SettingsLogPageHost
{
  private readonly LogAggregationService _aggregationService = new();
  private readonly LogStatisticsPresenter _statisticsPresenter = new();
  private CancellationTokenSource? _statisticsCancellation;
  private int _statisticsGeneration;
  private bool _statisticsDirty = true;
  private bool _diagnosticPageActive;

  internal LogAggregationService AggregationServiceForTests => _aggregationService;

  internal LogStatisticsPresenter StatisticsPresenterForTests => _statisticsPresenter;

  private void WireStatisticsUi()
  {
    var componentDistribution = _window.FindName("logComponentDistribution") as ItemsControl;
    var errorTrendChart = _window.FindName("logErrorTrendChart") as LogErrorTrendChart;
    var emptyStateLabel = _window.FindName("lblLogStatisticsEmpty") as TextBlock;

    _statisticsPresenter.Attach(componentDistribution, errorTrendChart, emptyStateLabel);

    if (_window.FindName("logStatisticsInspector") is Expander statisticsInspector)
      statisticsInspector.Expanded += (_, _) =>
      {
        if (_statisticsDirty)
          ScheduleStatisticsRefresh();
      };
  }

  internal void SetDiagnosticPageActive(bool active)
  {
    if (_disposed)
      return;

    _diagnosticPageActive = active;
    if (!active)
    {
      _statisticsCancellation?.Cancel();
      _statisticsDirty = true;
      return;
    }

    EnsureLogViewInitialized();
    ScheduleStatisticsRefresh();
  }

  private void ScheduleStatisticsRefresh()
  {
    if (_disposed || _logController is null)
      return;

    if (!_diagnosticPageActive ||
        _window.FindName("logStatisticsInspector") is not Expander statisticsInspector ||
        !statisticsInspector.IsExpanded)
    {
      _statisticsDirty = true;
      return;
    }

    _statisticsDirty = false;
    _statisticsGeneration = _aggregationService.NotifyGenerationChanged();
    var generation = _statisticsGeneration;

    _statisticsCancellation?.Cancel();
    _statisticsCancellation?.Dispose();
    _statisticsCancellation = new CancellationTokenSource();
    var cancellationToken = _statisticsCancellation.Token;

    var request = BuildAggregationRequest();
    var eventSource = _logController.EventSourceForCorrelation;

    _ = Task.Run(async () =>
    {
      var result = await _aggregationService.AggregateAsync(
        eventSource,
        request,
        generation,
        cancellationToken).ConfigureAwait(false);

      if (_window.Dispatcher.HasShutdownStarted || _disposed)
        return;

      await _window.Dispatcher.InvokeAsync(
        () =>
        {
          if (_disposed || generation != _statisticsGeneration)
            return;

          _statisticsPresenter.Apply(result);
        },
        System.Windows.Threading.DispatcherPriority.Background);
    }, cancellationToken);
  }

  private LogAggregationRequest BuildAggregationRequest()
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

    var expression = _sessionCoordinator.IsActive
      ? _sessionCoordinator.BuildActiveQuery(filterState.QueryExpression)
      : filterState.QueryExpression;

    return new LogAggregationRequest(
      expression,
      TimeSpan.FromMinutes(10),
      TimeZoneInfo.Local);
  }
}
