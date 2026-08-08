using System.Windows.Input;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogMatchNavigationWpfTests
{
  [Fact]
  public void F3_and_ShiftF3_delegate_to_match_navigator()
  {
    LogMatchNavigationDirection? direction = null;
    LogMatchNavigationCommands.TryHandleKey(Key.F3, ModifierKeys.None, d => direction = d)
      .Should().BeTrue();
    direction.Should().Be(LogMatchNavigationDirection.Forward);

    LogMatchNavigationCommands.TryHandleKey(Key.F3, ModifierKeys.Shift, d => direction = d)
      .Should().BeTrue();
    direction.Should().Be(LogMatchNavigationDirection.Backward);
  }

  [Fact]
    public void Enter_and_ShiftEnter_delegate_to_match_navigator()
  {
    LogMatchNavigationDirection? direction = null;
    LogMatchNavigationCommands.TryHandleKey(Key.Enter, ModifierKeys.None, d => direction = d)
      .Should().BeTrue();
    direction.Should().Be(LogMatchNavigationDirection.Forward);

    LogMatchNavigationCommands.TryHandleKey(Key.Enter, ModifierKeys.Shift, d => direction = d)
      .Should().BeTrue();
        direction.Should().Be(LogMatchNavigationDirection.Backward);
    }

    [Fact]
    public void F3_moves_within_page_then_loads_next_numbered_page()
    {
        var state = LogPaginationState.Initial
            .ApplyExactCount(95, isScanComplete: true);
        var row = LogTableRow.FromEvent(CreateEvent("e:other", "other"));

        var inPage = LogPagedMatchNavigator.Resolve([row], "other", -1, forward: true, state);
        inPage.RowIndex.Should().Be(0);

        var crossPage = LogPagedMatchNavigator.Resolve([row], "target", 0, forward: true, state);
        crossPage.TargetPage.Should().Be(2);
    }

    [Fact]
    public void ShiftF3_crosses_to_previous_numbered_page()
    {
        var state = LogPaginationState.Initial
            .ApplyExactCount(95, isScanComplete: true)
            .StartNavigation(2)
            .ApplyExactCount(95, isScanComplete: true);
        var row = LogTableRow.FromEvent(CreateEvent("e:other", "other"));

        var result = LogPagedMatchNavigator.Resolve([row], "target", 0, forward: false, state);

        result.TargetPage.Should().Be(1);
    }

    private static LogEvent CreateEvent(string eventId, string message)
        => new(
            LogEvent.CurrentSchemaVersion,
            eventId,
            DateTimeOffset.UtcNow,
            LogSeverity.Info,
            LogComponentRegistry.Engine,
            LogChannelRegistry.Diagnostics,
            EventName: "TestEvent",
            message,
            Exception: null,
            OperationId: null,
            TraceId: null,
            SpanId: null,
            UserActionId: null,
            ProcessId: null,
            ThreadId: null,
            LogEvent.EmptyProperties);
}
