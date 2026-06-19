using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;

namespace SmartGuard.Engine.Tests;

public class PolicyEngineTests
{
    private static GuardConfig TestConfig() => new()
    {
        ActivePlanGuid = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
        BalancedPlanGuid = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
        PowerSaverPlanGuid = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a"),
        BalancedThresholdSec = 300,
        PowerSaverThresholdSec = 900,
        LowBatteryPercent = 30,
        Paused = false
    };

    [Fact]
    public void Returns_null_when_paused()
    {
        var cfg = TestConfig();
        cfg.Paused = true;
        PolicyEngine.GetExpectedPlanGuid(0, true, 80, cfg).Should().BeNull();
    }

    [Fact]
    public void Returns_active_plan_when_manual_high_performance_boost_is_active_even_if_idle()
    {
        var cfg = TestConfig();
        cfg.ManualHighPerformanceUntil = DateTime.Now.AddMinutes(30);
        PolicyEngine.GetExpectedPlanGuid(900, true, 80, cfg)
            .Should().Be(cfg.ActivePlanGuid);
    }

    [Fact]
    public void Returns_power_saver_when_idle_ge_15_minutes()
    {
        var cfg = TestConfig();
        PolicyEngine.GetExpectedPlanGuid(900, true, 80, cfg)
            .Should().Be(cfg.PowerSaverPlanGuid);
    }

    [Fact]
    public void Returns_balanced_when_idle_between_5_and_15_minutes()
    {
        var cfg = TestConfig();
        PolicyEngine.GetExpectedPlanGuid(400, true, 80, cfg)
            .Should().Be(cfg.BalancedPlanGuid);
    }

    [Fact]
    public void Returns_active_plan_when_active_on_AC()
    {
        var cfg = TestConfig();
        PolicyEngine.GetExpectedPlanGuid(60, true, 10, cfg)
            .Should().Be(cfg.ActivePlanGuid);
    }

    [Fact]
    public void Returns_active_plan_on_battery_when_charge_ge_30_percent()
    {
        var cfg = TestConfig();
        PolicyEngine.GetExpectedPlanGuid(60, false, 50, cfg)
            .Should().Be(cfg.ActivePlanGuid);
    }

    [Fact]
    public void Returns_balanced_on_battery_when_charge_lt_30_percent_and_active()
    {
        var cfg = TestConfig();
        PolicyEngine.GetExpectedPlanGuid(60, false, 25, cfg)
            .Should().Be(cfg.BalancedPlanGuid);
    }

    [Fact]
    public void Should_apply_switch_when_guids_differ()
    {
        PolicyEngine.ShouldApplyPowerPlanSwitch(
            Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
            Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e")).Should().BeTrue();
    }

    [Fact]
    public void Should_not_apply_switch_when_guids_match()
    {
        var g = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        PolicyEngine.ShouldApplyPowerPlanSwitch(g, g).Should().BeFalse();
    }

    [Fact]
    public void GetPlanDisplayName_uses_system_catalog_when_preferred_name_is_null()
    {
        var cfg = TestConfig();
        var catalog = new Dictionary<Guid, string>
        {
            [cfg.BalancedPlanGuid] = "平衡(系统名)",
        };

        PolicyEngine.GetPlanDisplayName(cfg.BalancedPlanGuid, cfg, catalog)
            .Should().Be("平衡(系统名)");
    }

    [Fact]
    public void GetPlanDisplayName_uses_config_alias_when_no_system_name_available()
    {
        var cfg = TestConfig();

        PolicyEngine.GetPlanDisplayName(cfg.BalancedPlanGuid, cfg, new Dictionary<Guid, string>())
            .Should().Be("平衡");
    }

    [Fact]
    public void GetPlanDisplayName_uses_system_catalog_for_unknown_oem_plan()
    {
        var cfg = TestConfig();
        var oem = Guid.Parse("b8a2c9f4-7d3e-4a1b-9c2f-5e8d6a3b1c4f");
        var catalog = new Dictionary<Guid, string> { [oem] = "Honor Performance" };

        PolicyEngine.GetPlanDisplayName(oem, cfg, catalog)
            .Should().Be("Honor Performance");
    }

    [Fact]
    public void GetPlanDisplayName_falls_back_to_guid_when_not_in_catalog()
    {
        var cfg = TestConfig();
        var unknown = Guid.Parse("b8a2c9f4-7d3e-441b-9c2f-5e8d6a3b1c4f");

        PolicyEngine.GetPlanDisplayName(unknown, cfg, new Dictionary<Guid, string>())
            .Should().Be(unknown.ToString());
    }

    [Fact]
    public void GetPlanDisplayName_uses_preferred_name_when_catalog_misses()
    {
        var cfg = TestConfig();
        var oem = Guid.Parse("b8a2c9f4-7d3e-4a1b-9c2f-5e8d6a3b1c4f");

        PolicyEngine.GetPlanDisplayName(oem, cfg, new Dictionary<Guid, string>(), "Honor Performance")
            .Should().Be("Honor Performance");
    }

    [Fact]
    public void GetPlanDisplayName_prefers_preferred_name_over_config_alias_for_oem_plan()
    {
        var cfg = TestConfig();
        // Honor OEM uses the same GUID as Windows High Performance plan
        var honorGuid = cfg.ActivePlanGuid;

        PolicyEngine.GetPlanDisplayName(honorGuid, cfg, new Dictionary<Guid, string>(), "Honor Performance")
            .Should().Be("Honor Performance");
    }
}
