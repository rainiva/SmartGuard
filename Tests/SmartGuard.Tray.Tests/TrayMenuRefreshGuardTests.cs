using FluentAssertions;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayMenuRefreshGuardTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldDefer_when_context_menu_is_open(bool menuOpen, bool expected)
    {
        TrayMenuRefreshGuard.ShouldDefer(menuOpen).Should().Be(expected);
    }
}
