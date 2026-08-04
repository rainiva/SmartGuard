using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogSessionProjectorTests
{
  private static LogEvent CreateEvent(
    string eventId,
    DateTimeOffset timestamp,
    string? eventName = null,
    int? processId = null,
    string component = LogComponentRegistry.Engine,
    string message = "message")
    => new(
      LogEvent.CurrentSchemaVersion,
      eventId,
      timestamp,
      LogSeverity.Info,
      component,
      LogEventChannels.ForEventName(eventName ?? EventNameRegistry.HeartbeatTick),
      eventName,
      message,
      Exception: null,
      OperationId: null,
      TraceId: null,
      SpanId: null,
      UserActionId: null,
      processId,
      ThreadId: null,
      LogEvent.EmptyProperties);

  [Fact]
  public void ProjectSessions_splits_on_explicit_lifecycle_start_and_stop()
  {
    var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
    var events = new[]
    {
      CreateEvent("e:start-1", baseTime, EventNameRegistry.AppStarted),
      CreateEvent("e:mid-1", baseTime.AddMinutes(1)),
      CreateEvent("e:stop-1", baseTime.AddMinutes(2), EventNameRegistry.AppStopping),
      CreateEvent("e:start-2", baseTime.AddMinutes(3), EventNameRegistry.AppStarted),
      CreateEvent("e:mid-2", baseTime.AddMinutes(4)),
    };

    var sessions = LogSessionProjector.ProjectSessions(events);

    sessions.Should().HaveCount(2);
    sessions[0].StartEventId.Should().Be("e:start-1");
    sessions[0].EndEventId.Should().Be("e:stop-1");
    sessions[0].IsActive.Should().BeFalse();
    sessions[1].StartEventId.Should().Be("e:start-2");
    sessions[1].EndEventId.Should().Be("e:mid-2");
    sessions[1].IsActive.Should().BeTrue();
  }

  [Fact]
  public void ProjectSessions_degrades_legacy_logs_by_process_startup_and_gap()
  {
    var baseTime = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero);
    var events = new[]
    {
      CreateEvent("e:1", baseTime, processId: 100),
      CreateEvent("e:2", baseTime.AddMinutes(1), processId: 200),
      CreateEvent("e:3", baseTime.AddMinutes(2), processId: 200, message: "Engine started"),
      CreateEvent("e:4", baseTime.AddMinutes(40), processId: 200),
    };

    var sessions = LogSessionProjector.ProjectSessions(events);

    sessions.Should().HaveCount(4);
    sessions[0].StartEventId.Should().Be("e:1");
    sessions[1].StartEventId.Should().Be("e:2");
    sessions[2].StartEventId.Should().Be("e:3");
    sessions[3].StartEventId.Should().Be("e:4");
    sessions[3].IsActive.Should().BeTrue();
  }

  [Fact]
  public void ProjectSessions_keeps_session_continuous_across_archives()
  {
    var baseTime = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    var events = new[]
    {
      CreateEvent("e:archive-a", baseTime, processId: 4242, component: LogComponentRegistry.Engine),
      CreateEvent("e:archive-b", baseTime.AddMinutes(5), processId: 4242, component: LogComponentRegistry.Tray),
    };

    var sessions = LogSessionProjector.ProjectSessions(events);

    sessions.Should().ContainSingle();
    sessions[0].StartEventId.Should().Be("e:archive-a");
    sessions[0].EndEventId.Should().Be("e:archive-b");
    sessions[0].IsActive.Should().BeTrue();
  }

  [Fact]
  public void ProjectSessions_marks_current_running_session_as_active()
  {
    var baseTime = new DateTimeOffset(2026, 8, 2, 14, 0, 0, TimeSpan.Zero);
    var events = new[]
    {
      CreateEvent("e:start", baseTime, EventNameRegistry.AppStarted),
      CreateEvent("e:live", baseTime.AddMinutes(10)),
    };

    var sessions = LogSessionProjector.ProjectSessions(events);

    sessions.Should().ContainSingle();
    sessions[0].IsActive.Should().BeTrue();
    sessions[0].EndTimestamp.Should().Be(baseTime.AddMinutes(10));
  }

  [Fact]
  public void BuildSessionExpression_matches_only_events_in_selected_session()
  {
    var baseTime = new DateTimeOffset(2026, 8, 2, 16, 0, 0, TimeSpan.Zero);
    var session = new LogSessionMetadata(
      "s:e:start",
      baseTime,
      baseTime.AddMinutes(5),
      100,
      LogComponentRegistry.Engine,
      IsActive: false,
      "e:start",
      "e:end");
    var expression = LogSessionProjector.BuildSessionExpression(session);
    var inside = CreateEvent("e:inside", baseTime.AddMinutes(2));
    var outside = CreateEvent("e:outside", baseTime.AddMinutes(10));

    LogQueryExpressionEvaluator.Matches(inside, expression).Should().BeTrue();
    LogQueryExpressionEvaluator.Matches(outside, expression).Should().BeFalse();
  }

  [Fact]
  public async Task ProjectSessionsAsync_projects_metadata_without_materializing_all_events()
  {
    var baseTime = new DateTimeOffset(2026, 8, 2, 18, 0, 0, TimeSpan.Zero);
    var events = Enumerable.Range(0, 1000)
      .Select(index => CreateEvent($"e:{index}", baseTime.AddSeconds(index)))
      .ToList();
    var source = new TrackingEventSource(events);
    var service = new LogQueryService();

    var sessions = await service.ProjectSessionsAsync(source, CancellationToken.None);

    sessions.Should().ContainSingle();
    source.PeakHeldEvents.Should().BeLessThan(1000);
  }

  private sealed class TrackingEventSource : ILogEventSource
  {
    private readonly IReadOnlyList<LogEvent> _events;
    private int _peakHeldEvents;

    public TrackingEventSource(IReadOnlyList<LogEvent> events) => _events = events;

    public int PeakHeldEvents => _peakHeldEvents;

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
      var held = 0;
      foreach (var logEvent in _events)
      {
        cancellationToken.ThrowIfCancellationRequested();
        held++;
        _peakHeldEvents = Math.Max(_peakHeldEvents, held);
        yield return logEvent;
        held--;
      }

      await Task.CompletedTask;
    }
  }
}
