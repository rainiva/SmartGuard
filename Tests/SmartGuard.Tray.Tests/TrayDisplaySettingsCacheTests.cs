using FluentAssertions;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayDisplaySettingsCacheTests
{
    [Fact]
    public void NotifyOnPlanChange_reuses_cached_value_within_ttl()
    {
        TrayDisplaySettingsCache.ResetForTests();
        TrayDisplaySettingsCache.CacheDuration = TimeSpan.FromSeconds(5);

        var loadCount = 0;
        var cache = new TrayDisplaySettingsCache(() =>
        {
            loadCount++;
            return true;
        });

        cache.NotifyOnPlanChange.Should().BeTrue();
        cache.NotifyOnPlanChange.Should().BeTrue();
        cache.NotifyOnPlanChange.Should().BeTrue();

        loadCount.Should().Be(1);
    }

    [Fact]
    public void NotifyOnPlanChange_reloads_after_cache_expires()
    {
        TrayDisplaySettingsCache.ResetForTests();
        TrayDisplaySettingsCache.CacheDuration = TimeSpan.FromMilliseconds(20);

        var loadCount = 0;
        var notify = true;
        var cache = new TrayDisplaySettingsCache(() =>
        {
            loadCount++;
            return notify;
        });

        cache.NotifyOnPlanChange.Should().BeTrue();
        Thread.Sleep(30);
        notify = false;
        cache.NotifyOnPlanChange.Should().BeFalse();

        loadCount.Should().Be(2);
    }
}
