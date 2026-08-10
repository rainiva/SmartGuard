using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogCorrelationCoordinatorTests
{
  private static LogEvent CreateEvent(
    string eventId,
    string? operationId = null,
    string? traceId = null,
    string? userActionId = null,
    LogSeverity severity = LogSeverity.Info)
    => new(
      LogEvent.CurrentSchemaVersion,
      eventId,
      DateTimeOffset.UtcNow,
      severity,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Diagnostics,
      EventNameRegistry.AppStarted,
      "message",
      Exception: null,
      operationId,
      traceId,
      SpanId: null,
      userActionId,
      ProcessId: null,
      ThreadId: null,
      LogEvent.EmptyProperties);

  private static LogCorrelationMainViewState CreateMainState(
    string? selectedEventId = "e:anchor",
    LogViewportAnchor? anchor = null)
    => new(
      BaseExpression: LogQueryParser.Parse("severity:INFO").Expression!,
      SearchKeyword: "severity:INFO",
      ActiveTags: ["INFO"],
      TimeRange: LogViewTimeRange.LastHour,
      CustomRangeStart: new DateTime(2026, 8, 2, 9, 0, 0),
      CustomRangeEnd: new DateTime(2026, 8, 2, 10, 0, 0),
      SearchCaseSensitive: true,
      Anchor: anchor ?? new LogViewportAnchor("e:anchor", 4, 120, 3),
      SelectedEventId: selectedEventId);

  [Fact]
  public void EnterCorrelationView_saves_main_state_and_builds_operation_query()
  {
    var coordinator = new LogCorrelationCoordinator();
    var mainState = CreateMainState();
    var anchorEvent = CreateEvent("e:anchor", operationId: "op-42");

    coordinator.TryEnterCorrelationView(anchorEvent, mainState).Should().BeTrue();
    coordinator.IsActive.Should().BeTrue();
    coordinator.ActiveTitle.Should().Be("操作关联：op-42");

    var query = coordinator.BuildActiveQuery();
    LogQueryExpressionEvaluator.Matches(
      CreateEvent("e:matching", operationId: "op-42"),
      query).Should().BeTrue();
    LogQueryExpressionEvaluator.Matches(
      CreateEvent("e:wrong-level", operationId: "op-42", severity: LogSeverity.Error),
      query).Should().BeFalse();
    LogQueryExpressionEvaluator.Matches(
      CreateEvent("e:wrong-operation", operationId: "op-other"),
      query).Should().BeFalse();
  }

  [Fact]
  public void EnterCorrelationView_falls_back_to_trace_query_when_operation_missing()
  {
    var coordinator = new LogCorrelationCoordinator();
    var anchorEvent = CreateEvent("e:trace", traceId: "trace-9");

    coordinator.TryEnterCorrelationView(anchorEvent, CreateMainState()).Should().BeTrue();

    var query = coordinator.BuildActiveQuery();
    query.Should().BeOfType<LogQueryAndExpression>();
    var correlation = (LogQueryFieldExpression)((LogQueryAndExpression)query).Right;
    correlation.Field.Should().Be("traceid");
    correlation.Value.Should().Be("trace-9");
    coordinator.ActiveTitle.Should().Be("Trace 关联：trace-9");
  }

  [Fact]
  public void EnterCorrelationView_falls_back_to_user_action_query()
  {
    var coordinator = new LogCorrelationCoordinator();
    var anchorEvent = CreateEvent("e:ua", userActionId: "ua-7");

    coordinator.TryEnterCorrelationView(anchorEvent, CreateMainState()).Should().BeTrue();

    var query = coordinator.BuildActiveQuery();
    query.Should().BeOfType<LogQueryAndExpression>();
    var correlation = (LogQueryFieldExpression)((LogQueryAndExpression)query).Right;
    correlation.Field.Should().Be("useractionid");
    correlation.Value.Should().Be("ua-7");
  }

  [Fact]
    public void ExitCorrelationView_restores_saved_cursor_anchor_and_selection()
  {
    var coordinator = new LogCorrelationCoordinator();
    var mainState = CreateMainState(selectedEventId: "e:keep", anchor: new LogViewportAnchor("e:keep", 2, 80, 5));
    coordinator.TryEnterCorrelationView(CreateEvent("e:anchor", operationId: "op-1"), mainState).Should().BeTrue();

    var restored = coordinator.ExitCorrelationView();

    coordinator.IsActive.Should().BeFalse();
    restored.Should().BeEquivalentTo(mainState);
    restored!.SelectedEventId.Should().Be("e:keep");
    restored.Anchor!.EventId.Should().Be("e:keep");
    restored.SearchKeyword.Should().Be("severity:INFO");
        restored.ActiveTags.Should().Equal("INFO");
    }

    [Fact]
    public void Correlation_view_back_restores_page_filter_sort_and_selected_event()
    {
        var coordinator = new LogCorrelationCoordinator();
        var mainState = CreateMainState() with
        {
            PageNumber = 31,
            SortDirection = LogSortDirection.OldestFirst,
        };

        coordinator.TryEnterCorrelationView(
          CreateEvent("e:anchor", operationId: "op-1"),
          mainState).Should().BeTrue();

        var restored = coordinator.ExitCorrelationView();

        restored.Should().NotBeNull();
        restored!.PageNumber.Should().Be(31);
        restored.SortDirection.Should().Be(LogSortDirection.OldestFirst);
        restored.SearchKeyword.Should().Be("severity:INFO");
        restored.SelectedEventId.Should().Be("e:anchor");
    }

  [Fact]
  public void EnterCorrelationView_returns_false_when_no_correlation_key()
  {
    var coordinator = new LogCorrelationCoordinator();

    coordinator.TryEnterCorrelationView(CreateEvent("e:none"), CreateMainState()).Should().BeFalse();
    coordinator.IsActive.Should().BeFalse();
  }
}
