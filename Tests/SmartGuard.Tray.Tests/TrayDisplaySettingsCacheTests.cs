using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayDisplaySettingsCacheTests
{
    [Fact]
    public void NotifyOnPlanChange_reloads_within_ttl_when_preferences_loader_returns_new_values_after_invalidate()
    {
        TrayDisplaySettingsCache.ResetForTests();
        TrayDisplaySettingsCache.CacheDuration = TimeSpan.FromSeconds(5);

        var notifyPlan = true;
        var loadCount = 0;
        var cache = new TrayDisplaySettingsCache(() =>
        {
            loadCount++;
            return new TrayNotificationPreferences(notifyPlan, true);
        });

        cache.NotifyOnPlanChange.Should().BeTrue();
        loadCount.Should().Be(1);

        notifyPlan = false;
        cache.Invalidate();
        cache.NotifyOnPlanChange.Should().BeFalse();
        loadCount.Should().Be(2);
    }

    [Fact]
    public void Repository_backed_cache_reloads_within_ttl_after_external_config_rewrite()
    {
        TrayDisplaySettingsCache.ResetForTests();
        TrayDisplaySettingsCache.CacheDuration = TimeSpan.FromSeconds(5);

        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTrayCache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var configPath = Path.Combine(dir, "SmartGuard.config.json");

        try
        {
            var repository = new GuardConfigRepository(configPath);
            var config = GuardConfig.CreateDefault(dir);
            config.NotifyOnPlanChange = true;
            repository.Save(config);

            var cache = new TrayDisplaySettingsCache(repository, dir);
            cache.NotifyOnPlanChange.Should().BeTrue();

            config = repository.LoadOrDefault(dir);
            config.NotifyOnPlanChange = false;
            repository.Save(config);
            Thread.Sleep(200);

            cache.NotifyOnPlanChange.Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void NotifyOnPlanChange_reuses_cached_value_within_ttl()
    {
        TrayDisplaySettingsCache.ResetForTests();
        TrayDisplaySettingsCache.CacheDuration = TimeSpan.FromSeconds(5);

        var loadCount = 0;
        var cache = new TrayDisplaySettingsCache(() =>
        {
            loadCount++;
            return new TrayNotificationPreferences(true, true);
        });

        cache.NotifyOnPlanChange.Should().BeTrue();
        cache.NotifyOnPlanChange.Should().BeTrue();
        cache.NotifyOnPlanChange.Should().BeTrue();

        loadCount.Should().Be(1);
    }

    [Fact]
    public void NotifyOnPlanChange_uses_seeded_value_without_immediate_reload()
    {
        TrayDisplaySettingsCache.ResetForTests();
        TrayDisplaySettingsCache.CacheDuration = TimeSpan.FromSeconds(5);

        var loadCount = 0;
        var cache = new TrayDisplaySettingsCache(
            new TrayNotificationPreferences(true, true),
            () =>
            {
                loadCount++;
                return new TrayNotificationPreferences(false, false);
            });

        cache.NotifyOnPlanChange.Should().BeTrue();
        cache.NotifyOnPlanChange.Should().BeTrue();
        loadCount.Should().Be(0);
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
            return new TrayNotificationPreferences(notify, notify);
        });

        cache.NotifyOnPlanChange.Should().BeTrue();
        Thread.Sleep(30);
        notify = false;
        cache.NotifyOnPlanChange.Should().BeFalse();

        loadCount.Should().Be(2);
    }

    [Fact]
    public void NotifyOnExternalChange_reloads_after_cache_expires()
    {
        TrayDisplaySettingsCache.ResetForTests();
        TrayDisplaySettingsCache.CacheDuration = TimeSpan.FromMilliseconds(20);

        var loadCount = 0;
        var notifyExternal = true;
        var cache = new TrayDisplaySettingsCache(() =>
        {
            loadCount++;
            return new TrayNotificationPreferences(true, notifyExternal);
        });

        cache.NotifyOnExternalChange.Should().BeTrue();
        Thread.Sleep(30);
        notifyExternal = false;
        cache.NotifyOnExternalChange.Should().BeFalse();

        loadCount.Should().Be(2);
    }
}
