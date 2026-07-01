using FluentAssertions;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayPauseBehaviorTests
{
  [Theory]
  [InlineData(false, "暂停守护")]
  [InlineData(true, "恢复守护")]
  [InlineData(null, "暂停守护")]
  public void MenuText_reflects_status_paused_not_config(bool? statusPaused, string expected)
  {
    TrayPauseState.MenuText(statusPaused).Should().Be(expected);
  }

  [Fact]
  public void ToggleTarget_when_status_not_paused_requests_pause_even_if_config_would_disagree()
  {
    TrayPauseState.ToggleTarget(statusPaused: false).Should().BeTrue();
  }

  [Fact]
  public void ToggleTarget_when_status_paused_requests_resume()
  {
    TrayPauseState.ToggleTarget(statusPaused: true).Should().BeFalse();
  }

  [Fact]
  public void ToggleTarget_when_status_missing_defaults_to_request_pause()
  {
    TrayPauseState.ToggleTarget(statusPaused: null).Should().BeTrue();
  }
}
