using System.Reflection;
using System.Text;
using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

/// <summary>
/// 任务 3a：真实前台采集 + 受控事件监听 + 循环整合的行为测试。
/// 事件/事实序列与节流参数全部可注入，时间使用单调假时钟，不做真实睡眠等待。
/// </summary>
public class ForegroundGuardIntegrationTests
{
  internal const string NotepadPath = @"C:\Windows\System32\Notepad.exe";
  internal const string CalcPath = @"C:\Windows\System32\Calc.exe";

  // ---- 轻路径与周期截止时间（GuardianCycleCoordinator + Runner 合成） ----

  [Fact]
  public async Task Foreground_event_burst_merges_into_single_light_recheck_and_single_wake()
  {
    using var harness = ForegroundGuardHarness.Create();
    harness.SetForeground(NotepadPath);
    await harness.RunCycleAsync();
    harness.FullRunnerRuns.Should().Be(1);
    harness.LastAdmitted.Should().BeTrue();

    harness.Advance(TimeSpan.FromSeconds(20));
    await harness.RunCycleAsync();
    harness.FullRunnerRuns.Should().Be(2);
    harness.DrainWakeSignalReleases().Should().Be(0);

    for (var i = 0; i < 5; i++)
      harness.ForegroundChanged();
    harness.DrainWakeSignalReleases().Should().Be(1, "N 次前台事件合并为一次待处理信号与一次唤醒");

    var lightReadsBefore = harness.LightReads;
    await harness.RunCycleAsync();
    harness.LightReads.Should().Be(lightReadsBefore + 1, "合并后的信号只触发一次轻路径复核");
    harness.FullRunnerRuns.Should().Be(2, "准入结果未变不增加完整轮次");
  }

  [Fact]
  public async Task Pending_signal_survives_light_throttle_and_is_processed_after_floor()
  {
    using var harness = ForegroundGuardHarness.Create();
    harness.SetForeground(NotepadPath);
    await harness.RunCycleAsync();
    harness.Advance(TimeSpan.FromSeconds(1));
    harness.ForegroundChanged();
    await harness.RunCycleAsync();
    harness.LightReads.Should().Be(1);

    // 节流窗口内的新事件：不丢失，保留为待处理标记（级别触发）
    harness.Advance(TimeSpan.FromMilliseconds(200));
    harness.ForegroundChanged();
    await harness.RunCycleAsync();
    harness.LightReads.Should().Be(1, "节流窗口内不重复复核");
    harness.HasPendingForegroundSignal.Should().BeTrue("最后信号不能被丢弃");
    harness.ComputeWaitDelay().Should().BeGreaterThan(TimeSpan.Zero)
      .And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(1), "须在节流到期前唤醒补处理");

