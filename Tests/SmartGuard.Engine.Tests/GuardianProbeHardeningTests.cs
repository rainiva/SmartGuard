using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

[Collection("LoggingFallbackTests")]
public sealed class GuardianProbeHardeningTests
{
  private static readonly Guid High = PowerPlanCatalogProvider.HighPerformancePlanGuid;
  private static readonly Guid Balanced = PowerPlanCatalogProvider.BalancedPlanGuid;

  [Fact]
  public async Task Cold_catalog_uses_the_cancelable_path_without_synchronous_enrichment()
  {
    var syncCalls = 0;
    var asyncCalls = 0;
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    PowerCfgProcessRunner.RunOverrideForTests = (_, _) =>
    {
      syncCalls++;
      return new(false, false, null, "", "unexpected synchronous work");
    };
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) =>
    {
      asyncCalls++;
      return Task.FromResult(new PowerCfgCommandResult(false, false, null, "", "unavailable"));
    };
    try
    {
      await WithRunner(async (runner, state, _) =>
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None));
      syncCalls.Should().Be(0, "display-name enrichment cannot hold the core round in the old 105-second path");
      asyncCalls.Should().Be(1, "the guardian does not need catalog retries to decide policy");
    }
    finally { ResetOverrides(); }
  }

  [Fact]
  public async Task Cancellation_during_catalog_stops_the_round_before_state_publication()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    PowerCfgProcessRunner.RunOverrideForTests = (_, _) => new(false, false, null, "", "unavailable");
    PowerCfgProcessRunner.RunAsyncOverrideForTests = async (_, _, token) =>
    {
      await Task.Delay(Timeout.InfiniteTimeSpan, token);
      return new(false, false, null, "", "unreachable");
    };
    try
    {
      await WithRunner(async (runner, state, path) =>
      {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          runner.RunAsync(state, GuardianIterationContext.Running, cancellation.Token));
        File.Exists(path).Should().BeFalse("a stopped round cannot later publish an apparently current state");
      });
    }
    finally { ResetOverrides(); }
  }

  private static void ResetOverrides()
  {
    PowerCfgProcessRunner.RunOverrideForTests = null;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = null;
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
  }

  [Fact]
  public async Task Catalog_failure_keeps_real_cleanup_uncertainty_in_the_logged_transition()
  {
    var observations = new List<WmiProbeObservation>();
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(
      new PowerCfgCommandResult(true, true, null, "", "")
      { FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false, ElapsedMilliseconds = 2100 });
    try
    {
      await WithRunner((runner, state, _) => runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None),
        observe: observations.Add);
      observations.Should().Contain(x => x.Operation == "plan-catalog" && !x.TerminationConfirmed
        && x.FailureKind == "cleanup-unconfirmed", "catalog degradation cannot invent a successful cleanup certificate");
    }
    finally { ResetOverrides(); }
  }

  [Fact]
  public async Task Diagnostic_callback_failure_does_not_prevent_probe_failure_state_publication()
  {
    var native = new FakePowrProfNative { GetActive = () => (5, Guid.Empty) };
    await PowerSchemeTestScope.WithNative(native, () => WithRunner(async (runner, state, path) =>
    {
      state.PlanCatalog = new Dictionary<Guid, string>();
      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      StatusJsonReader.TryRead(path)!.guardReason.Should().Be("probe-failed");
    }, useNativePlanReader: true, observe: _ => throw new IOException("diagnostic sink unavailable")));
  }

  [Fact]
  public async Task Current_plan_probe_uses_the_scheme_service()
  {
    var native = new FakePowrProfNative { GetActive = () => (0, High), ReadName = _ => (0, "高性能") };
    await PowerSchemeTestScope.WithNative(native, () => WithRunner(async (runner, state, path) =>
    {
      state.PlanCatalog = new Dictionary<Guid, string>();
      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      StatusJsonReader.TryRead(path)!.guardReason.Should().Be("paused");
    }, useNativePlanReader: true));
    native.Calls.Should().StartWith(["get-active", $"read-name {High:D}"]);
  }

  [Fact]
  public async Task Timed_out_call_blocks_new_calls_until_it_returns_then_probing_recovers()
  {
    using var release = new ManualResetEventSlim();
    var first = true;
    var native = new FakePowrProfNative
    {
      GetActive = () =>
      {
        if (first) { first = false; release.Wait(TimeSpan.FromSeconds(10)); }
        return (0, High);
      },
    };
    var observations = new List<WmiProbeObservation>();
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      try
      {
        await WithRunner(async (runner, state, _) =>
        {
          state.PlanCatalog = new Dictionary<Guid, string>();
          await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
          await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
          native.Calls.Count(call => call == "get-active").Should().Be(1, "an in-flight call admits no sibling");
          observations.Select(x => x.FailureKind).Should().Equal("timed-out", "call-pending");

          release.Set();
          await PowerSchemeTestScope.WaitUntilIdleAsync();
          await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
          observations.Should().ContainSingle(x => x.Operation == "plan-read" && x.Recovered);
        }, useNativePlanReader: true, observe: observations.Add);
      }
      finally { release.Set(); }
    }, deadline: TimeSpan.FromMilliseconds(100));
  }

  private static async Task WithRunner(Func<GuardianIterationRunner, GuardianLoopIterationState, string, Task> run,
    bool useNativePlanReader = false, Action<WmiProbeObservation>? observe = null,
    bool paused = true, int brightness = -1, Guid? observedPlan = null,
    Func<bool>? canMutate = null, Action<GuardConfig>? configure = null,
    Func<Guid, CancellationToken, Task<PowerPlanApplyResult>>? apply = null,
    Action<string>? loggedEvent = null, bool useNativeApply = false)
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardProbeRound_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var config = GuardConfig.CreateDefault(root);
      config.Paused = paused;
      configure?.Invoke(config);
      var path = Path.Combine(root, "status.json");
      var runner = new GuardianIterationRunner(new IdleTracker(), new BrightnessService(), new StatusPublisher(path),
        () => GuardConfigReadResult.FromConfig(config),
        (_, _, _, eventName, _, _) => { loggedEvent?.Invoke(eventName); return GuardianLogWriteResult.AcceptedByPipeline; },
        _ => { }, true, new HeartbeatSchedule(), readIdleSeconds: () => 0,
        readBatteryInfo: _ => (90, true),
        readCurrentPlan: useNativePlanReader ? null
          : () => new(true, new PowerSchemeInfo(observedPlan ?? config.ActivePlanGuid, null), PowerPlanFailureKind.None, ""),
        readBrightness: () => brightness, observeNativeProbe: observe, canMutatePolicy: canMutate,
        applyPlan: useNativeApply ? null : apply ?? ((_, _) => throw new InvalidOperationException("Fixture forbids physical plan writes")));
      await run(runner, new GuardianLoopIterationState(), path);
    }
    finally { Directory.Delete(root, true); }
  }

  [Fact]
  public async Task Lifecycle_revocation_during_ac_sync_prevents_dc_write()
  {
    var permitted = true;
    var native = new FakePowrProfNative { WriteValue = (_, _, _, _) => { permitted = false; return 0; } };
    await PowerSchemeTestScope.WithNative(native, () => WithRunner(async (runner, state, _) =>
    {
      state.PlanCatalog = new Dictionary<Guid, string>();
      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
    }, paused: false, brightness: 50, observedPlan: Balanced, canMutate: () => permitted));
    native.Calls.Where(call => call.StartsWith("write-value", StringComparison.Ordinal))
      .Should().ContainSingle().Which.Should().Contain(" Ac 50");
  }

  [Fact]
  public async Task Catalog_unconfirmed_cleanup_prevents_the_remaining_hidden_queries()
  {
    var commands = new List<string>();
    var observations = new List<WmiProbeObservation>();
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (command, _, _) =>
    {
      commands.Add(command);
      return Task.FromResult(command == "/list"
        ? new PowerCfgCommandResult(true, false, 0, $"GUID: {PowerPlanCatalogProvider.BalancedPlanGuid:D} (balanced)", "")
        : new PowerCfgCommandResult(true, true, null, "", "")
          { FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false });
    };
    try
    {
      await WithRunner((runner, state, _) => runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None),
        observe: observations.Add);
      commands.Should().HaveCount(2, "the first uncertain query closes the whole remaining enrichment chain");
      observations.Should().NotContain(x => x.Recovered);
    }
    finally { ResetOverrides(); }
  }

  [Fact]
  public async Task Plan_probe_recovery_is_announced_once_after_a_failure()
  {
    var round = 0;
    var native = new FakePowrProfNative { GetActive = () => ++round == 1 ? (5u, Guid.Empty) : (0u, High) };
    var observations = new List<WmiProbeObservation>();
    await PowerSchemeTestScope.WithNative(native, () => WithRunner(async (runner, state, _) =>
    {
      state.PlanCatalog = new Dictionary<Guid, string>();
      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      observations.Should().ContainSingle(x => x.FailureKind == "api-error" && x.Win32Error == 5);
      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      observations.Should().ContainSingle(x => x.Recovered);
    }, useNativePlanReader: true, observe: observations.Add));
  }

  [Fact]
  public async Task Oversized_initial_brightness_wait_cannot_hold_a_round_for_days()
  {
    await WithRunner(async (runner, state, _) =>
    {
      state.PlanCatalog = new Dictionary<Guid, string>();
      using var stop = new CancellationTokenSource();
      var running = runner.RunAsync(state, GuardianIterationContext.Running, stop.Token);
      try { await running.WaitAsync(TimeSpan.FromSeconds(3)); }
      finally
      {
        stop.Cancel();
        try { await running; } catch (OperationCanceledException) { }
      }
    }, paused: false, observedPlan: PowerPlanCatalogProvider.BalancedPlanGuid,
      configure: config => config.BrightnessRestoreMs = int.MaxValue,
      apply: (target, _) => Task.FromResult(new PowerPlanApplyResult(true, target, target, PowerPlanFailureKind.None, "")));
  }

  [Fact]
  public async Task Persistent_plan_read_failure_does_not_flood_the_legacy_warning_path()
  {
    var warnings = 0;
    var native = new FakePowrProfNative { GetActive = () => (5, Guid.Empty) };
    await PowerSchemeTestScope.WithNative(native, () => WithRunner(async (runner, state, _) =>
    {
      state.PlanCatalog = new Dictionary<Guid, string>();
      for (var i = 0; i < 12; i++)
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
    }, useNativePlanReader: true,
      loggedEvent: name => { if (name == EventNameRegistry.PowerPlanProbeFailed) warnings++; }));
    warnings.Should().Be(1, "native aggregation must not leave the legacy per-round warning flooding the log");
  }

  [Fact]
  public async Task Plan_apply_switches_through_the_scheme_service_and_verifies()
  {
    var active = Balanced;
    var native = new FakePowrProfNative
    {
      GetActive = () => (0, active),
      SetActive = scheme => { active = scheme; return 0; },
    };
    var events = new List<string>();
    await PowerSchemeTestScope.WithNative(native, () => WithRunner(async (runner, state, _) =>
    {
      state.PlanCatalog = new Dictionary<Guid, string>();
      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
    }, paused: false, useNativePlanReader: true, useNativeApply: true, loggedEvent: events.Add));
    native.Calls.Should().Contain(call => call.StartsWith("set-active ", StringComparison.Ordinal));
    events.Should().Contain(EventNameRegistry.PowerPlanSwitched);
  }
}
