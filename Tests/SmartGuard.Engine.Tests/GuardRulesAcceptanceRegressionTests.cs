using System.Runtime.InteropServices;
using FluentAssertions.Execution;
using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

/// <summary>Acceptance regressions with isolated configuration and intercepted power side effects.</summary>
public sealed class GuardRulesAcceptanceRegressionTests
{
  private const string ListedPath = @"C:\Windows\System32\notepad.exe";

  [Theory]
  [InlineData("foreground-left")]
  [InlineData("paused")]
  [InlineData("removed")]
  [InlineData("unavailable")]
  public async Task Revoked_action_qualification_suppresses_all_pre_switch_effects(string change)
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardRecheck_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var clock = new ManualTimeProvider();
      var config = GuardConfig.CreateDefault(root);
      config.ForegroundRestrictionEnabled = true;
      config.ForegroundProgramPaths = [ListedPath];
      config.HeartbeatIntervalMin = 0;
      config.BrightnessRestoreMs = 0;
      // Even a broken implementation must never write physical brightness during this regression.
      config.BrightnessRetryCount = 0;
      var currentConfig = GuardConfigReadResult.FromConfig(config);
      var foreground = ForegroundObservation.Identified(ListedPath);
      var state = new GuardianLoopIterationState
      {
        PlanCatalog = new Dictionary<Guid, string>(),
        LastKnownGuid = config.PowerSaverPlanGuid,
        LastForegroundAdmitted = true,
      };
      var events = new List<string>();
      var tracker = new GuardObservationTracker();
      var guardFacts = new List<System.Text.Json.JsonElement>();
      var synced = new List<Guid>();
      var applied = new List<Guid>();
      var changed = false;
      var runner = new GuardianIterationRunner(
        new IdleTracker(clock), new BrightnessService(),
        new StatusPublisher(Path.Combine(root, "status.json"), clock),
        () => currentConfig,
        (_, _, _, eventName, properties, _) =>
        {
          events.Add(eventName);
          if (eventName == EventNameRegistry.GuardStateChanged) guardFacts.Add(properties!["actionResult"]);
          return GuardianLogWriteResult.AcceptedByPipeline;
        },
        _ => { }, () => true, new HeartbeatSchedule(clock),
        readIdleSeconds: () => 0,
        readBatteryInfo: _ => (90, true),
        readCurrentPlan: () => new(true, new PowerSchemeInfo(config.BalancedPlanGuid, null),
          PowerPlanFailureKind.None, string.Empty),
        applyPlan: (target, _) =>
        {
          applied.Add(target);
          return Task.FromResult(new PowerPlanApplyResult(true, target, target,
            PowerPlanFailureKind.None, string.Empty));
        },
        syncPlanBrightness: (target, _) => synced.Add(target),
        timeProvider: clock,
        readForeground: () => foreground,
        readBrightness: () =>
        {
          // WMI can take time: qualification may change after the initial snapshot but before action.
          if (!changed)
          {
            changed = true;
            var revised = GuardConfig.CreateDefault(root);
            revised.ForegroundRestrictionEnabled = true;
            revised.ForegroundProgramPaths = [ListedPath];
            revised.HeartbeatIntervalMin = 0;
            revised.BrightnessRetryCount = 0;
            switch (change)
            {
              case "foreground-left":
                foreground = ForegroundObservation.Identified(@"C:\Windows\explorer.exe");
                break;
              case "paused":
                revised.Paused = true;
                currentConfig = GuardConfigReadResult.FromConfig(revised);
                break;
              case "removed":
                revised.ForegroundProgramPaths = [];
                currentConfig = GuardConfigReadResult.FromConfig(revised);
                break;
              case "unavailable":
                currentConfig = GuardConfigReadResult.Unavailable;
                break;
            }
          }
          return 50;
        }, observationTracker: tracker);

      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      tracker.Drain(currentConfig.Config, (_, _, _, name, properties, _) =>
      {
        events.Add(name);
        if (name == EventNameRegistry.GuardStateChanged) guardFacts.Add(properties!["actionResult"]);
        return GuardianLogWriteResult.AcceptedByPipeline;
      });

