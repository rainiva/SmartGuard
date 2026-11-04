using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

[Collection("LoggingFallbackTests")]
public sealed class GuardianProbeHardeningTests
{
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
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(
      new PowerCfgCommandResult(true, true, null, "", "") { FailureKind = "timed-out" });
    try
    {
      await WithRunner(async (runner, state, path) =>
      {
        state.PlanCatalog = new Dictionary<Guid, string>();
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
        StatusJsonReader.TryRead(path)!.guardReason.Should().Be("probe-failed");
      }, useNativePlanReader: true, observe: _ => throw new IOException("diagnostic sink unavailable"));
    }
    finally { ResetOverrides(); }
  }

  [Fact]
  public async Task Current_plan_probe_also_uses_cancelable_process_execution()
  {
    var syncCalls = 0;
    PowerCfgProcessRunner.RunOverrideForTests = (_, _) =>
    {
      syncCalls++;
      return new(false, false, null, "", "unexpected synchronous work");
    };
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(
      new PowerCfgCommandResult(true, false, 0,
        $"GUID: {PowerPlanCatalogProvider.HighPerformancePlanGuid:D} (high)", ""));
    try
    {
      await WithRunner(async (runner, state, path) =>
      {
        state.PlanCatalog = new Dictionary<Guid, string>();
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
        StatusJsonReader.TryRead(path)!.guardReason.Should().Be("paused");
      }, useNativePlanReader: true);
      syncCalls.Should().Be(0);
    }
    finally { ResetOverrides(); }
  }

  [Fact]
  public async Task Unconfirmed_process_cleanup_blocks_further_process_creation_in_that_guardian()
  {
    var calls = 0;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) =>
    {
      calls++;
      return Task.FromResult(new PowerCfgCommandResult(true, true, null, "", "")
        { FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false });
    };
    try
    {
      await WithRunner(async (runner, state, _) =>
      {
        state.PlanCatalog = new Dictionary<Guid, string>();
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      }, useNativePlanReader: true);
      calls.Should().Be(1, "an uncertain child cannot accumulate a new sibling on every periodic retry");
    }
    finally { ResetOverrides(); }
  }

  private static async Task WithRunner(Func<GuardianIterationRunner, GuardianLoopIterationState, string, Task> run,
    bool useNativePlanReader = false, Action<WmiProbeObservation>? observe = null,
    bool paused = true, int brightness = -1, Guid? observedPlan = null,
    Func<bool>? canMutate = null, Action<GuardConfig>? configure = null,
    Func<Guid, CancellationToken, Task<PowerPlanApplyResult>>? apply = null,
    Action<string>? loggedEvent = null)
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
        applyPlan: apply ?? ((_, _) => throw new InvalidOperationException("Fixture forbids physical plan writes")));
      await run(runner, new GuardianLoopIterationState(), path);
    }
    finally { Directory.Delete(root, true); }
  }

  [Fact]
  public async Task Lifecycle_revocation_during_ac_sync_prevents_dc_write()
  {
    var permitted = true;
    var commands = new List<string>();
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (command, _, _) =>
    {
      commands.Add(command);
      permitted = false;
      return Task.FromResult(new PowerCfgCommandResult(true, false, 0, "", ""));
    };
    try
    {
      await WithRunner(async (runner, state, _) =>
      {
        state.PlanCatalog = new Dictionary<Guid, string>();
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      }, paused: false, brightness: 50, observedPlan: PowerPlanCatalogProvider.BalancedPlanGuid, canMutate: () => permitted);
      commands.Should().ContainSingle().Which.Should().StartWith("/setacvalueindex");
    }
    finally { ResetOverrides(); }
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
  public async Task Malformed_success_exit_does_not_announce_plan_probe_recovery()
  {
    var round = 0;
    var observations = new List<WmiProbeObservation>();
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(++round switch
    {
      1 => new PowerCfgCommandResult(true, true, null, "", "") { FailureKind = "timed-out" },
      2 => new PowerCfgCommandResult(true, false, 0, "not a power plan", ""),
      _ => new PowerCfgCommandResult(true, false, 0, $"GUID: {PowerPlanCatalogProvider.HighPerformancePlanGuid:D} (high)", ""),
    });
    try
    {
      await WithRunner(async (runner, state, _) =>
      {
        state.PlanCatalog = new Dictionary<Guid, string>();
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
        observations.Should().NotContain(x => x.Recovered);
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
        observations.Should().ContainSingle(x => x.Recovered);
      }, useNativePlanReader: true, observe: observations.Add);
    }
    finally { ResetOverrides(); }
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
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(
      new PowerCfgCommandResult(true, true, null, "", "") { FailureKind = "timed-out" });
    try
    {
      await WithRunner(async (runner, state, _) =>
      {
        state.PlanCatalog = new Dictionary<Guid, string>();
        for (var i = 0; i < 12; i++)
          await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      }, useNativePlanReader: true,
        loggedEvent: name => { if (name == EventNameRegistry.PowerPlanProbeFailed) warnings++; });
      warnings.Should().Be(1, "native aggregation must not leave the legacy per-round warning flooding the log");
    }
    finally { ResetOverrides(); }
  }
}
