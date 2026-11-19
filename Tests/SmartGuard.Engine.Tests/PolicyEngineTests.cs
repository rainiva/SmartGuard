using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class PolicyEngineTests
{
    private static readonly DateTime FixedLocalNow = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Local);

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
    public void GetStatusLabel_is_active_below_balanced_threshold()
    {
        var cfg = TestConfig();
        PolicyEngine.GetStatusLabel(92, cfg).Should().Be("活跃");
        PolicyEngine.GetStatusLabel(299, cfg).Should().Be("活跃");
        PolicyEngine.GetStatusLabel(300, cfg).Should().Be("空闲");
        PolicyEngine.GetStatusLabel(900, cfg).Should().Be("深度空闲");
    }

    [Fact]
    public void Evaluate_returns_none_when_paused()
    {
        var cfg = TestConfig();
        cfg.Paused = true;
        var decision = PolicyEngine.Evaluate(0, true, 80, ForegroundObservation.Unknown, cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().BeNull();
        decision.Source.Should().Be(GuardDecisionSource.None);
        decision.Reason.Should().Be(GuardDecisionReason.Paused);
    }

    [Fact]
    public void Evaluate_returns_manual_plan_when_manual_high_performance_boost_is_active_even_if_idle()
    {
        var cfg = TestConfig();
        cfg.ManualHighPerformanceUntil = FixedLocalNow.AddMinutes(30);
        var decision = PolicyEngine.Evaluate(900, true, 80, ForegroundObservation.Unknown, cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().Be(cfg.ActivePlanGuid);
        decision.Source.Should().Be(GuardDecisionSource.Manual);
        decision.Reason.Should().Be(GuardDecisionReason.ManualActive);
    }

    [Fact]
    public void Evaluate_manual_override_ignores_pause_and_foreground_restriction()
    {
        var cfg = TestConfig();
        cfg.ManualHighPerformanceUntil = FixedLocalNow.AddMinutes(30);
        cfg.Paused = true;
        cfg.ForegroundRestrictionEnabled = true;
        cfg.ForegroundProgramPaths.Clear();

        var decision = PolicyEngine.Evaluate(900, false, 10, ForegroundObservation.Unknown, cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().Be(cfg.ActivePlanGuid);
        decision.Source.Should().Be(GuardDecisionSource.Manual);
        decision.Reason.Should().Be(GuardDecisionReason.ManualActive);
    }

    [Fact]
    public void Evaluate_manual_expiry_boundary_is_decided_by_injected_local_now()
    {
        var cfg = TestConfig();
        cfg.ManualHighPerformanceUntil = FixedLocalNow.AddMinutes(30);

        // 期限前一秒仍人工接管（深度空闲也顺延到手动计划）。
        var before = PolicyEngine.Evaluate(
            900, false, 80, ForegroundObservation.Unknown, cfg, FixedLocalNow.AddMinutes(30).AddSeconds(-1));
        before.Source.Should().Be(GuardDecisionSource.Manual);
        before.TargetPlanGuid.Should().Be(cfg.ActivePlanGuid);

        // 到期时刻（until > now 为 false）回到既有策略链，不依赖 DateTime.Now。
        var expired = PolicyEngine.Evaluate(
            0, true, 80, ForegroundObservation.Unknown, cfg, FixedLocalNow.AddMinutes(30));
        expired.Source.Should().Be(GuardDecisionSource.Automatic);
        expired.Reason.Should().Be(GuardDecisionReason.PolicySelected);
        expired.TargetPlanGuid.Should().Be(cfg.ActivePlanGuid);
    }

    [Theory]
    [InlineData(0, true, 80, "active")]
    [InlineData(299, true, 80, "active")]
    [InlineData(300, true, 80, "balanced")]
    [InlineData(899, false, 80, "balanced")]
    [InlineData(900, true, 80, "power-saver")]
    [InlineData(901, false, 100, "power-saver")]
    [InlineData(0, true, 10, "active")]
    [InlineData(0, false, 30, "active")]
    [InlineData(0, false, 29, "balanced")]
    [InlineData(299, false, 0, "balanced")]
    public void Evaluate_with_restriction_disabled_matches_pre_migration_chain(
        int idleSeconds, bool isOnAc, int batteryPercent, string expectedTier)
    {
        var cfg = TestConfig();
        var expected = expectedTier switch
        {
            "active" => cfg.ActivePlanGuid,
            "balanced" => cfg.BalancedPlanGuid,
            "power-saver" => cfg.PowerSaverPlanGuid,
            _ => throw new InvalidOperationException("unknown tier"),
        };

        // 限制关闭：前台未识别、名单为空都不得影响既有策略链（迁移前后等价）。
        var decision = PolicyEngine.Evaluate(idleSeconds, isOnAc, batteryPercent, ForegroundObservation.Unknown, cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().Be(expected);
        decision.Source.Should().Be(GuardDecisionSource.Automatic);
        decision.Reason.Should().Be(GuardDecisionReason.PolicySelected);
    }

    [Theory]
    [InlineData(0, true, 80, "active")]
    [InlineData(300, false, 80, "balanced")]
    [InlineData(900, false, 80, "power-saver")]
    [InlineData(60, false, 25, "balanced")]
    public void Evaluate_with_restriction_enabled_and_foreground_matched_matches_same_chain(
        int idleSeconds, bool isOnAc, int batteryPercent, string expectedTier)
    {
        var cfg = TestConfig();
        cfg.ForegroundRestrictionEnabled = true;
        cfg.ForegroundProgramPaths.Add(@"C:\Apps\Alpha.exe");
        var expected = expectedTier switch
        {
            "active" => cfg.ActivePlanGuid,
            "balanced" => cfg.BalancedPlanGuid,
            "power-saver" => cfg.PowerSaverPlanGuid,
            _ => throw new InvalidOperationException("unknown tier"),
        };

        var decision = PolicyEngine.Evaluate(
            idleSeconds, isOnAc, batteryPercent,
            ForegroundObservation.Identified(@"C:\Apps\Alpha.exe"), cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().Be(expected);
        decision.Source.Should().Be(GuardDecisionSource.Automatic);
        decision.Reason.Should().Be(GuardDecisionReason.ForegroundMatched);
    }

    [Fact]
    public void Evaluate_with_restriction_enabled_and_empty_list_does_not_intervene()
    {
        var cfg = TestConfig();
        cfg.ForegroundRestrictionEnabled = true;
        cfg.ForegroundProgramPaths.Clear();

        // 名单为空先于身份判定；深度空闲也不得切换。
        var decision = PolicyEngine.Evaluate(900, true, 80, ForegroundObservation.Identified(@"C:\Apps\Alpha.exe"), cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().BeNull();
        decision.Source.Should().Be(GuardDecisionSource.None);
        decision.Reason.Should().Be(GuardDecisionReason.ForegroundListEmpty);
    }

    [Fact]
    public void Evaluate_with_restriction_enabled_and_unknown_identity_does_not_intervene()
    {
        var cfg = TestConfig();
        cfg.ForegroundRestrictionEnabled = true;
        cfg.ForegroundProgramPaths.Add(@"C:\Apps\Alpha.exe");

        var decision = PolicyEngine.Evaluate(0, true, 80, ForegroundObservation.Unknown, cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().BeNull();
        decision.Source.Should().Be(GuardDecisionSource.None);
        decision.Reason.Should().Be(GuardDecisionReason.ForegroundIdentityUnknown);
    }

    [Fact]
    public void Evaluate_with_restriction_enabled_and_unmatched_executable_waits_for_listed_program()
    {
        var cfg = TestConfig();
        cfg.ForegroundRestrictionEnabled = true;
        cfg.ForegroundProgramPaths.Add(@"C:\Apps\Alpha.exe");

        var decision = PolicyEngine.Evaluate(0, true, 80, ForegroundObservation.Identified(@"C:\Apps\Beta.exe"), cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().BeNull();
        decision.Source.Should().Be(GuardDecisionSource.None);
        decision.Reason.Should().Be(GuardDecisionReason.WaitingForListedProgram);
    }

    [Fact]
    public void Evaluate_matches_listed_program_case_insensitively_after_normalization()
    {
        var cfg = TestConfig();
        cfg.ForegroundRestrictionEnabled = true;
        cfg.ForegroundProgramPaths.Add(@"c:\apps\ALPHA.exe");

        var decision = PolicyEngine.Evaluate(0, false, 80, ForegroundObservation.Identified(@"C:\Apps\alpha.exe"), cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().Be(cfg.ActivePlanGuid);
        decision.Reason.Should().Be(GuardDecisionReason.ForegroundMatched);
    }

    [Fact]
    public void Evaluate_unnormalizable_foreground_path_is_treated_as_not_matching()
    {
        var cfg = TestConfig();
        cfg.ForegroundRestrictionEnabled = true;
        cfg.ForegroundProgramPaths.Add(@"C:\Apps\Alpha.exe");

        // 识别成功但路径非绝对 .exe 规范形态：按未命中处理，不旁路。
        var decision = PolicyEngine.Evaluate(0, true, 80, ForegroundObservation.Identified("alpha.exe"), cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().BeNull();
        decision.Reason.Should().Be(GuardDecisionReason.WaitingForListedProgram);
    }

    [Fact]
    public void Evaluate_with_invalid_stored_entry_fails_closed_before_identity_checks()
    {
        var cfg = TestConfig();
        cfg.ForegroundRestrictionEnabled = true;
        cfg.ForegroundProgramPaths.Add(@"C:\Apps\Alpha.exe");
        cfg.ForegroundProgramPaths.Add(@"relative\not-absolute.exe");

        // 名单存在非法条目：配置不可信 → 不干预，即使前台身份也不可识别（非法优先于未知）。
        var decision = PolicyEngine.Evaluate(0, true, 80, ForegroundObservation.Unknown, cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().BeNull();
        decision.Source.Should().Be(GuardDecisionSource.None);
        decision.Reason.Should().Be(GuardDecisionReason.ForegroundProgramPathsInvalid);
    }

    [Fact]
    public void Evaluate_with_restriction_disabled_ignores_foreground_and_list()
    {
        var cfg = TestConfig();
        cfg.ForegroundRestrictionEnabled = false;
        cfg.ForegroundProgramPaths.Add(@"relative\invalid-entry.exe");

        // 限制关闭：既不校验名单也不要求前台识别。
        var decision = PolicyEngine.Evaluate(0, true, 80, ForegroundObservation.Identified(@"C:\Elsewhere\Beta.exe"), cfg, FixedLocalNow);
        decision.TargetPlanGuid.Should().Be(cfg.ActivePlanGuid);
        decision.Reason.Should().Be(GuardDecisionReason.PolicySelected);
    }

    [Fact]
    public void IsExternalPlanChange_is_false_when_tray_owned_switch_lands_on_expected_plan()
    {
        var cfg = TestConfig();
        var previous = cfg.BalancedPlanGuid;
        var current = cfg.ActivePlanGuid;
        var expected = cfg.ActivePlanGuid; // manual high-performance boost

        PolicyEngine.IsExternalPlanChange(previous, current, scriptJustSwitched: false, expected)
            .Should().BeFalse();
    }

    [Fact]
    public void IsExternalPlanChange_is_true_when_plan_changes_away_from_expected()
    {
        var cfg = TestConfig();
        var previous = cfg.ActivePlanGuid;
        var current = cfg.BalancedPlanGuid;
        var expected = cfg.ActivePlanGuid;

        PolicyEngine.IsExternalPlanChange(previous, current, scriptJustSwitched: false, expected)
            .Should().BeTrue();
    }

    [Fact]
    public void IsExternalPlanChange_is_false_when_script_just_switched()
    {
        var previous = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var current = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

        PolicyEngine.IsExternalPlanChange(previous, current, scriptJustSwitched: true, expected: current)
            .Should().BeFalse();
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

    // ---- Runner 消费统一决策的行为（合成事实，不触真实采集） ----

    [Fact]
    public async Task Runner_without_foreground_collection_does_not_switch_under_restriction()
    {
        using var harness = GuardDecisionHarness.Create(
            PowerPlanCatalogProvider.HighPerformancePlanGuid, apiIdle: 900);
        harness.EnableForegroundRestriction(@"C:\Apps\Alpha.exe");
        // readForeground 尚未接入真实采集：默认 Unknown，限制开启时按识别不可用处理，不旁路回退。

        await harness.RunIterationAsync();

        harness.AppliedPlans.Should().BeEmpty();
        harness.ExternalChangeEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Foreground_hold_absorbs_baseline_and_never_reports_backlog_external_change()
    {
        using var harness = GuardDecisionHarness.Create(
            PowerPlanCatalogProvider.HighPerformancePlanGuid, apiIdle: 10);
        harness.EnableForegroundRestriction(@"C:\Apps\Alpha.exe");
        harness.SetForeground(ForegroundObservation.Unknown);
        harness.SetOsPlan(PowerPlanCatalogProvider.BalancedPlanGuid); // 未命中期间的外部变更

        await harness.RunIterationAsync();
        harness.AppliedPlans.Should().BeEmpty();
        harness.ExternalChangeEvents.Should().BeEmpty();

        // 重新命中：吸收为基线，不得把未守护期间的变化误报为刚发生的外部变更。
        harness.SetForeground(ForegroundObservation.Identified(@"C:\Apps\Alpha.exe"));
        await harness.RunIterationAsync();
        harness.ExternalChangeEvents.Should().BeEmpty();
        harness.AppliedPlans.Should().Equal(PowerPlanCatalogProvider.HighPerformancePlanGuid);
        harness.LastKnownPlan.Should().Be(PowerPlanCatalogProvider.HighPerformancePlanGuid);

        // 命中状态下守护已恢复：稳定一轮后，外部变更重新被报告并纠偏。
        await harness.RunIterationAsync();
        harness.SetOsPlan(PowerPlanCatalogProvider.PowerSaverPlanGuid);
        await harness.RunIterationAsync();
        harness.ExternalChangeEvents.Should().ContainSingle();
        harness.AppliedPlans.Should().Equal(
            PowerPlanCatalogProvider.HighPerformancePlanGuid,
            PowerPlanCatalogProvider.HighPerformancePlanGuid);
    }

    [Fact]
    public async Task Config_unavailable_degrades_to_observation_without_actions_or_heartbeat()
    {
        using var harness = GuardDecisionHarness.Create(
            PowerPlanCatalogProvider.HighPerformancePlanGuid, apiIdle: 900);
        harness.SetOsPlan(PowerPlanCatalogProvider.BalancedPlanGuid);
        harness.SetConfigUnavailable();

        await harness.RunIterationAsync();
        harness.AppliedPlans.Should().BeEmpty();
        harness.ExternalChangeEvents.Should().BeEmpty();
        harness.HeartbeatEvents.Should().Be(0);
        harness.ReadPublishedIdle().Should().Be(900);

        // 恢复有效读取后重新决策：不补发坏配置期间的纠偏通知。
        harness.RestoreValidConfig();
        await harness.RunIterationAsync();
        harness.ExternalChangeEvents.Should().BeEmpty();
        harness.AppliedPlans.Should().Equal(PowerPlanCatalogProvider.PowerSaverPlanGuid);
    }

    [Fact]
    public async Task Config_unavailable_with_failed_plan_probe_still_publishes_observation()
    {
        using var harness = GuardDecisionHarness.Create(
            PowerPlanCatalogProvider.HighPerformancePlanGuid, apiIdle: 0);
        harness.SetConfigUnavailable();
        harness.FailPlanProbe("Access is denied.");

        await harness.RunIterationAsync();

        harness.StatusFileExists.Should().BeTrue();
        harness.ExternalChangeEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Manual_high_performance_deadline_follows_injected_local_now()
    {
        using var harness = GuardDecisionHarness.Create(
            PowerPlanCatalogProvider.BalancedPlanGuid, apiIdle: 900);
        harness.Config.ManualHighPerformanceUntil = harness.CurrentLocalNow.AddMinutes(30);

        await harness.RunIterationAsync();
        harness.AppliedPlans.Should().Equal(PowerPlanCatalogProvider.HighPerformancePlanGuid);

        harness.Advance(TimeSpan.FromMinutes(31));
        await harness.RunIterationAsync();
        harness.AppliedPlans.Should().Equal(
            PowerPlanCatalogProvider.HighPerformancePlanGuid,
            PowerPlanCatalogProvider.PowerSaverPlanGuid);
    }
}

internal sealed class GuardDecisionHarness : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly GuardianLoopIterationState _state = new();
    private readonly string _root;
    private readonly string _statusPath;
    private readonly StatusPublisher _publisher;
    private readonly HeartbeatSchedule _heartbeatSchedule = new();
    private readonly GuardianIterationRunner _runner;
    private GuardConfigReadResult _configRead;
    private Guid _activePlan;
    private uint _apiIdle;
    private readonly bool _onAc = true;
    private PowerPlanProbeResult? _forcedProbeFailure;
    private bool _failNextApply;
    private ForegroundObservation _foreground = ForegroundObservation.Unknown;

    private GuardDecisionHarness(Guid activePlan, uint apiIdle)
    {
        _root = Path.Combine(Path.GetTempPath(), "SmartGuardDecisionHarness_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _statusPath = Path.Combine(_root, "SmartGuard.status.json");
        Config = GuardConfig.CreateDefault(_root);
        Config.BrightnessRestoreMs = 0;
        _configRead = GuardConfigReadResult.FromConfig(Config);
        _activePlan = activePlan;
        _apiIdle = apiIdle;
        _publisher = new StatusPublisher(_statusPath, _time);
        _state.PlanCatalog = new Dictionary<Guid, string>
        {
            [PowerPlanCatalogProvider.HighPerformancePlanGuid] = PowerPlanCatalogProvider.HighPerformanceDisplayName,
            [PowerPlanCatalogProvider.BalancedPlanGuid] = PowerPlanCatalogProvider.BalancedDisplayName,
            [PowerPlanCatalogProvider.PowerSaverPlanGuid] = PowerPlanCatalogProvider.PowerSaverDisplayName,
        };
        _state.LastKnownGuid = activePlan;
        _runner = new GuardianIterationRunner(
            new IdleTracker(_time),
            new BrightnessService(),
            _publisher,
            () => _configRead,
            (config, level, message, eventName, properties, observedAt) =>
            {
                Events.Add(new(eventName, _time.GetUtcNow(), message));
                return GuardianLogWriteResult.AcceptedByPipeline;
            },
            _ => { },
            powerCfgBrightnessSupported: () => false,
            _heartbeatSchedule,
            readIdleSeconds: () => _apiIdle,
            readBatteryInfo: _ => (70, _onAc),
            readCurrentPlan: () => _forcedProbeFailure ?? new PowerPlanProbeResult(
                true,
                new PowerSchemeInfo(_activePlan, null),
                PowerPlanFailureKind.None,
                string.Empty),
            applyPlan: (target, cancellationToken) =>
            {
                if (_failNextApply)
                {
                    _failNextApply = false;
                    return Task.FromResult(new PowerPlanApplyResult(
                        false,
                        target,
                        _activePlan,
                        PowerPlanFailureKind.VerificationMismatch,
                        "test verification mismatch"));
                }

                _activePlan = target;
                AppliedPlans.Add(target);
                return Task.FromResult(new PowerPlanApplyResult(
                    true,
                    target,
                    target,
                    PowerPlanFailureKind.None,
                    string.Empty));
            },
            timeProvider: _time,
            readForeground: () => _foreground);
    }

    public GuardConfig Config { get; }
    public List<Guid> AppliedPlans { get; } = [];
    public List<HarnessEvent> Events { get; } = [];
    public IReadOnlyList<HarnessEvent> ExternalChangeEvents
        => Events.Where(x => x.EventName == EventNameRegistry.ExternalPlanChange).ToArray();
    public int HeartbeatEvents
        => Events.Count(x => x.EventName == EventNameRegistry.HeartbeatTick);
    public Guid LastKnownPlan => _activePlan;
    public DateTime CurrentLocalNow => _time.GetLocalNow().LocalDateTime;
    public bool StatusFileExists => File.Exists(_statusPath);

    public static GuardDecisionHarness Create(Guid activePlan, uint apiIdle) => new(activePlan, apiIdle);

    public void EnableForegroundRestriction(params string[] paths)
    {
        Config.ForegroundRestrictionEnabled = true;
        Config.ForegroundProgramPaths = paths.ToList();
    }

    public void SetForeground(ForegroundObservation observation) => _foreground = observation;

    public void SetOsPlan(Guid plan) => _activePlan = plan;

    public void SetConfigUnavailable() => _configRead = GuardConfigReadResult.Unavailable;

    public void RestoreValidConfig() => _configRead = GuardConfigReadResult.FromConfig(Config);

    public void FailPlanProbe(string diagnostic)
        => _forcedProbeFailure = new PowerPlanProbeResult(
            false,
            null,
            PowerPlanFailureKind.ApiFailed,
            diagnostic);

    public void Advance(TimeSpan value) => _time.Advance(value);

    public async Task RunIterationAsync()
        => await _runner.RunAsync(_state, GuardianIterationContext.Running, CancellationToken.None);

    public int ReadPublishedIdle()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(_statusPath));
        return document.RootElement.GetProperty("idleSeconds").GetInt32();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