      using var scope = new AssertionScope();
      changed.Should().BeTrue("the change must happen after the initial qualification snapshot");
      synced.Should().BeEmpty("revoked qualification forbids even pre-switch brightness synchronization");
      applied.Should().BeEmpty("a previously admitted snapshot cannot authorize a later action");
      events.Should().NotContain(EventNameRegistry.ExternalPlanChange);
      state.LastEmittedNotification.Should().BeNull("revoked qualification must not emit a correction notification");
      guardFacts.Should().ContainSingle().Which.GetString().Should().Be("not-attempted");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Action_recheck_uses_current_idle_and_power_facts(bool becameIdle)
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardFacts_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var clock = new ManualTimeProvider();
      var config = GuardConfig.CreateDefault(root);
      config.BrightnessRestoreMs = 0;
      config.BrightnessRetryCount = 0;
      config.HeartbeatIntervalMin = 0;
      var changed = false;
      var applied = new List<Guid>();
      var runner = new GuardianIterationRunner(
        new IdleTracker(clock), new BrightnessService(), new StatusPublisher(Path.Combine(root, "status.json"), clock),
        () => GuardConfigReadResult.FromConfig(config),
        (_, _, _, _, _, _) => GuardianLogWriteResult.AcceptedByPipeline,
        _ => { }, () => false, new HeartbeatSchedule(clock),
        readIdleSeconds: () => changed && becameIdle ? (uint)(config.PowerSaverThresholdSec + 1) : 0,
        readBatteryInfo: _ => changed && !becameIdle ? (1, false) : (90, true),
        readCurrentPlan: () => new(true, new PowerSchemeInfo(
          becameIdle ? config.BalancedPlanGuid : config.PowerSaverPlanGuid, null), PowerPlanFailureKind.None, ""),
        applyPlan: (target, _) =>
        {
          applied.Add(target);
          return Task.FromResult(new PowerPlanApplyResult(true, target, target, PowerPlanFailureKind.None, ""));
        },
        timeProvider: clock,
        readBrightness: () => { changed = true; return -1; });

      await runner.RunAsync(new GuardianLoopIterationState { PlanCatalog = new Dictionary<Guid, string>() },
        GuardianIterationContext.Running, CancellationToken.None);

