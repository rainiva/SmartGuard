using FluentAssertions;
using SmartGuard.Contracts;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayNotificationPreferencesTests
{
    [Theory]
    [InlineData(NotificationKinds.PlanSwitch, true, true, true)]
    [InlineData(NotificationKinds.PlanSwitch, false, true, false)]
    [InlineData(NotificationKinds.ExternalChange, true, false, false)]
    [InlineData(NotificationKinds.ExternalChange, false, false, false)]
    [InlineData(null, true, false, true)]
    [InlineData("", true, false, true)]
    [InlineData("unknown", false, true, false)]
    public void ShouldNotify_respects_kind_specific_switches(
        string? kind,
        bool notifyOnPlanChange,
        bool notifyOnExternalChange,
        bool expected)
    {
        TrayNotificationPreferences.ShouldNotify(kind, notifyOnPlanChange, notifyOnExternalChange)
            .Should().Be(expected);
    }
}