    // 节流到期后无需新事件即补处理（末次信号仍被核验）
    harness.Advance(TimeSpan.FromMilliseconds(900));
    await harness.RunCycleAsync();
    harness.LightReads.Should().Be(2);
    harness.FullRunnerRuns.Should().Be(1);
  }

  [Fact]
  public async Task Hit_transition_requests_full_evaluation_respecting_five_second_floor()
  {
    using var harness = ForegroundGuardHarness.Create();
    harness.SetForeground(CalcPath);
    await harness.RunCycleAsync();
    harness.LastAdmitted.Should().BeFalse("未命中名单 → 不准入");
    harness.FullRunnerRuns.Should().Be(1);

    harness.Advance(TimeSpan.FromSeconds(6));
    harness.SetForeground(NotepadPath);
    harness.ForegroundChanged();
    await harness.RunCycleAsync();
    harness.FullRunnerRuns.Should().Be(2, "未命中→命中转变在节流允许时立即触发完整评估");
    harness.LastAdmitted.Should().BeTrue();

    // 命中 → 未命中：立即撤销资格，不触发完整评估、不探测电源计划
    harness.Advance(TimeSpan.FromSeconds(1));
    harness.SetForeground(CalcPath);
    harness.ForegroundChanged();
    await harness.RunCycleAsync();
    harness.LastAdmitted.Should().BeFalse();
    harness.FullRunnerRuns.Should().Be(2);

    // 未命中 → 命中：5 秒下限拦下 → 挂起请求；节流结束后必须补跑
    harness.Advance(TimeSpan.FromSeconds(1));
    harness.SetForeground(NotepadPath);
    harness.ForegroundChanged();
    await harness.RunCycleAsync();
    harness.FullRunnerRuns.Should().Be(2, "距上次完整评估 2 秒 < 5 秒下限");

    harness.Advance(TimeSpan.FromSeconds(1));
    await harness.RunCycleAsync();
    harness.FullRunnerRuns.Should().Be(2, "距上次完整评估 3 秒 < 5 秒下限");

    harness.Advance(TimeSpan.FromSeconds(3));
    await harness.RunCycleAsync();
    harness.FullRunnerRuns.Should().Be(3, "末次稳定前台须在节流结束后重新评估");
    harness.LastAdmitted.Should().BeTrue();
  }

  [Fact]
  public async Task Unchanged_admission_light_rechecks_add_no_full_runs_logs_or_status_writes()
  {
    using var harness = ForegroundGuardHarness.Create();
    harness.SetForeground(NotepadPath);
    await harness.RunCycleAsync();
    var fullRunsBefore = harness.FullRunnerRuns;
    var logsBefore = harness.Events.Count;
    var statusBefore = harness.StatusContent;
    statusBefore.Should().NotBeEmpty("首轮完整轮次发布过状态");

    for (var second = 1; second <= 10; second++)
    {
      harness.Advance(TimeSpan.FromSeconds(1));
      harness.ForegroundChanged();
      await harness.RunCycleAsync();
    }

    harness.FullRunnerRuns.Should().Be(fullRunsBefore, "准入未变的轻路径不触发完整 Runner");
    harness.LightReads.Should().Be(10, "轻路径按节流上限每秒一次复核");
    harness.Events.Count.Should().Be(logsBefore, "准入未变不写日志");
    harness.StatusContent.Should().Be(statusBefore, "准入未变不写状态");
  }

  [Fact]
  public async Task Revoked_admission_stops_automatic_action_until_periodic_full_recheck()
  {
    using var harness = ForegroundGuardHarness.Create();
    harness.SetForeground(NotepadPath);
    await harness.RunCycleAsync();
    harness.LastAdmitted.Should().BeTrue();

    harness.SetIdle(400);
    harness.Advance(TimeSpan.FromSeconds(2));
    harness.SetForeground(CalcPath);
    harness.ForegroundChanged();
    await harness.RunCycleAsync();
    harness.LastAdmitted.Should().BeFalse("命中→未命中立即撤销自动动作资格");
    harness.AppliedPlans.Should().BeEmpty("轻路径不执行电源动作");
    harness.FullRunnerRuns.Should().Be(1);

    harness.Advance(TimeSpan.FromSeconds(15));
    await harness.RunCycleAsync();
    harness.AppliedPlans.Should().BeEmpty("深度空闲但未命中名单 → 限制族不干预，不纠偏");
    harness.FullRunnerRuns.Should().Be(2);
    harness.LastAdmitted.Should().BeFalse();
  }

  [Fact]
  public async Task Continuous_foreground_events_do_not_postpone_periodic_deadline()
  {
    using var harness = ForegroundGuardHarness.Create();
    harness.SetForeground(NotepadPath);
    await harness.RunCycleAsync();
    harness.FullRunnerRuns.Should().Be(1);

    for (var second = 1; second <= 14; second++)
    {
      harness.Advance(TimeSpan.FromSeconds(1));
      harness.ForegroundChanged();
      await harness.RunCycleAsync();
      harness.FullRunnerRuns.Should().Be(1, $"第 {second} 秒的前台事件不得触发完整轮次或推迟周期");
    }

    harness.Advance(TimeSpan.FromSeconds(1));
    await harness.RunCycleAsync();
    harness.FullRunnerRuns.Should().Be(2, "持续事件不得饿死周期完整轮次（截止 = 上一完整轮次 + 间隔）");
  }

  [Fact]
  public async Task Suspend_holds_foreground_work_and_resume_recollects_fresh_facts()
  {
    using var harness = ForegroundGuardHarness.Create();
    harness.SetForeground(NotepadPath);
    await harness.RunCycleAsync();
    harness.LastAdmitted.Should().BeTrue();

    harness.Signal(PowerLifecycleKind.Suspending);
    harness.Advance(TimeSpan.FromSeconds(1));
    harness.ForegroundChanged();
    await harness.RunCycleAsync();
    harness.FullRunnerRuns.Should().Be(1, "挂起阶段不执行轮次");
    harness.LightReads.Should().Be(0, "挂起阶段不为前台事件做业务查询");

    harness.Advance(TimeSpan.FromMinutes(68));
    harness.SetForeground(CalcPath);
    harness.Signal(PowerLifecycleKind.Resumed);
    await harness.RunCycleAsync();
    harness.FullRunnerRuns.Should().Be(2, "恢复指令强制完整轮次");
    harness.LastAdmitted.Should().BeFalse("恢复后重新采集最新前台（calc 未命中），不沿用陈旧准入");
    harness.LightReads.Should().Be(0, "挂起积压信号由完整轮次吸收，不重复轻路径");
  }

  [Fact]
  public async Task Power_wake_forces_full_iteration_before_deadline()
  {
    using var harness = ForegroundGuardHarness.Create();
    harness.SetForeground(NotepadPath);
    await harness.RunCycleAsync();
    harness.Advance(TimeSpan.FromSeconds(2));

    harness.Signal(PowerLifecycleKind.StatusChanged);
    await harness.RunCycleAsync();
    harness.FullRunnerRuns.Should().Be(2, "电源事件唤醒强制完整轮次（不等待周期截止）");
  }

  [Fact]
  public async Task Cycle_failure_re_arms_interval_and_converges_instead_of_tight_looping()
  {
    using var harness = ForegroundGuardHarness.Create();
    harness.BreakConfigLoad();
    harness.SetForeground(NotepadPath);

    await Assert.ThrowsAsync<IOException>(() => harness.RunCycleAsync());
    harness.NoteCycleFailure(15);

    var delay = harness.ComputeWaitDelay();
    delay.Should().BePositive("失败后按完整间隔冷却，不紧密重试");
    delay.Should().BeCloseTo(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1));

    // 收敛：失败后丢弃节流中的前台请求，下一真实事件重新标记
    harness.ForegroundChanged();
    harness.NoteCycleFailure(15);
    harness.HasPendingForegroundSignal.Should().BeFalse("失败路径收敛到确定状态");
  }

  [Fact]
  public void Initial_throttle_parameters_are_frozen_for_trial()
  {
    GuardianIterationTiming.ForegroundLightRecheckFloor.Should().Be(TimeSpan.FromSeconds(1));
    GuardianIterationTiming.ForegroundFullEvaluationFloor.Should().Be(TimeSpan.FromSeconds(5));
  }

  [Fact]
  public async Task Custom_floors_override_defaults_for_injectable_trials()
  {
    using var harness = ForegroundGuardHarness.Create(lightRecheckFloor: TimeSpan.FromSeconds(2));
    harness.SetForeground(NotepadPath);
    await harness.RunCycleAsync();

    harness.Advance(TimeSpan.FromMilliseconds(500));
    harness.ForegroundChanged();
    await harness.RunCycleAsync();
    harness.LightReads.Should().Be(1, "首次轻路径复核不受节流限制");

    harness.Advance(TimeSpan.FromSeconds(1));
    harness.ForegroundChanged();
    await harness.RunCycleAsync();
    harness.LightReads.Should().Be(1, "自定义 2 秒下限内不复核（默认 1 秒会复核）");
    harness.HasPendingForegroundSignal.Should().BeTrue();

    harness.Advance(TimeSpan.FromSeconds(1));
    await harness.RunCycleAsync();
    harness.LightReads.Should().Be(2, "自定义下限到期后补处理保留的信号");
  }

  // ---- ForegroundProgramReader（Win32 时序可注入） ----

  [Fact]
  public void Reader_identifies_foreground_path_reverifies_identity_and_releases_handle()
  {
    var seams = new FakeWin32Seams();
    var reader = seams.CreateReader();

    var observation = reader.Read();

    observation.IdentificationSucceeded.Should().BeTrue();
    observation.ExecutablePath.Should().Be(seams.ImagePath);
    seams.GetForegroundWindowCalls.Should().Be(2, "查询后须复核前台 HWND");
    seams.OpenProcessCalls.Should().Be(1);
    seams.CloseHandleCalls.Should().Be(1, "成功路径释放进程句柄");
    seams.BufferCapacitySeen.Should().Be(ForegroundProgramReader.ImageNameBufferCapacityChars);
    seams.BufferCapacitySeen.Should().Be(32768, "缓冲上限按计划预算 32,768 字符");
  }

  [Fact]
  public void Reader_returns_unknown_when_foreground_window_is_null()
  {
    var seams = new FakeWin32Seams { InitialHwnd = IntPtr.Zero };
    var observation = seams.CreateReader().Read();

    observation.IdentificationSucceeded.Should().BeFalse();
    observation.FailureReason.Should().NotBeNullOrEmpty();
    seams.GetForegroundWindowCalls.Should().Be(1);
    seams.OpenProcessCalls.Should().Be(0);
    seams.CloseHandleCalls.Should().Be(0);
  }

  [Fact]
  public void Reader_returns_unknown_when_foreground_window_changes_during_query()
  {
    var seams = new FakeWin32Seams { RecheckHwnd = new IntPtr(0x2222) };
    var observation = seams.CreateReader().Read();

    observation.IdentificationSucceeded.Should().BeFalse("查询期间前台 HWND 已切换");
    observation.FailureReason.Should().NotBeNullOrEmpty();
    seams.CloseHandleCalls.Should().Be(1, "失败路径同样释放进程句柄");
  }

  [Fact]
  public void Reader_returns_unknown_when_process_id_changes_during_query()
  {
    var seams = new FakeWin32Seams { RecheckPid = 9999 };
    var observation = seams.CreateReader().Read();

    observation.IdentificationSucceeded.Should().BeFalse("查询期间前台 PID 已变化");
    observation.FailureReason.Should().NotBeNullOrEmpty();
    seams.CloseHandleCalls.Should().Be(1);
  }

  [Fact]
  public void Reader_returns_unknown_when_process_exits_mid_query()
  {
    var seams = new FakeWin32Seams { FailOpenProcess = true };
    var observation = seams.CreateReader().Read();

    observation.IdentificationSucceeded.Should().BeFalse("进程退出 → 打开失败 → 识别不可用");
    observation.FailureReason.Should().NotBeNullOrEmpty();
    seams.OpenProcessCalls.Should().Be(1);
    seams.CloseHandleCalls.Should().Be(0, "未取得句柄则无需释放");
  }

  [Fact]
  public void Reader_returns_unknown_when_image_query_fails_and_still_releases_handle()
  {
    var seams = new FakeWin32Seams { FailQuery = true };
    var observation = seams.CreateReader().Read();

    observation.IdentificationSucceeded.Should().BeFalse();
    observation.FailureReason.Should().NotBeNullOrEmpty();
    seams.CloseHandleCalls.Should().Be(1, "查询失败路径在 finally 中释放句柄");
  }

  [Fact]
  public void Reader_returns_unknown_when_image_path_is_empty()
  {
    var seams = new FakeWin32Seams { ImagePath = string.Empty };
    var observation = seams.CreateReader().Read();

    observation.IdentificationSucceeded.Should().BeFalse();
    observation.FailureReason.Should().NotBeNullOrEmpty();
    seams.CloseHandleCalls.Should().Be(1);
  }

  [Fact]
  public void Reader_real_win32_smoke_never_throws_and_holds_path_shape()
  {
    var reader = new ForegroundProgramReader();
    var observation = reader.Read();

    if (observation.IdentificationSucceeded)
      observation.ExecutablePath!.ToLowerInvariant().Should().EndWith(".exe");
    else
      observation.FailureReason.Should().NotBeNullOrEmpty();
  }

  // ---- ForegroundEventListener（hook / 线程 / 停止语义可注入） ----

  [Fact]
  public async Task Listener_registers_hook_once_forwards_events_and_unhooks_on_listener_thread_on_stop()
  {
    var signals = 0;
    IntPtr registeredHandle = IntPtr.Zero;
    var captured = default(WinEventDelegate);
    var hooked = new TaskCompletionSource<bool>();
    var unhooked = new TaskCompletionSource<IntPtr>();
    var hookThreadId = 0;
    var unhookThreadId = 0;

    using var listener = new ForegroundEventListener(
      () => Interlocked.Increment(ref signals),
      setHook: callback =>
      {
        captured = callback;
        hookThreadId = Environment.CurrentManagedThreadId;
        registeredHandle = new IntPtr(0xABCD);
        hooked.TrySetResult(true);
        return registeredHandle;
      },
      unhook: handle =>
      {
        unhookThreadId = Environment.CurrentManagedThreadId;
        unhooked.TrySetResult(handle);
      },
      pumpUntilStop: stop =>
      {
        stop.WaitOne();
        return true;
      });

    listener.Start();
    (await hooked.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
    listener.IsRunning.Should().BeTrue();
    listener.IsHealthy.Should().BeTrue();
    listener.LastFailure.Should().BeNull();
    captured.Should().NotBeNull("托管回调委托须在注册前固定持有");

    captured!(IntPtr.Zero, ForegroundEventListener.EventSystemForeground, IntPtr.Zero, 0, 0, 0, 0);
    captured(IntPtr.Zero, ForegroundEventListener.EventSystemForeground, IntPtr.Zero, 0, 0, 0, 0);
    signals.Should().Be(2, "监听器逐事件转发，合并标记由协调器负责");

    listener.Stop();
    listener.IsRunning.Should().BeFalse();
    listener.IsHealthy.Should().BeFalse();
    var unhookHandle = await unhooked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    unhookHandle.Should().Be(registeredHandle, "卸载的正是注册的 hook");
    unhookThreadId.Should().Be(hookThreadId, "UnhookWinEvent 必须在注册线程调用");
    hookThreadId.Should().NotBe(Environment.CurrentManagedThreadId, "监听运行在专用线程上");

    captured(IntPtr.Zero, ForegroundEventListener.EventSystemForeground, IntPtr.Zero, 0, 0, 0, 0);
    signals.Should().Be(2, "Stop 之后回调不得再产生任何写入或工作");
  }

  [Fact]
  public async Task Listener_registration_failure_is_observable_without_unhook()
  {
    var unhookCalls = 0;
    using var listener = new ForegroundEventListener(
      () => { },
      setHook: _ => IntPtr.Zero,
      unhook: _ => unhookCalls++,
      pumpUntilStop: stop =>
      {
        stop.WaitOne();
        return true;
      });

    listener.Start();
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    while (listener.IsRunning && DateTime.UtcNow < deadline)
      await Task.Delay(10);

    listener.IsRunning.Should().BeFalse("注册失败的监听线程自行退出");
    listener.IsHealthy.Should().BeFalse();
    listener.LastFailure.Should().NotBeNullOrEmpty();
    unhookCalls.Should().Be(0, "注册失败无 hook 可卸载");

    listener.Stop();
  }

  [Fact]
  public async Task Listener_abnormal_message_loop_exit_is_observable()
  {
    using var abnormalReturn = new ForegroundEventListener(() => { }, pumpUntilStop: _ => false);
    abnormalReturn.Start();
    await WaitForAsync(() => !abnormalReturn.IsRunning);
    abnormalReturn.IsHealthy.Should().BeFalse();
    abnormalReturn.LastFailure.Should().NotBeNullOrEmpty();

    using var thrownPump = new ForegroundEventListener(
      () => { },
      setHook: _ => new IntPtr(0xBEEF),
      unhook: _ => { },
      pumpUntilStop: _ => throw new InvalidOperationException("pump exploded"));
    thrownPump.Start();
    await WaitForAsync(() => !thrownPump.IsRunning);
    thrownPump.IsHealthy.Should().BeFalse();
    thrownPump.LastFailure.Should().Contain("pump exploded");
    thrownPump.Stop();
  }

  [Fact]
  public async Task Listener_repeated_start_stop_cycles_do_not_accumulate_hooks_or_threads()
  {
    var setHookCalls = 0;
    var unhookCalls = 0;
    var hookedHandles = new List<IntPtr>();
    var unhookedHandles = new List<IntPtr>();
    var currentHandle = IntPtr.Zero;

    using var listener = new ForegroundEventListener(
      () => { },
      setHook: callback =>
      {
        setHookCalls++;
        currentHandle = new IntPtr(0xAB00 + setHookCalls);
        hookedHandles.Add(currentHandle);
        return currentHandle;
      },
      unhook: handle => unhookedHandles.Add(handle),
      pumpUntilStop: stop =>
      {
        stop.WaitOne();
        return true;
      });

    for (var cycle = 0; cycle < 5; cycle++)
    {
      listener.Start();
      await WaitForAsync(() => listener.IsHealthy);
      listener.IsRunning.Should().BeTrue();
      listener.Stop();
      listener.IsRunning.Should().BeFalse();
    }

    setHookCalls.Should().Be(5, "每次 Start 恰好注册一个 hook");
    unhookCalls.Should().Be(0, "卸载经真实实现路径计数不可见，改以句柄序列断言");
    unhookedHandles.Should().Equal(hookedHandles, "重复启停不积累 hook");
  }

  [Fact]
  public async Task Listener_real_win32_hook_starts_and_stops_cleanly()
  {
    using var listener = new ForegroundEventListener(() => { });
    listener.Start();
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    while (!listener.IsHealthy && DateTime.UtcNow < deadline)
      await Task.Delay(10);

    listener.IsHealthy.Should().BeTrue("真机 Windows 上 EVENT_SYSTEM_FOREGROUND 注册应成功");
    listener.LastFailure.Should().BeNull();

    listener.Stop();
    listener.IsRunning.Should().BeFalse();
    listener.IsHealthy.Should().BeFalse();
    listener.LastFailure.Should().BeNull("受控停止不算失败");
  }

  // ---- GuardianLoop 订阅同步（真实 GuardianLoop + 工厂注入） ----

  [Fact]
  public void Loop_syncs_listener_with_restriction_toggle_and_rebuilds_only_on_off_to_on()
  {
    var created = new List<ForegroundEventListener>();
    GuardianLoop.ForegroundListenerFactoryOverrideForTests = onSignal =>
    {
      var listener = new ForegroundEventListener(
        onSignal,
        setHook: _ => new IntPtr(0x5A00 + created.Count),
        unhook: _ => { },
        pumpUntilStop: stop =>
        {
          stop.WaitOne();
          return true;
        });
      created.Add(listener);
      return listener;
    };
    var root = CreateTempRoot();
    try
    {
      var configPath = Path.Combine(root, "SmartGuard.config.json");
      var loop = CreateLoop(root, configPath);

      WriteConfig(configPath, restrictionEnabled: false);
      loop.SyncForegroundSubscription();
      created.Should().BeEmpty("限制关闭：不创建监听");
      loop.ReadForegroundForRunner().IdentificationSucceeded.Should().BeFalse("限制关闭跳过采集");
      loop.ForegroundListenerRunning.Should().BeFalse();

      WriteConfig(configPath, restrictionEnabled: true);
      loop.SyncForegroundSubscription();
      created.Should().HaveCount(1);
      loop.ForegroundListenerRunning.Should().BeTrue();

      WriteConfig(configPath, restrictionEnabled: false);
      loop.SyncForegroundSubscription();
      created.Should().HaveCount(1);
      created[0].IsRunning.Should().BeFalse("限制关闭时卸载 hook 并等待线程退出");
      loop.ForegroundListenerRunning.Should().BeFalse();

      WriteConfig(configPath, restrictionEnabled: true);
      loop.SyncForegroundSubscription();
      created.Should().HaveCount(2, "限制关闭→开启转换允许重建监听");
      loop.ForegroundListenerRunning.Should().BeTrue();

      loop.SyncForegroundSubscription();
      loop.SyncForegroundSubscription();
      created.Should().HaveCount(2, "已运行的监听不重建");
    }
    finally
    {
      GuardianLoop.ForegroundListenerFactoryOverrideForTests = null;
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  [Fact]
  public void Listener_failure_degrades_observable_without_restart_storm_and_logs_once()
  {
    var created = 0;
    GuardianLoop.ForegroundListenerFactoryOverrideForTests = onSignal =>
    {
      created++;
      return new ForegroundEventListener(
        onSignal,
        setHook: _ => IntPtr.Zero,
        unhook: _ => { },
        pumpUntilStop: stop =>
        {
          stop.WaitOne();
          return true;
        });
    };
    var root = CreateTempRoot();
    try
    {
      var configPath = Path.Combine(root, "SmartGuard.config.json");
      WriteConfig(configPath, restrictionEnabled: true);
      var loop = CreateLoop(root, configPath);
      var pipeline = (LogPipeline)typeof(GuardianLoop).GetField("_logPipeline", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(loop)!;
      pipeline.Start();

      loop.SyncForegroundSubscription();
      created.Should().Be(1);

      var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
      while (loop.ForegroundListenerRunning && DateTime.UtcNow < deadline)
        Thread.Sleep(10);
      loop.ForegroundListenerRunning.Should().BeFalse("注册失败的监听线程退出");
      loop.ForegroundListenerHealthy.Should().BeFalse();
      loop.ForegroundListenerFailure.Should().NotBeNullOrEmpty();

      loop.SyncForegroundSubscription();
      loop.SyncForegroundSubscription();
      created.Should().Be(1, "故障路径不在高频路径重启监听");
      loop.ForegroundDegradationReported.Should().BeTrue("降级可观测（一次性标记）");

      pipeline.StopAsync().GetAwaiter().GetResult().Should().BeTrue();
      var config = GuardConfig.CreateDefault(root);
      var logPath = SmartGuardPaths.ResolveLogFile(config, root);
      var logLines = File.ReadAllLines(logPath, Encoding.UTF8);
      logLines.Count(line => line.Contains(EventNameRegistry.ForegroundListenerDegraded, StringComparison.Ordinal))
        .Should().Be(1, "监听失败只记一次降级日志");
    }
    finally
    {
      GuardianLoop.ForegroundListenerFactoryOverrideForTests = null;
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  [Fact]
  public void Loop_runner_foreground_read_gates_real_collection_on_restriction()
  {
    var reads = 0;
    GuardianLoop.ForegroundReaderFactoryOverrideForTests = () => new ForegroundProgramReader(
      getForegroundWindow: () => new IntPtr(0x7700),
      getWindowProcessId: delegate (IntPtr hwnd, out uint pid)
      {
        pid = 4321;
        return true;
      },
      openProcessLimited: delegate (uint pid, out IntPtr handle)
      {
        handle = new IntPtr(0x8800);
        return true;
      },
      queryProcessImageName: delegate (IntPtr handle, StringBuilder buffer, ref uint size)
      {
        reads++;
        size = (uint)NotepadPath.Length;
        _ = buffer.Append(NotepadPath);
        return true;
      },
      closeProcessHandle: _ => { });
    var root = CreateTempRoot();
    try
    {
      var configPath = Path.Combine(root, "SmartGuard.config.json");
      var loop = CreateLoop(root, configPath);

      WriteConfig(configPath, restrictionEnabled: false);
      loop.SyncForegroundSubscription();
      var restrictedOff = loop.ReadForegroundForRunner();
      restrictedOff.IdentificationSucceeded.Should().BeFalse("限制关闭 → 跳过采集");
      reads.Should().Be(0);

      WriteConfig(configPath, restrictionEnabled: true);
      loop.SyncForegroundSubscription();
      var restrictedOn = loop.ReadForegroundForRunner();
      restrictedOn.IdentificationSucceeded.Should().BeTrue();
      restrictedOn.ExecutablePath.Should().Be(NotepadPath);
      reads.Should().Be(1);
    }
    finally
    {
      GuardianLoop.ForegroundReaderFactoryOverrideForTests = null;
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  // ---- helpers ----

  private static string CreateTempRoot()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardForegroundGuard_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
  }

  private static GuardianLoop CreateLoop(string root, string configPath)
    => new(
      root,
      configPath,
      Path.Combine(root, "SmartGuard.status.json"),
      Path.Combine(root, ".SmartGuard.initialized"),
      Path.Combine(root, "SmartGuard.startup.log"),
      powerCfgBrightnessSupported: false);

  private static void WriteConfig(string configPath, bool restrictionEnabled)
  {
    File.WriteAllText(
      configPath,
      $$"""
      {
        "ForegroundRestrictionEnabled": {{(restrictionEnabled ? "true" : "false").ToLowerInvariant()}},
        "ForegroundProgramPaths": ["{{NotepadPath.Replace("\\", "\\\\")}}"]
      }
      """,
      new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    // GuardConfigRepository 按 LastWriteTimeUtc 缓存：同一时间戳刻度内的连续写入不可见，
    // 显式单调递增时间戳以保证后续 TryLoad 观察到新配置（同 GuardianIterationTimingTests 做法）。
    var previous = File.GetLastWriteTimeUtc(configPath);
    File.SetLastWriteTimeUtc(configPath, previous + TimeSpan.FromSeconds(1));
  }

  private static async Task WaitForAsync(Func<bool> condition)
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    while (!condition() && DateTime.UtcNow < deadline)
      await Task.Delay(10);
    condition().Should().BeTrue("等待的条件须在期限内成立");
  }

  /// <summary>可注入的 Win32 时序假件：模拟查询期间 HWND/PID 变化、进程退出、查询失败。</summary>
  private sealed class FakeWin32Seams
  {
    private int _pidCalls;

    public IntPtr InitialHwnd { get; init; } = new(0x1101);
    public IntPtr RecheckHwnd { get; init; }
    public uint InitialPid { get; init; } = 4242;
    public uint RecheckPid { get; init; }
    public bool FailOpenProcess { get; init; }
    public bool FailQuery { get; init; }
    public string ImagePath { get; init; } = @"C:\Windows\System32\notepad.exe";

    public int GetForegroundWindowCalls;
    public int OpenProcessCalls;
    public int CloseHandleCalls;
    public uint? BufferCapacitySeen;

    public ForegroundProgramReader CreateReader() => new(
      getForegroundWindow: () =>
      {
        GetForegroundWindowCalls++;
        return GetForegroundWindowCalls == 1
          ? InitialHwnd
          : (RecheckHwnd != IntPtr.Zero ? RecheckHwnd : InitialHwnd);
      },
      getWindowProcessId: delegate (IntPtr hwnd, out uint pid)
      {
        _pidCalls++;
        pid = _pidCalls == 1 ? InitialPid : (RecheckPid != 0 ? RecheckPid : InitialPid);
        return pid != 0 && hwnd != IntPtr.Zero;
      },
      openProcessLimited: delegate (uint pid, out IntPtr handle)
      {
        OpenProcessCalls++;
        if (FailOpenProcess)
        {
          handle = IntPtr.Zero;
          return false;
        }
        handle = new IntPtr(0x5555);
        return true;
      },
      queryProcessImageName: delegate (IntPtr handle, StringBuilder buffer, ref uint size)
      {
        BufferCapacitySeen = (uint)buffer.Capacity;
        if (FailQuery || ImagePath.Length == 0)
          return false;
        size = (uint)ImagePath.Length;
        _ = buffer.Append(ImagePath);
        return true;
      },
      closeProcessHandle: _ => CloseHandleCalls++);
  }
}

/// <summary>
/// 前台守护行为测试台架：镜像 GuardianLoop 周期体的组件组合（生命周期状态机 +
/// 协调器 + 真实 GuardianIterationRunner 合成委托），时间使用单调假时钟，
/// 前台事实与节流参数全部可注入；不睡眠、不触碰真实电源计划。
/// </summary>
internal sealed class ForegroundGuardHarness : IDisposable
{
  public const int IntervalSeconds = 15;

  private readonly ManualTimeProvider _time = new();
  private readonly PowerLifecycleStateMachine _lifecycle;
  private readonly GuardianLoopIterationState _state = new();
  private readonly GuardConfig _config;
  private readonly GuardianCycleCoordinator _coordinator;
  private readonly SemaphoreSlim _wakeSignal = new(0, int.MaxValue);
  private readonly string _root;
  private Guid _activePlan = PowerPlanCatalogProvider.HighPerformancePlanGuid;
  private uint _apiIdle = 10;
  private string? _foregroundPath;
  private bool _configLoadThrows;
  private int _powerWake;
  private int _foregroundReads;

  private ForegroundGuardHarness(TimeSpan? lightRecheckFloor, TimeSpan? fullEvaluationFloor)
  {
    _root = Path.Combine(Path.GetTempPath(), "SmartGuardForegroundHarness_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
    _config = GuardConfig.CreateDefault(_root);
    _config.ForegroundRestrictionEnabled = true;
    _config.ForegroundProgramPaths = [ForegroundGuardIntegrationTests.NotepadPath];
    _config.BrightnessRestoreMs = 0;
    _config.HeartbeatIntervalMin = 0;
    _lifecycle = new PowerLifecycleStateMachine(_time);
    _state.PlanCatalog = new Dictionary<Guid, string>
    {
      [PowerPlanCatalogProvider.HighPerformancePlanGuid] = PowerPlanCatalogProvider.HighPerformanceDisplayName,
      [PowerPlanCatalogProvider.BalancedPlanGuid] = PowerPlanCatalogProvider.BalancedDisplayName,
      [PowerPlanCatalogProvider.PowerSaverPlanGuid] = PowerPlanCatalogProvider.PowerSaverDisplayName,
    };
    _state.LastKnownGuid = _activePlan;

    Func<GuardConfigReadResult> loadConfig = () =>
    {
      if (_configLoadThrows)
        throw new IOException("config read failed");
      return GuardConfigReadResult.FromConfig(_config);
    };
    Func<ForegroundObservation> readForeground = () =>
    {
      Interlocked.Increment(ref _foregroundReads);
      return _foregroundPath is null
        ? ForegroundObservation.Unknown
        : ForegroundObservation.Identified(_foregroundPath);
    };
    var runner = new GuardianIterationRunner(
      new IdleTracker(_time),
      new BrightnessService(),
      new StatusPublisher(Path.Combine(_root, "SmartGuard.status.json"), _time),
      loadConfig,
      (config, level, message, eventName) =>
      {
        Events.Add(eventName);
        return GuardianLogWriteResult.AcceptedByPipeline;
      },
      _ => { },
      powerCfgBrightnessSupported: false,
      heartbeatSchedule: new HeartbeatSchedule(_time),
      readIdleSeconds: () => _apiIdle,
      readBatteryInfo: _ => (70, true),
      readCurrentPlan: () => new PowerPlanProbeResult(
        true,
        new PowerSchemeInfo(_activePlan, null),
        PowerPlanFailureKind.None,
        string.Empty),
      applyPlan: (target, cancellationToken) =>
      {
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
      readForeground: readForeground);

    _coordinator = new GuardianCycleCoordinator(
      loadConfig,
      readForeground,
      _state,
      _wakeSignal,
      async (context, cancellationToken) =>
      {
        FullRunnerRuns++;
        await runner.RunAsync(_state, context, cancellationToken);
      },
      _time,
      consumePowerWake: () => Interlocked.Exchange(ref _powerWake, 0) != 0,
      lightRecheckFloor: lightRecheckFloor,
      fullEvaluationFloor: fullEvaluationFloor);
  }

  public int FullRunnerRuns { get; private set; }
  public List<string> Events { get; } = [];
  public List<Guid> AppliedPlans { get; } = [];
  public int ForegroundReads => _foregroundReads;
  public int LightReads => _foregroundReads - FullRunnerRuns;
  public bool? LastAdmitted => _state.LastForegroundAdmitted;
  public bool HasPendingForegroundSignal => _coordinator.HasPendingForegroundSignal;
  public string StatusContent => File.ReadAllText(StatusPath);

  private string StatusPath => Path.Combine(_root, "SmartGuard.status.json");

  public static ForegroundGuardHarness Create(
    TimeSpan? lightRecheckFloor = null,
    TimeSpan? fullEvaluationFloor = null)
    => new(lightRecheckFloor, fullEvaluationFloor);

  /// <summary>镜像 GuardianLoop.RunAsync 的周期体：TakeDirective → RunCycleAsync（间隔 15 秒）。</summary>
  public Task RunCycleAsync()
  {
    var directive = _lifecycle.TakeDirective();
    return _coordinator.RunCycleAsync(directive, IntervalSeconds, CancellationToken.None);
  }

  /// <summary>模拟监听回调：前台变化信号（0→1 合并 + 唤醒释放发生在协调器内）。</summary>
  public void ForegroundChanged() => _coordinator.MarkForegroundSignal();

  /// <summary>镜像 GuardianLoop.HandlePowerEvent：Observe + 电源唤醒标记。</summary>
  public void Signal(PowerLifecycleKind kind)
  {
    _lifecycle.Observe(new PowerLifecycleSignal(kind, _time.GetUtcNow()));
    Interlocked.Exchange(ref _powerWake, 1);
  }

  public int DrainWakeSignalReleases()
  {
    var releases = 0;
    while (_wakeSignal.Wait(0))
      releases++;
    return releases;
  }

  public void SetForeground(string path) => _foregroundPath = path;
  public void SetIdle(uint idle) => _apiIdle = idle;
  public void BreakConfigLoad() => _configLoadThrows = true;
  public void Advance(TimeSpan value) => _time.Advance(value);
  public void NoteCycleFailure(int intervalSeconds) => _coordinator.NoteCycleFailure(intervalSeconds);
  public TimeSpan ComputeWaitDelay() => _coordinator.ComputeWaitDelay();

  public void Dispose()
  {
    _wakeSignal.Dispose();
    try { Directory.Delete(_root, recursive: true); } catch { }
  }
}
