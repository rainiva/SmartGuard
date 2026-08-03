using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogMatchNavigatorTests
{
  private static LogMatch Match(int ordinal, string eventId)
    => new(ordinal, eventId, LogSourceIdentity.FromContentPrefix(DateTimeOffset.UtcNow, eventId), 0);

  [Fact]
  public void NavigateNext_cycles_forward_within_current_page()
  {
    var navigator = new LogMatchNavigator();
    var matches = new[] { Match(1, "e-1"), Match(2, "e-2"), Match(3, "e-3") };
    navigator.SetMatches(queryGeneration: 1, matches, scanComplete: true, visibleEventIds: ["e-1", "e-2", "e-3"]);

    navigator.NavigateNext(reverse: false).TargetEventId.Should().Be("e-1");
    navigator.NavigateNext(reverse: false).TargetEventId.Should().Be("e-2");
    navigator.NavigateNext(reverse: false).TargetEventId.Should().Be("e-3");
    navigator.NavigateNext(reverse: false).TargetEventId.Should().Be("e-1");
  }

  [Fact]
  public void NavigateNext_cycles_backward_with_shift_direction()
  {
    var navigator = new LogMatchNavigator();
    var matches = new[] { Match(1, "e-1"), Match(2, "e-2") };
    navigator.SetMatches(queryGeneration: 1, matches, scanComplete: true, visibleEventIds: ["e-1", "e-2"]);

    navigator.NavigateNext(reverse: true).TargetEventId.Should().Be("e-2");
    navigator.NavigateNext(reverse: true).TargetEventId.Should().Be("e-1");
  }

  [Fact]
  public void NavigateNext_requests_page_load_when_match_not_visible()
  {
    var navigator = new LogMatchNavigator();
    var matches = new[] { Match(1, "e-visible"), Match(2, "e-offpage") };
    navigator.SetMatches(queryGeneration: 1, matches, scanComplete: true, visibleEventIds: ["e-visible"]);

    navigator.NavigateNext(reverse: false).TargetEventId.Should().Be("e-visible");
    var offPage = navigator.NavigateNext(reverse: false);

    offPage.RequiresPageLoad.Should().BeTrue();
    offPage.TargetEventId.Should().Be("e-offpage");
    offPage.TargetCursor.Should().NotBeNull();
  }

  [Fact]
  public void Query_generation_change_invalidates_stale_match_ids()
  {
    var navigator = new LogMatchNavigator();
    navigator.SetMatches(
      queryGeneration: 1,
      [Match(1, "old-event")],
      scanComplete: true,
      visibleEventIds: ["old-event"]);

    navigator.NavigateNext(reverse: false).TargetEventId.Should().Be("old-event");

    navigator.NotifyQueryGenerationChanged(2);
    navigator.SetMatches(queryGeneration: 2, [Match(1, "new-event")], scanComplete: true, visibleEventIds: ["new-event"]);

    navigator.CurrentMatchIndex.Should().Be(0);
    navigator.NavigateNext(reverse: false).TargetEventId.Should().Be("new-event");
  }

  [Fact]
  public void BuildStatusText_shows_progressive_total_while_scanning()
  {
    LogMatchNavigator.BuildStatusText(currentIndex: 2, discoveredCount: 5, scanComplete: false)
      .Should().Be("第 2 个 / 已发现 5+");

    LogMatchNavigator.BuildStatusText(currentIndex: 0, discoveredCount: 0, scanComplete: false)
      .Should().Be("零匹配");
  }

  [Fact]
  public void BuildStatusText_shows_exact_total_when_scan_complete()
  {
    LogMatchNavigator.BuildStatusText(currentIndex: 1, discoveredCount: 3, scanComplete: true)
      .Should().Be("第 1 个 / 3");
  }
}
