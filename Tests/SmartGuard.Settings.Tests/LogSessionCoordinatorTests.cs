using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogSessionCoordinatorTests
{
  private static LogSessionMetadata CreateSession(bool isActive = false)
    => new(
      "s:e:start",
      new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero),
      new DateTimeOffset(2026, 8, 2, 10, 30, 0, TimeSpan.Zero),
      100,
      LogComponentRegistry.Engine,
      isActive,
      "e:start",
      "e:end");

  private static LogSessionMainViewState CreateMainState()
    => new(
      BaseExpression: LogQueryParser.Parse("severity:INFO").Expression!,
      SearchKeyword: "severity:INFO",
      ActiveTags: ["INFO"],
      TimeRange: LogViewTimeRange.LastHour,
      CustomRangeStart: new DateTime(2026, 8, 2, 9, 0, 0),
      CustomRangeEnd: new DateTime(2026, 8, 2, 11, 0, 0),
      SearchCaseSensitive: true,
      Anchor: new LogViewportAnchor("e:anchor", 2, 80, 5),
      SelectedEventId: "e:anchor");

  [Fact]
  public void SelectSession_builds_query_clause_equivalent_to_session_window()
  {
    var coordinator = new LogSessionCoordinator();
    var session = CreateSession();

    coordinator.SelectSession(session, CreateMainState());

    coordinator.IsActive.Should().BeTrue();
    coordinator.ActiveTitle.Should().Contain("2026-08-02");

    var query = coordinator.BuildActiveQuery();
    query.Should().BeOfType<LogQueryAndExpression>();
    var and = (LogQueryAndExpression)query;
    and.Left.Should().Be(CreateMainState().BaseExpression);
    and.Right.Should().BeOfType<LogQueryAndExpression>();
  }

  [Fact]
  public void ExitSessionView_restores_saved_main_state()
  {
    var coordinator = new LogSessionCoordinator();
    var mainState = CreateMainState();

    coordinator.SelectSession(CreateSession(), mainState);
    var restored = coordinator.ExitSessionView();

    coordinator.IsActive.Should().BeFalse();
    restored.Should().BeEquivalentTo(mainState);
  }

  [Fact]
  public async Task LoadSessionMetadataAsync_does_not_preload_session_events()
  {
    var coordinator = new LogSessionCoordinator();
    var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
    var events = new[]
    {
      CreateEvent("e:start", baseTime, EventNameRegistry.AppStarted),
      CreateEvent("e:live", baseTime.AddMinutes(1)),
    };

    var sessions = await coordinator.LoadSessionMetadataAsync(
      new FakeEventSource(events),
      CancellationToken.None);

    sessions.Should().ContainSingle();
    coordinator.IsActive.Should().BeFalse();
  }

  private static LogEvent CreateEvent(
    string eventId,
    DateTimeOffset timestamp,
    string? eventName = null)
    => new(
      LogEvent.CurrentSchemaVersion,
      eventId,
      timestamp,
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Lifecycle,
      eventName,
      "message",
      Exception: null,
      OperationId: null,
      TraceId: null,
      SpanId: null,
      UserActionId: null,
      ProcessId: 100,
      ThreadId: null,
      LogEvent.EmptyProperties);

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