      applied.Should().Equal(new[] { becameIdle ? config.PowerSaverPlanGuid : config.BalancedPlanGuid },
        "idle threshold and low-battery facts can change during slow initial probes");
    }
    finally { Directory.Delete(root, recursive: true); }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task Revocation_during_forced_battery_probe_prevents_stale_configuration_action(bool unavailable)
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardSlowProbe_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var clock = new ManualTimeProvider();
      var config = GuardConfig.CreateDefault(root);
      config.ForegroundRestrictionEnabled = true;
      config.ForegroundProgramPaths = [ListedPath];
      config.HeartbeatIntervalMin = 0;
      config.BrightnessRestoreMs = 0;
      config.BrightnessRetryCount = 0;
      var latest = GuardConfigReadResult.FromConfig(config);
      var batteryReads = 0;
      var applied = new List<Guid>();
      var events = new List<string>();
      var state = new GuardianLoopIterationState
      {
        PlanCatalog = new Dictionary<Guid, string>(),
        LastKnownGuid = config.PowerSaverPlanGuid,
        LastForegroundAdmitted = true,
      };
      var runner = new GuardianIterationRunner(
        new IdleTracker(clock), new BrightnessService(), new StatusPublisher(Path.Combine(root, "status.json"), clock),
        () => latest,
        (_, _, _, eventName, _, _) => { events.Add(eventName); return GuardianLogWriteResult.AcceptedByPipeline; },
        _ => { }, () => false, new HeartbeatSchedule(clock),
        readIdleSeconds: () => 0,
        readBatteryInfo: _ =>
        {
          if (++batteryReads == 2)
          {
            // Simulate another process saving a new config while a slow forced WMI query is in flight.
            // Replace the object: mutating the old instance would conceal an incorrectly ordered reload.
            var paused = GuardConfig.CreateDefault(root);
            paused.Paused = true;
            latest = unavailable ? GuardConfigReadResult.Unavailable : GuardConfigReadResult.FromConfig(paused);
          }
          return (90, true);
        },
        readCurrentPlan: () => new(true, new PowerSchemeInfo(config.BalancedPlanGuid, null), PowerPlanFailureKind.None, ""),
        applyPlan: (target, _) =>
        {
          applied.Add(target);
          return Task.FromResult(new PowerPlanApplyResult(true, target, target, PowerPlanFailureKind.None, ""));
        },
        timeProvider: clock,
        readForeground: () => ForegroundObservation.Identified(ListedPath),
        readBrightness: () => -1);

      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);

      using var scope = new AssertionScope();
      batteryReads.Should().Be(2, "the revocation must occur inside the action-time forced probe");
      applied.Should().BeEmpty("slow probes must finish before the final configuration authorization read");
      events.Should().NotContain(EventNameRegistry.ExternalPlanChange);
      state.LastEmittedNotification.Should().BeNull("revoked configuration cannot authorize a correction notification");
    }
    finally { Directory.Delete(root, recursive: true); }
  }

  [Fact]
  public async Task Empty_list_full_iteration_does_not_query_process_identity()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardEmpty_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var clock = new ManualTimeProvider();
      var config = GuardConfig.CreateDefault(root);
      config.ForegroundRestrictionEnabled = true;
      config.ForegroundProgramPaths = [];
      config.HeartbeatIntervalMin = 0;
      var reads = 0;
      var runner = new GuardianIterationRunner(
        new IdleTracker(clock), new BrightnessService(), new StatusPublisher(Path.Combine(root, "status.json"), clock),
        () => GuardConfigReadResult.FromConfig(config),
        (_, _, _, _, _, _) => GuardianLogWriteResult.AcceptedByPipeline,
        _ => { }, () => false, new HeartbeatSchedule(clock),
        readIdleSeconds: () => 0, readBatteryInfo: _ => (90, true),
        readCurrentPlan: () => new(true, new PowerSchemeInfo(config.BalancedPlanGuid, null), PowerPlanFailureKind.None, ""),
        applyPlan: (_, _) => throw new InvalidOperationException("An empty list cannot authorize a switch."),
        timeProvider: clock,
        readForeground: () => { reads++; return ForegroundObservation.Identified(ListedPath); },
        readBrightness: () => -1);

      await runner.RunAsync(new GuardianLoopIterationState { PlanCatalog = new Dictionary<Guid, string>() },
        GuardianIterationContext.Running, CancellationToken.None);

      reads.Should().Be(0, "periodic compensation also has no useful identity to collect with an empty list");
    }
    finally { Directory.Delete(root, recursive: true); }
  }

  [Fact]
  public async Task Empty_list_foreground_event_does_not_query_process_identity()
  {
    var clock = new ManualTimeProvider();
    using var wake = new SemaphoreSlim(0);
    var config = GuardConfig.CreateDefault(Path.GetTempPath());
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = [];
    var reads = 0;
    var coordinator = new GuardianCycleCoordinator(
      () => GuardConfigReadResult.FromConfig(config),
      () => { reads++; return ForegroundObservation.Identified(ListedPath); },
      new GuardianLoopIterationState { LastForegroundAdmitted = false }, wake,
      (_, _) => Task.CompletedTask, clock);
    var directive = new PowerLifecycleStateMachine(clock).TakeDirective();
    await coordinator.RunCycleAsync(directive, 15, CancellationToken.None);
    clock.Advance(TimeSpan.FromSeconds(1));
    coordinator.MarkForegroundSignal();

    await coordinator.RunCycleAsync(directive, 15, CancellationToken.None);

    reads.Should().Be(0, "an empty admission list has no useful foreground identity to collect");
  }

  [Fact]
  public async Task Default_message_pump_consumes_posted_quit_without_raw_input()
  {
    var registered = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
    var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    // Only a private thread queue is used. No window, real hook, foreground change or input is generated.
    using var listener = new ForegroundEventListener(
      () => { },
      setHook: callback =>
      {
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        registered.SetResult(GetCurrentThreadId());
        return new IntPtr(1);
      },
      unhook: _ => exited.TrySetResult());
    listener.Start();
    var thread = await registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    PostThreadMessage(thread, 0x0012, IntPtr.Zero, IntPtr.Zero).Should().BeTrue();
    var completed = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(2)));
    // Assert before Dispose signals the stop handle: stopping explicitly would hide a broken wake mask.
    completed.Should().BeSameAs(exited.Task, "WM_QUIT must wake and exit the real message pump without raw input");
  }

  [DllImport("kernel32.dll")]
  private static extern uint GetCurrentThreadId();

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

  [DllImport("user32.dll")]
  private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);

  [StructLayout(LayoutKind.Sequential)]
  private struct NativeMessage
  {
    public IntPtr Window;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public int X;
    public int Y;
    public uint Private;
  }
}
