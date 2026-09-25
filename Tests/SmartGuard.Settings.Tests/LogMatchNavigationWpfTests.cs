using System.Text.Json;
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

    [Fact]
    public void Keyword_search_does_not_match_outcome_stored_only_in_source_event_properties()
    {
        var state = LogPaginationState.Initial.ApplyExactCount(1, isScanComplete: true);
        var row = LogTableRow.FromEvent(CreateEvent(
            "e:outcome",
            "电源事件已记录",
            LogEventOutcome.CreateProperties(LogEventOutcome.Handled)));

        var rawOutcome = LogPagedMatchNavigator.Resolve([row], "handled", -1, forward: true, state);
        rawOutcome.RowIndex.Should().BeNull();
        rawOutcome.TargetPage.Should().BeNull();

        var localizedOutcome = LogPagedMatchNavigator.Resolve([row], "已处理", -1, forward: true, state);
        localizedOutcome.RowIndex.Should().BeNull();
        localizedOutcome.TargetPage.Should().BeNull();
    }

    private static LogEvent CreateEvent(
        string eventId,
        string message,
        IReadOnlyDictionary<string, JsonElement>? properties = null)
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
            properties ?? LogEvent.EmptyProperties);
}
