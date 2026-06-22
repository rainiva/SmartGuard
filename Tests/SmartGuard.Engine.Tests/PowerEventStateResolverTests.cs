using Microsoft.Win32;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class PowerEventStateResolverTests
{
    [Fact]
    public void Resolve_uses_fresh_battery_read_for_status_change()
    {
        PowerEventStateResolver.Resolve(
                PowerModes.StatusChange,
                readBatteryInfo: () => (90, false))
            .Should().BeFalse();
    }

    [Fact]
    public void Resolve_uses_interpreted_value_for_resume_without_reading_battery()
    {
        var readCount = 0;
        PowerEventStateResolver.Resolve(
                PowerModes.Resume,
                readBatteryInfo: () =>
                {
                    readCount++;
                    return (90, false);
                })
            .Should().BeTrue();
        readCount.Should().Be(0);
    }

    [Fact]
    public void Resolve_uses_interpreted_value_for_suspend_without_reading_battery()
    {
        var readCount = 0;
        PowerEventStateResolver.Resolve(
                PowerModes.Suspend,
                readBatteryInfo: () =>
                {
                    readCount++;
                    return (90, true);
                })
            .Should().BeFalse();
        readCount.Should().Be(0);
    }
}
