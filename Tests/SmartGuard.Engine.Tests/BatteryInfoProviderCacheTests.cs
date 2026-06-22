using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class BatteryInfoProviderCacheTests
{
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
