using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class BatteryInfoProviderCacheTests
{
    [Fact]
    public void ResolveFromSources_keeps_native_status_when_optional_wmi_probe_throws()
    {
        var wmiRead = false;
        var result = BatteryInfoProvider.ResolveFromSources(
            readNativeStatus: () => (AcLineStatus: (byte)0, BatteryLifePercent: (byte)89, BatteryFlag: (byte)0),
            readWmiStatus: () =>
            {
                wmiRead = true;
                throw new InvalidOperationException("WMI unavailable");
            });

        result.Percent.Should().Be(89);
        result.IsOnAc.Should().BeFalse();
        wmiRead.Should().BeFalse("complete native status must not depend on optional WMI");
    }

    [Fact]
    public void GetBatteryInfo_forceRefresh_bypasses_cached_snapshot()
    {
        try
        {
            BatteryInfoProvider.SetCacheForTests(90, isOnAc: true);
            BatteryInfoProvider.GetBatteryInfo().IsOnAc.Should().BeTrue();

            BatteryInfoProvider.ReadCoreOverrideForTests = () => (88, false);

            BatteryInfoProvider.GetBatteryInfo(forceRefresh: true).IsOnAc.Should().BeFalse();
            BatteryInfoProvider.GetBatteryInfo().IsOnAc.Should().BeFalse();
        }
        finally
        {
            BatteryInfoProvider.ReadCoreOverrideForTests = null;
            BatteryInfoProvider.InvalidateCache();
        }
    }
}
