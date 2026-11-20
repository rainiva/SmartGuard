using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogOperationProjectorTests
{
  private static LogEvent CreateEvent(
    string eventId,
    DateTimeOffset timestamp,
    string component,
    string? operationId = null,
    string? traceId = null,
    string? userActionId = null)
  {
    return new LogEvent(
      LogEvent.CurrentSchemaVersion,
      eventId,
      timestamp,
      LogSeverity.Info,
      component,
      LogChannelRegistry.Diagnostics,
      EventNameRegistry.AppStarted,
      $"message-{eventId}",
      Exception: null,
      operationId,
      traceId,
      SpanId: null,
      userActionId,
      ProcessId: null,
      ThreadId: null,
      LogEvent.EmptyProperties);
  }

  [Fact]
  public void ResolveCorrelationKey_prefers_operation_id()
  {
    var logEvent = CreateEvent("e:1", DateTimeOffset.UtcNow, LogComponentRegistry.Engine, "op-1", "trace-1", "ua-1");

    var key = LogOperationGraph.ResolveCorrelationKey(logEvent);

    key.Should().NotBeNull();
    key!.Kind.Should().Be(LogCorrelationKeyKind.OperationId);
    key.Value.Should().Be("op-1");
  }

  [Fact]
  public void ResolveCorrelationKey_falls_back_to_trace_id()
  {
    var logEvent = CreateEvent("e:2", DateTimeOffset.UtcNow, LogComponentRegistry.Tray, traceId: "trace-2", userActionId: "ua-2");

    var key = LogOperationGraph.ResolveCorrelationKey(logEvent);

    key.Should().NotBeNull();
    key!.Kind.Should().Be(LogCorrelationKeyKind.TraceId);
    key.Value.Should().Be("trace-2");
  }

  [Fact]
  public void ResolveCorrelationKey_falls_back_to_user_action_id()
  {
    var logEvent = CreateEvent("e:3", DateTimeOffset.UtcNow, LogComponentRegistry.Settings, userActionId: "ua-3");

    var key = LogOperationGraph.ResolveCorrelationKey(logEvent);

    key.Should().NotBeNull();
    key!.Kind.Should().Be(LogCorrelationKeyKind.UserActionId);
    key.Value.Should().Be("ua-3");
  }

  [Fact]
  public void BuildCorrelationExpression_uses_operation_field()
  {
    var key = new LogOperationCorrelationKey(LogCorrelationKeyKind.OperationId, "op-shared");
    var expression = LogOperationGraph.BuildCorrelationExpression(key);

    expression.Should().BeOfType<LogQueryFieldExpression>();
    var field = (LogQueryFieldExpression)expression;
    field.Field.Should().Be("operationid");
    field.Value.Should().Be("op-shared");
  }

  [Fact]
  public void OrderCorrelatedEvents_sorts_by_timestamp_then_event_id()
  {
    var sharedTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
    var events = new[]
    {
      CreateEvent("e:b", sharedTime, LogComponentRegistry.Tray, operationId: "op-1"),
      CreateEvent("e:a", sharedTime, LogComponentRegistry.Engine, operationId: "op-1"),
      CreateEvent("e:c", sharedTime.AddSeconds(1), LogComponentRegistry.Settings, operationId: "op-1"),
    };

    var ordered = LogOperationProjector.OrderCorrelatedEvents(events);

    ordered.Select(e => e.EventId).Should().Equal("e:a", "e:b", "e:c");
  }

  [Fact]
  public async Task QueryMatchingEventsAsync_aggregates_cross_component_operation_events()
  {
    var sharedTime = DateTimeOffset.UtcNow;
    var events = new[]
    {
      CreateEvent("e:engine", sharedTime, LogComponentRegistry.Engine, operationId: "op-shared"),
      CreateEvent("e:tray", sharedTime.AddMilliseconds(1), LogComponentRegistry.Tray, operationId: "op-shared"),
      CreateEvent("e:other", sharedTime.AddMilliseconds(2), LogComponentRegistry.Settings, operationId: "op-other"),
    };
    var key = new LogOperationCorrelationKey(LogCorrelationKeyKind.OperationId, "op-shared");
    var expression = LogOperationGraph.CombineWithBase(null, LogOperationGraph.BuildCorrelationExpression(key));
    var service = new LogQueryService();

    var matches = await service.QueryMatchingEventsAsync(new FakeEventSource(events), expression, CancellationToken.None);

    matches.Select(e => e.EventId).Should().Equal("e:engine", "e:tray");
    matches.Select(e => e.Component).Should().Equal(LogComponentRegistry.Engine, LogComponentRegistry.Tray);
  }

  private sealed class FakeEventSource : ILogEventSource
  {
    private readonly IReadOnlyList<LogEvent> _events;

    public FakeEventSource(IReadOnlyList<LogEvent> events) => _events = events;

    public Task<LogEventPage> ReadLatestAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadOlderAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadNewerAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public async IAsyncEnumerable<LogEvent> StreamAsync(
      LogReadScope scope,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
      foreach (var logEvent in _events)
      {
        cancellationToken.ThrowIfCancellationRequested();
        yield return logEvent;
      }

      await Task.CompletedTask;
    }
  }
}
