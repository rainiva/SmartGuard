# SmartGuard 电源生命周期可靠性修复 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让空闲时间、三档电源计划和心跳日志在正常运行、合盖、现代待机、睡眠恢复、系统时间调整及日志降级场景下保持一致、可解释、可验证。

**Architecture:** 新增一个 Engine 内部的 `PowerLifecycleStateMachine`，作为 `Running / Suspending / ResumeStabilizing` 生命周期事实唯一 Owner；`PowerEventWakeListener` 只传递有类型的 Windows 电源模式信号，不再把 Suspend/Resume 伪装成电池/插电。现有 `PolicyEngine` 继续作为三档计划纯决策真源；`IdleTracker`、计划执行、心跳调度和状态发布只消费生命周期指令，不各自猜测睡眠。

**Tech Stack:** .NET 8、C#、Windows `Microsoft.Win32.SystemEvents`、`TimeProvider` 单调时间、xUnit、FluentAssertions、Pester、Task Scheduler、Windows System Event Log。

## Global Constraints

- 行为改动严格 TDD：每个 Task 先保存 RED 失败证据，再写最小生产实现，GREEN 后才重构。
- 不新增配置字段、Feature Flag、第二套计划策略、第二套空闲计算、第二套日志管线或常驻 Watcher。
- `PolicyEngine` 仍是三档计划选择唯一真源；生命周期层只决定“本轮是否允许计划仲裁”，不得复制阈值规则。
- Suspend/Resume/StatusChange 不得再编码成 `bool isOnAc`；供电事实仍只由 `BatteryInfoProvider` 读取。
- 进程内持续时间使用注入的 `TimeProvider.GetTimestamp/GetElapsedTime`；持久化时间使用 UTC `DateTimeOffset`；显示层才转本地时间。
- Suspend 期间禁止 `powercfg /setactive`；Resume 后必须先重建空闲基线、吸收 OS 当前计划，再允许下一次普通策略仲裁。
- 心跳在挂起期间不补写历史周期；恢复后只写一条明确的恢复/心跳事件。一次投递失败不得推进下次心跳窗口。
- 状态文件最多 60 秒刷新一次，最长也不得超过 90 秒不可解释地陈旧；不恢复每 15 秒全量重写。
- 所有队列、等待、重试和进程执行保持现有容量或时间上限；不得新增无界并发、轮询或 `Thread.Sleep`。
- 生命周期转换只进入状态机内部的有界队列（容量 8）；相邻重复信号合并，溢出必须发出结构化告警并保留最新状态，禁止静默丢弃。
- `Suspending` 不得成为永久停工态：30 秒内未观察到 Resume 时，由同一状态机进入受控恢复稳定期，重建 idle/plan 基线并记录降级事件；不得另建常驻 watchdog。
- 不触碰与本计划无关的未跟踪 `.work/`、`tasks/`、`.superpowers/` 材料；每次提交只暂存本 Task 文件。
- 自动化通过不等于真实现代待机验收。缺少合盖/Modern Standby/UAC 条件时必须报告 `NOT RUN` 或 `BLOCKED`。

---

## 已确认基线与被破坏的不变量

### 现场证据

- 2026-08-22 13:43:59 Windows 因 Lid 进入 Modern Standby；13:44:03 Engine 把 OS 切到平衡误判为 `EXTERNAL`，13:44:04 又切回高性能。
- 14:51:38 Windows 恢复；14:51:54 才恢复心跳，14:53:05 才恢复管线健康样本。该空档中 `droppedNormal=0`、`droppedHigh=0`、`failedBatches=0`，反证日志队列吞写。
- 14:57:40 Windows 明确由 Mouse 唤醒；14:57:48 Engine 仍判定空闲并切平衡，15:01:55 才恢复活跃。
- 正常心跳间隔为 10.02–10.15 分钟；异常空档与 Windows 挂起区间一致。
- `SmartGuard.status.json` 可停留在 22:04:44，而主日志继续写到 22:16，说明状态发布的去重策略会制造“Engine 停住”的错觉。

### 根因

1. `PowerEventInterpreter` 把 `Resume => true`、`Suspend => false`，把生命周期错误压缩成供电布尔值。
2. `PowerEventWakeListener` 对三种 `PowerModes` 一律触发普通 Engine 迭代，导致挂起边界执行计划纠偏。
3. `IdleTracker` 用墙上时间补偿睡眠，并在长墙钟差下把 API idle 的下降当成睡眠回跳，鼠标唤醒可能不被重置。
4. 心跳是普通迭代的一部分，但日志没有 Suspend/Resume 语义，正常暂停看起来像漏写。
5. `StatusPublisher` 忽略正常 idle 增长、timestamp 和 operationId，状态新鲜度没有上限。
6. `PowerCfgExecutor` 对部分失败返回空字符串，计划执行后未以重新探测到目标 GUID 作为成功真相。

### 修复后不变量

- `PowerLifecycleStateMachine` 是挂起/恢复阶段唯一真源。
- `BatteryInfoProvider` 是 AC/DC 与电量唯一真源。
- `IdleTracker` 只累计 awake epoch 内的单调持续时间；Resume 建立新 epoch，睡眠时长不进入三档空闲阈值。
- `PolicyEngine` 只接收可靠 idle/battery 输入，不感知 Windows 事件，不拥有生命周期。
- 计划切换成功以 `setactive` 成功且随后 `getactivescheme == target` 为准。
- 心跳“成功”以进入异步管线或同步降级链被接受为准；被背压/拒绝时下一轮继续尝试。
- 状态快照写入采用“有意义变化或 60 秒新鲜度上限”，timestamp 使用 UTC offset 格式。
- Suspend/Resume 即使在主循环暂停期间连续到达，也必须按观察顺序保留并各记录一次；日志写入时间不得冒充 OS 信号观察时间。
- Resume 信号缺失时，Engine 最迟在恢复执行后的一个普通轮询周期内进入受控恢复路径；恢复路径先重建基线，不得直接恢复计划写入。

## Change Impact Matrix

| 影响域 | 结论与约束 |
|---|---|
| Architecture | 高影响：新增生命周期 Owner，必须先更新 `ARCHITECTURE-CONTRACT.md` 和架构门禁。 |
| Reliability | 核心：覆盖挂起中止、恢复重建、重复信号、漏信号、探测失败、写入背压和进程重启。 |
| Performance | 事件驱动；不提高 15 秒轮询频率。状态文件最多每分钟一次保底写，额外 IO 有界。 |
| Security | 不改变权限、UAC、任务 Principal、日志敏感字段和诊断导出范围。 |
| Compatibility | 不新增配置字段；旧配置和旧 status reader 可继续工作。 |
| Operations | 新增真实待机验收回执；自动化不能替代 Lid/Mouse/AC/DC 操作。 |
| Rollback | 每 Task 独立提交；无配置迁移，按逆序 `git revert` 可回到旧行为。 |

## 文件结构与责任锁定

| 文件 | 动作 | 单一责任 |
|---|---|---|
| `src/SmartGuard.Engine/Infrastructure/PowerLifecycleSignal.cs` | Create | Windows 电源模式的领域化有类型信号。 |
| `src/SmartGuard.Engine/Infrastructure/PowerLifecycleStateMachine.cs` | Create | 生命周期阶段、有界转换批次、恢复稳定期和漏 Resume 自恢复唯一 Owner。 |
| `src/SmartGuard.Engine/Infrastructure/PowerEventWakeListener.cs` | Modify | 只订阅/退订并转发 typed signal；事件线程不做 IO。 |
| `src/SmartGuard.Engine/Infrastructure/PowerEventInterpreter.cs` | Delete | 删除错误的 Resume/AC、Suspend/DC 映射。 |
| `src/SmartGuard.Engine/Infrastructure/PowerEventStateResolver.cs` | Delete | 删除生命周期到供电布尔值的压缩层。 |
| `src/SmartGuard.Engine/Infrastructure/IdleTracker.cs` | Modify | 单调 awake-idle 累计和 Resume 重建。 |
| `src/SmartGuard.Engine/Worker/GuardianIterationContext.cs` | Create | 向单轮执行传递生命周期许可，不包含策略规则。 |
| `src/SmartGuard.Engine/Worker/GuardianLoop.cs` | Modify | 消费生命周期指令、跳过挂起期仲裁、触发恢复迭代。 |
| `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs` | Modify | 依 context 采样/仲裁/发布，正确标注事件名。 |
| `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs` | Modify | `powercfg` 进程结果唯一真源：exit/stdout/stderr/timeout。 |
| `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs` | Modify | 解析、执行、验证计划操作，不吞失败。 |
| `src/SmartGuard.Engine/Infrastructure/HeartbeatSchedule.cs` | Create | 单调心跳到期和仅成功推进窗口。 |
| `src/SmartGuard.Engine/Infrastructure/HeartLogSampler.cs` | Delete | 删除和心跳调度重叠的第二窗口。 |
| `src/SmartGuard.Engine/Worker/GuardianLoop.Logging.cs` | Modify | 返回明确写入结果；降级链结果可观察。 |
| `src/SmartGuard.Engine/Infrastructure/EngineLogEvents.cs` | Modify | 构造携带原始观察时间的 lifecycle 结构化事件；不改变通用日志 schema。 |
| `src/SmartGuard.Configuration/LogFallbackChain.cs` | Modify | 返回最终是否有一层接受事件，仍永不抛出。 |
| `src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs` | Modify | 有意义变化 + 单调新鲜度上限 + 无删除窗口替换。 |
| `src/SmartGuard.Configuration/EventNameRegistry.cs` | Modify | 登记 Suspend/Resume/plan-apply-failed 事件。 |
| `src/SmartGuard.Configuration/LogEventChannels.cs` | Modify | 新事件进入 Lifecycle/Power 正确 channel。 |
| `Tests/SmartGuard.Engine.Tests/*` | Modify/Create | 纯状态机、空闲、执行结果、心跳、状态发布和组合序列。 |
| `Tests/SmartGuard.Architecture.Tests/PowerLifecycleArchitectureTests.cs` | Create | 防止再次把生命周期压成 bool 或在 Suspend 期写计划。 |
| `scripts/Invoke-PowerLifecycleAcceptance.ps1` | Create | 真实待机观察、结构化结果、transcript 和退出码。 |
| `docs/ARCHITECTURE-CONTRACT.md` | Modify | 登记 Owner、不变量、门禁和真实验收。 |

---

### Task 1: 先锁定架构契约和反回归门禁

**Files:**
- Create: `Tests/SmartGuard.Architecture.Tests/PowerLifecycleArchitectureTests.cs`
- Modify: `docs/ARCHITECTURE-CONTRACT.md`

**Interfaces:**
- Consumes: 当前 `GuardianLoop`、`PowerEventWakeListener`、`PolicyEngine` 源码。
- Produces: 后续 Task 必须满足的 Owner、禁止清单和源代码门禁。

- [ ] **Step 1: 写 RED 架构测试**

```csharp
using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class PowerLifecycleArchitectureTests
{
  [Fact]
  public void Contract_registers_single_power_lifecycle_owner()
  {
    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    contract.Should().Contain("PowerLifecycleStateMachine");
    contract.Should().Contain("Suspend 期间禁止计划仲裁");
    contract.Should().Contain("Resume 建立新的 awake-idle epoch");
  }
}
```

- [ ] **Step 2: 运行 RED 并保存证据**

```powershell
dotnet test .\Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerLifecycleArchitectureTests" --logger "trx;LogFileName=power-lifecycle-architecture-red.trx"
```

Expected: FAIL；缺少 `PowerLifecycleStateMachine`、Suspend/Resume 不变量契约行。把 TRX 复制到 `.work/test-results/power-lifecycle/task-01-red.trx`，不得覆盖。

- [ ] **Step 3: 最小更新架构契约**

在真源注册表加入：

```markdown
| 电源生命周期 | `PowerLifecycleStateMachine`（Engine 进程内） | `PowerEventWakeListener` typed signal | `GuardianLoop` 消费一次性 directive | Suspend/Resume 压缩为 AC/DC；UI/PolicyEngine 自建生命周期 |
| Awake 空闲时间 | `IdleTracker` + `TimeProvider` | `GuardianIterationRunner` | — | 睡眠墙钟直接进入 300/900 秒阈值；Settings 重复计算策略 idle |
```

在稳定不变量章节加入：

```markdown
| PWR-LIFE-OWNER | 阻断 | `PowerLifecycleStateMachine` 是 Running/Suspending/ResumeStabilizing 唯一 Owner；`PolicyEngine` 不拥有生命周期。 |
| PWR-LIFE-SUSPEND | 阻断 | Suspend 期间禁止计划仲裁、`powercfg /setactive` 和外部计划变更判定。 |
| PWR-LIFE-RESUME | 阻断 | Resume 建立新的 awake-idle epoch；先吸收 OS 当前计划并完成恢复稳定期，再允许普通策略仲裁。 |
| PWR-LIFE-TIME | 阻断 | awake 持续时间和心跳间隔只使用注入的 `TimeProvider` 单调时间；持久化时间为 UTC offset。 |
| PWR-LIFE-VERIFY | 阻断 | 单元/组合测试通过不等于真实待机验收；Lid/Mouse/AC/DC 未执行时标记 NOT RUN 或 BLOCKED。 |
```

- [ ] **Step 4: 确认契约门禁 GREEN**

Run 同 Step 2。Expected: 失败 0。Task 2 才新增源码门禁并完成对应生产实现；禁止向主线提交故意失败的测试。

- [ ] **Step 5: 提交契约与已转绿门禁**

```powershell
git add docs/ARCHITECTURE-CONTRACT.md Tests/SmartGuard.Architecture.Tests/PowerLifecycleArchitectureTests.cs
git commit -m "test(engine): 锁定电源生命周期架构契约"
```

---

### Task 2: 用 typed signal 和状态机替代 AC/DC 伪映射

**Files:**
- Create: `src/SmartGuard.Engine/Infrastructure/PowerLifecycleSignal.cs`
- Create: `src/SmartGuard.Engine/Infrastructure/PowerLifecycleStateMachine.cs`
- Create: `Tests/SmartGuard.Engine.Tests/PowerLifecycleStateMachineTests.cs`
- Create: `Tests/SmartGuard.Engine.Tests/ManualTimeProvider.cs`
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerEventWakeListener.cs`
- Modify: `Tests/SmartGuard.Engine.Tests/PowerEventWakeListenerTests.cs`
- Modify: `Tests/SmartGuard.Architecture.Tests/PowerLifecycleArchitectureTests.cs`
- Delete: `src/SmartGuard.Engine/Infrastructure/PowerEventInterpreter.cs`
- Delete: `src/SmartGuard.Engine/Infrastructure/PowerEventStateResolver.cs`
- Delete: `Tests/SmartGuard.Engine.Tests/PowerEventInterpreterTests.cs`
- Delete: `Tests/SmartGuard.Engine.Tests/PowerEventStateResolverTests.cs`

**Interfaces:**
- Produces: `PowerLifecycleSignal`、`PowerLifecycleKind`、`PowerLifecycleStateMachine.Observe`、`TakeDirective`。
- Invariant: SystemEvents 回调只做内存状态变更和 semaphore release，不读电池、不写文件、不执行 powercfg。

- [ ] **Step 1: 写状态机 RED 测试**

```csharp
[Fact]
public void Suspend_blocks_policy_until_resume_stabilization_finishes()
{
  var time = new ManualTimeProvider();
  var machine = new PowerLifecycleStateMachine(time, TimeSpan.FromSeconds(5));

  machine.Observe(new(PowerLifecycleKind.Suspending, time.GetUtcNow()));
  machine.TakeDirective().AllowPolicyMutation.Should().BeFalse();

  machine.Observe(new(PowerLifecycleKind.Resumed, time.GetUtcNow()));
  var first = machine.TakeDirective();
  first.ResetIdleEpoch.Should().BeTrue();
  first.AbsorbCurrentPlanAsBaseline.Should().BeTrue();
  first.AllowPolicyMutation.Should().BeFalse();

  time.Advance(TimeSpan.FromSeconds(5));
  machine.TakeDirective().AllowPolicyMutation.Should().BeTrue();
}

[Fact]
public void Duplicate_resume_does_not_repeat_reset_after_first_directive()
{
  var time = new ManualTimeProvider();
  var machine = new PowerLifecycleStateMachine(time, TimeSpan.FromSeconds(5));
  var signal = new PowerLifecycleSignal(PowerLifecycleKind.Resumed, time.GetUtcNow());

  machine.Observe(signal);
  machine.Observe(signal);

  machine.TakeDirective().ResetIdleEpoch.Should().BeTrue();
  machine.TakeDirective().ResetIdleEpoch.Should().BeFalse();
}

[Fact]
public void Suspend_and_resume_observed_before_consumption_are_preserved_in_order()
{
  var time = new ManualTimeProvider();
  var machine = new PowerLifecycleStateMachine(time, TimeSpan.FromSeconds(5));

  machine.Observe(new(PowerLifecycleKind.Suspending, time.GetUtcNow()));
  time.Advance(TimeSpan.FromHours(1));
  machine.Observe(new(PowerLifecycleKind.Resumed, time.GetUtcNow()));

  machine.TakeDirective().TransitionsToLog.Select(x => x.Kind)
    .Should().Equal(PowerLifecycleKind.Suspending, PowerLifecycleKind.Resumed);
}

[Fact]
public void Missing_resume_enters_bounded_recovery_without_enabling_policy_immediately()
{
  var time = new ManualTimeProvider();
  var machine = new PowerLifecycleStateMachine(time, TimeSpan.FromSeconds(5));
  machine.Observe(new(PowerLifecycleKind.Suspending, time.GetUtcNow()));
  machine.TakeDirective().AllowPolicyMutation.Should().BeFalse();

  time.Advance(TimeSpan.FromSeconds(30));
  var recovery = machine.TakeDirective();

  recovery.RecoveryForced.Should().BeTrue();
  recovery.ResetIdleEpoch.Should().BeTrue();
  recovery.AbsorbCurrentPlanAsBaseline.Should().BeTrue();
  recovery.AllowPolicyMutation.Should().BeFalse();
  recovery.Phase.Should().Be(PowerLifecyclePhase.ResumeStabilizing);
}

[Fact]
public void Late_resume_after_forced_recovery_is_logged_without_repeating_reset()
{
  var time = new ManualTimeProvider();
  var machine = new PowerLifecycleStateMachine(time, TimeSpan.FromSeconds(5));
  machine.Observe(new(PowerLifecycleKind.Suspending, time.GetUtcNow()));
  machine.TakeDirective();
  time.Advance(TimeSpan.FromSeconds(30));
  machine.TakeDirective().ResetIdleEpoch.Should().BeTrue();

  machine.Observe(new(PowerLifecycleKind.Resumed, time.GetUtcNow()));
  var lateResume = machine.TakeDirective();

  lateResume.TransitionsToLog.Should().ContainSingle(x =>
    x.Kind == PowerLifecycleKind.Resumed);
  lateResume.ResetIdleEpoch.Should().BeFalse();
  lateResume.AbsorbCurrentPlanAsBaseline.Should().BeFalse();
  lateResume.AllowPolicyMutation.Should().BeFalse();
}
```

同时向 `PowerLifecycleArchitectureTests` 新增源码门禁：

```csharp
[Fact]
public void Power_events_are_not_collapsed_into_ac_state()
{
  SourceScanHelper.ReadSource("src/SmartGuard.Engine/Infrastructure/PowerEventWakeListener.cs")
    .Should().Contain("PowerLifecycleSignal");
  File.Exists(Path.Combine(
    SourceScanHelper.RepoRoot,
    "src/SmartGuard.Engine/Infrastructure/PowerEventStateResolver.cs")).Should().BeFalse();
}

[Fact]
public void Policy_engine_remains_the_only_three_tier_rule_owner()
{
  var lifecycle = SourceScanHelper.ReadSource(
    "src/SmartGuard.Engine/Infrastructure/PowerLifecycleStateMachine.cs");
  lifecycle.Should().NotContain("BalancedThresholdSec");
  lifecycle.Should().NotContain("PowerSaverThresholdSec");
  lifecycle.Should().NotContain("ActivePlanGuid");
}
```

测试项目新建一个共享确定性时钟 `Tests/SmartGuard.Engine.Tests/ManualTimeProvider.cs`：

```csharp
namespace SmartGuard.Engine.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
  private DateTimeOffset _utcNow = new(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);
  private long _timestamp;
  public override DateTimeOffset GetUtcNow() => _utcNow;
  public override long GetTimestamp() => _timestamp;
  public override long TimestampFrequency => TimeSpan.TicksPerSecond;

  public void Advance(TimeSpan value)
  {
    _utcNow += value;
    _timestamp += value.Ticks;
  }

  public void AdvanceTimestamp(TimeSpan value) => _timestamp += value.Ticks;
  public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
}
```

- [ ] **Step 2: 运行 RED**

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerLifecycleStateMachineTests|FullyQualifiedName~PowerEventWakeListenerTests" --logger "trx;LogFileName=power-lifecycle-state-red.trx"
```

Expected: FAIL with CS0246，typed signal/state machine 尚不存在。

- [ ] **Step 3: 新建明确类型和 directive**

```csharp
namespace SmartGuard.Engine.Infrastructure;

internal enum PowerLifecycleKind
{
  StatusChanged,
  Suspending,
  Resumed,
}

internal readonly record struct PowerLifecycleSignal(
  PowerLifecycleKind Kind,
  DateTimeOffset ObservedAtUtc);

internal enum PowerLifecyclePhase
{
  Running,
  Suspending,
  ResumeStabilizing,
}

internal readonly record struct PowerLifecycleDirective(
  PowerLifecyclePhase Phase,
  bool RunIteration,
  bool AllowPolicyMutation,
  bool ResetIdleEpoch,
  bool ForcePowerRefresh,
  bool AbsorbCurrentPlanAsBaseline,
  IReadOnlyList<PowerLifecycleSignal> TransitionsToLog,
  bool RecoveryForced,
  bool TransitionOverflowed,
  TimeSpan? ReevaluateAfter);
```

`PowerLifecycleStateMachine` 必须用 `lock` 保护 SystemEvents 线程与主循环之间的状态：

```csharp
public void Observe(PowerLifecycleSignal signal)
{
  lock (_gate)
  {
    switch (signal.Kind)
    {
      case PowerLifecycleKind.Suspending:
        EnqueueTransition(signal);
        _phase = PowerLifecyclePhase.Suspending;
        _suspendAtTimestamp = _timeProvider.GetTimestamp();
        break;
      case PowerLifecycleKind.Resumed:
        EnqueueTransition(signal);
        _phase = PowerLifecyclePhase.ResumeStabilizing;
        _resumeAtTimestamp = _timeProvider.GetTimestamp();
        _resetIdlePending = true;
        _baselinePending = true;
        _forcePowerRefreshPending = true;
        break;
      case PowerLifecycleKind.StatusChanged:
        _forcePowerRefreshPending = true;
        break;
    }
  }
}
```

`PowerLifecycleStateMachine` 与上述类型保持 `internal`，不扩大 Engine 程序集的公共 API。内部转换队列容量固定为 8；相邻同类信号合并。容量耗尽时丢弃最旧项、保留最新项并置 `TransitionOverflowed=true`，由主循环写 `PowerLifecycleSignalOverflow`，不得安静覆盖。

`TakeDirective` 一次性消费 reset/baseline/force/transition 批次；批次按 `ObservedAtUtc` 和入队顺序保序。`Suspending` 返回 `RunIteration=false`，同时给出距 30 秒自恢复上限的 `ReevaluateAfter`。若到期仍未收到 Resume，同一个状态机进入 `ResumeStabilizing`，设置 `RecoveryForced=true`、reset/baseline/force 三个标记，并写 `PowerLifecycleRecoveryForced`；它不伪造 `PowerResumeObserved`。若真实 Resume 随后迟到，仍保留并记录该观察事件、重新起算稳定窗口并强制刷新电源，但不得第二次 reset/baseline。恢复稳定期满 5 秒后只把 Phase 变为 Running，不再重复 reset。

这条自恢复使用状态机已有 `TimeProvider` 和主循环已有等待，不新增 timer、线程或第二 Owner。进程真正挂起时不会执行；进程恢复执行但 Resume 丢失时，首次 `TakeDirective` 即收敛到安全恢复路径。

- [ ] **Step 4: 改造监听器，事件线程不再读取电池**

```csharp
private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
{
  var kind = e.Mode switch
  {
    PowerModes.Suspend => PowerLifecycleKind.Suspending,
    PowerModes.Resume => PowerLifecycleKind.Resumed,
    _ => PowerLifecycleKind.StatusChanged,
  };
  _onPowerChanged(new PowerLifecycleSignal(kind, _timeProvider.GetUtcNow()));
}
```

构造函数改为接收 `Action<PowerLifecycleSignal>` 和可选 `TimeProvider`，生产默认 `TimeProvider.System`。删除两个 bool 解释器及其旧测试。

- [ ] **Step 5: 运行 GREEN 和架构门禁**

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerLifecycleStateMachineTests|FullyQualifiedName~PowerEventWakeListenerTests"
dotnet test .\Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerLifecycleArchitectureTests"
```

Expected: 两个命令失败数均为 0。

- [ ] **Step 6: 提交**

```powershell
git add src/SmartGuard.Engine/Infrastructure/PowerLifecycleSignal.cs src/SmartGuard.Engine/Infrastructure/PowerLifecycleStateMachine.cs src/SmartGuard.Engine/Infrastructure/PowerEventWakeListener.cs src/SmartGuard.Engine/Infrastructure/PowerEventInterpreter.cs src/SmartGuard.Engine/Infrastructure/PowerEventStateResolver.cs Tests/SmartGuard.Engine.Tests/ManualTimeProvider.cs Tests/SmartGuard.Engine.Tests/PowerLifecycleStateMachineTests.cs Tests/SmartGuard.Engine.Tests/PowerEventWakeListenerTests.cs Tests/SmartGuard.Engine.Tests/PowerEventInterpreterTests.cs Tests/SmartGuard.Engine.Tests/PowerEventStateResolverTests.cs Tests/SmartGuard.Architecture.Tests/PowerLifecycleArchitectureTests.cs
git commit -m "fix(engine): 建立电源生命周期状态机"
```

---

### Task 3: 把空闲计时改为单调 awake epoch

**Files:**
- Modify: `src/SmartGuard.Engine/Infrastructure/IdleTracker.cs`
- Modify: `Tests/SmartGuard.Engine.Tests/IdleTrackerTests.cs`

**Interfaces:**
- Produces: `IdleTracker.Sample(Func<uint>)`、`ResetAfterResume()`。
- Consumes: `TimeProvider`；Resume reset 只由生命周期 directive 触发。

- [ ] **Step 1: 写现场复现 RED 测试**

```csharp
[Fact]
public void Mouse_wake_sampled_eight_seconds_later_starts_a_new_awake_epoch()
{
  var time = new ManualTimeProvider();
  var tracker = new IdleTracker(time);
  tracker.Sample(() => 295).Should().Be(295);
  time.Advance(TimeSpan.FromMinutes(1));
  tracker.ResetAfterResume();
  time.Advance(TimeSpan.FromSeconds(8));
  tracker.Sample(() => 8).Should().Be(0);
  time.Advance(TimeSpan.FromSeconds(15));
  tracker.Sample(() => 23).Should().BeInRange(15u, 23u);
}

[Fact]
public void Awake_idle_uses_monotonic_elapsed_time_and_ignores_utc_clock_changes()
{
  var time = new ManualTimeProvider();
  var tracker = new IdleTracker(time);
  tracker.Sample(() => 10).Should().Be(10);
  time.AdvanceTimestamp(TimeSpan.FromSeconds(30));
  time.SetUtcNow(time.GetUtcNow().AddHours(-8));
  tracker.Sample(() => 40).Should().Be(40);
}
```

- [ ] **Step 2: 运行 RED**

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~IdleTrackerTests" --logger "trx;LogFileName=idle-awake-epoch-red.trx"
```

Expected: FAIL；旧 API 仍接收 `DateTime`，且会保留睡眠墙钟。

- [ ] **Step 3: 最小实现单调累计**

```csharp
public uint Sample(Func<uint> readApiIdle)
{
  var apiIdle = readApiIdle();
  var now = _timeProvider.GetTimestamp();
  if (!_seeded)
  {
    _effectiveIdleSeconds = _resumeResetPending ? 0u : apiIdle;
    _resumeResetPending = false;
    _seeded = true;
  }
  else
  {
    var elapsed = ToWholeSeconds(_timeProvider.GetElapsedTime(_lastSampleTimestamp, now));
    _effectiveIdleSeconds = IsUserActivity(apiIdle, _previousApiIdle)
      ? apiIdle
      : Math.Max(apiIdle, SaturatingAdd(_effectiveIdleSeconds, elapsed));
  }

  _previousApiIdle = apiIdle;
  _lastSampleTimestamp = now;
  return _effectiveIdleSeconds;
}

public void ResetAfterResume()
{
  _seeded = false;
  _resumeResetPending = true;
}
```

`IsUserActivity` 保留 `apiIdle <= 2 || apiIdle + 5 < previousApiIdle`；删除 `SleepRecoverySlackSeconds` 和墙钟睡眠推断。`SaturatingAdd` 必须在 `uint.MaxValue` 饱和。

- [ ] **Step 4: GREEN、PolicyEngine 回归并提交**

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~IdleTrackerTests|FullyQualifiedName~PolicyEngineTests"
git add src/SmartGuard.Engine/Infrastructure/IdleTracker.cs Tests/SmartGuard.Engine.Tests/IdleTrackerTests.cs
git commit -m "fix(engine): 按 awake epoch 计算空闲时间"
```

Expected: 失败 0；300/900 秒边界保持通过。

---

### Task 4: 把生命周期指令接入主循环和三档仲裁

**Files:**
- Create: `src/SmartGuard.Engine/Worker/GuardianIterationContext.cs`
- Create: `Tests/SmartGuard.Engine.Tests/GuardianPowerLifecycleIntegrationTests.cs`
- Modify: `src/SmartGuard.Engine/Worker/GuardianLoop.cs`
- Modify: `src/SmartGuard.Engine/Worker/GuardianLoop.Logging.cs`
- Modify: `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs`
- Modify: `src/SmartGuard.Engine/Infrastructure/EngineLogEvents.cs`
- Modify: `src/SmartGuard.Configuration/EventNameRegistry.cs`
- Modify: `src/SmartGuard.Configuration/LogEventChannels.cs`
- Modify: `src/SmartGuard.Configuration/GuardianIterationTiming.cs`
- Modify: `Tests/SmartGuard.Engine.Tests/GuardianIterationTimingTests.cs`
- Modify: `Tests/SmartGuard.Architecture.Tests/GuardianIterationRunnerArchitectureTests.cs`

**Interfaces:**
- Consumes: Task 2 `PowerLifecycleDirective`、Task 3 `ResetAfterResume()`。
- Produces: `GuardianIterationContext`；`PolicyEngine` 仍接收原参数。

- [ ] **Step 1: 写组合序列 RED 测试**

```csharp
[Fact]
public async Task Suspend_os_balance_mouse_resume_does_not_fight_os_or_preserve_sleep_idle()
{
  var harness = GuardianLifecycleHarness.Create(activePlan: Plans.HighPerformance, apiIdle: 290);
  harness.Signal(PowerLifecycleKind.Suspending);
  harness.SetOsPlan(Plans.Balanced);
  await harness.RunSignaledIterationAsync();
  harness.AppliedPlans.Should().BeEmpty();
  harness.ExternalChangeEvents.Should().BeEmpty();

  harness.Advance(TimeSpan.FromMinutes(68));
  harness.Signal(PowerLifecycleKind.Resumed);
  harness.SetApiIdle(8);
  await harness.RunSignaledIterationAsync();
  harness.LastPublishedIdle.Should().Be(0);
  harness.AppliedPlans.Should().BeEmpty();
  harness.LastKnownPlan.Should().Be(Plans.Balanced);

  harness.Advance(TimeSpan.FromSeconds(5));
  await harness.RunSignaledIterationAsync();
  harness.AppliedPlans.Should().Equal(Plans.HighPerformance);
}

[Fact]
public async Task External_plan_change_while_running_is_reported_and_corrected_once()
{
  var harness = GuardianLifecycleHarness.Create(activePlan: Plans.HighPerformance, apiIdle: 10);
  harness.SetOsPlan(Plans.Balanced);
  await harness.RunNormalIterationAsync();
  harness.ExternalChangeEvents.Should().HaveCount(1);
  harness.AppliedPlans.Should().Equal(Plans.HighPerformance);
}

[Fact]
public async Task Suspend_and_resume_queued_while_loop_is_paused_are_logged_in_observed_order()
{
  var harness = GuardianLifecycleHarness.Create(activePlan: Plans.HighPerformance, apiIdle: 0);
  harness.Signal(PowerLifecycleKind.Suspending);
  harness.Advance(TimeSpan.FromHours(1));
  harness.Signal(PowerLifecycleKind.Resumed);

  await harness.RunSignaledIterationAsync();

  harness.LifecycleEvents.Select(x => x.EventName).Should().Equal(
    EventNameRegistry.PowerSuspendObserved,
    EventNameRegistry.PowerResumeObserved);
  harness.LifecycleEvents.Select(x => x.ObservedAtUtc).Should().BeInAscendingOrder();
}

[Fact]
public async Task Missing_resume_recovers_baseline_but_cannot_mutate_plan_until_stable()
{
  var harness = GuardianLifecycleHarness.Create(activePlan: Plans.HighPerformance, apiIdle: 290);
  harness.Signal(PowerLifecycleKind.Suspending);
  await harness.RunSignaledIterationAsync();

  harness.Advance(TimeSpan.FromSeconds(30));
  await harness.RunNormalIterationAsync();

  harness.Events.Should().ContainSingle(x =>
    x.EventName == EventNameRegistry.PowerLifecycleRecoveryForced);
  harness.LastPublishedIdle.Should().Be(0);
  harness.AppliedPlans.Should().BeEmpty();
}
```

`GuardianLifecycleHarness` 通过 `GuardianIterationRunner` 的 internal 构造参数注入 battery probe、plan probe、plan apply、brightness 和 log sink；不得启动真实 `powercfg.exe`。

- [ ] **Step 2: 运行 RED**

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~GuardianPowerLifecycleIntegrationTests" --logger "trx;LogFileName=guardian-power-lifecycle-red.trx"
```

Expected: FAIL；当前循环没有生命周期 context。

- [ ] **Step 3: 新建 context**

```csharp
internal readonly record struct GuardianIterationContext(
  bool AllowPolicyMutation,
  bool ResetIdleEpoch,
  bool ForcePowerRefresh,
  bool AbsorbCurrentPlanAsBaseline,
  PowerLifecyclePhase LifecyclePhase)
{
  public static GuardianIterationContext Running { get; } = new(
    true, false, false, false, PowerLifecyclePhase.Running);
}
```

- [ ] **Step 4: 主循环只按 directive 调度**

```csharp
private void HandlePowerEvent(PowerLifecycleSignal signal)
{
  _powerLifecycle.Observe(signal);
  _wakeSignal.Release();
}

var directive = _powerLifecycle.TakeDirective();
WriteLifecycleTransitions(config, directive.TransitionsToLog);
if (directive.TransitionOverflowed)
  WriteLifecycleSignalOverflow(config);
if (directive.RecoveryForced)
  WriteLifecycleRecoveryForced(config);
if (!directive.RunIteration)
{
  WaitForNextIteration(
    GuardianIterationTiming.ResolveWaitSeconds(
      _configRepository, _rootPath, directive.ReevaluateAfter),
    cancellationToken);
  continue;
}

await _iterationRunner.RunAsync(
  _iterationState,
  new GuardianIterationContext(
    directive.AllowPolicyMutation,
    directive.ResetIdleEpoch,
    directive.ForcePowerRefresh,
    directive.AbsorbCurrentPlanAsBaseline,
    directive.Phase),
  cancellationToken);
```

`WriteLifecycleTransitions` 必须逐项写入，不得只记录批次最后一项。Task 4 在 `EngineLogEvents` 增加 internal lifecycle event factory，并让每条事件携带 `properties.lifecycleObservedAtUtc = signal.ObservedAtUtc.ToString("O")`；不得复用只接受 outcome 的旧 `WriteLog` 签名硬塞字段。顶层 `timestamp` 仍表示事件进入日志系统的时间。`ResolveWaitSeconds` 返回配置间隔与 lifecycle 重新评估间隔的较小整数秒；普通轮询下限仍是 5 秒，只有恢复稳定期和 30 秒漏 Resume 自恢复期限可缩短等待。

- [ ] **Step 5: Runner 吸收恢复基线但不写计划**

```csharp
if (context.ResetIdleEpoch)
  idleTracker.ResetAfterResume();
var idle = (int)idleTracker.Sample(readIdleSeconds);
var (batteryPercent, isOnAc) = readBatteryInfo(context.ForcePowerRefresh);
var activePlan = probeCurrentPlan();

if (context.AbsorbCurrentPlanAsBaseline)
{
  state.LastKnownGuid = activePlan.Guid;
  state.ScriptJustSwitched = false;
}

var externalChangeDetected = context.AllowPolicyMutation
  && !context.AbsorbCurrentPlanAsBaseline
  && PolicyEngine.IsExternalPlanChange(
    state.LastKnownGuid, activePlan.Guid, state.ScriptJustSwitched, expected);
```

只有 `AllowPolicyMutation=true` 才进入计划切换。恢复稳定期仍发布 status 和 `PowerResumeObserved`，但不发送计划通知。

- [ ] **Step 6: 修正事件身份**

```text
周期心跳 -> HeartbeatTick
计划切换成功 -> PowerPlanSwitched
外部计划变化 -> ExternalPlanChange
空闲标签变化 -> IdleDetected
亮度变化 -> BrightnessAdjusted
挂起/恢复 -> PowerSuspendObserved / PowerResumeObserved
漏 Resume 自恢复 -> PowerLifecycleRecoveryForced
转换队列溢出 -> PowerLifecycleSignalOverflow
```

禁止再给全部 Runner 日志统一套 `HeartbeatTick`。

- [ ] **Step 7: GREEN、架构门禁和提交**

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~GuardianPowerLifecycleIntegrationTests|FullyQualifiedName~GuardianIterationTimingTests|FullyQualifiedName~PolicyEngineTests|FullyQualifiedName~StructuredEngineLoggingTests"
dotnet test .\Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerLifecycleArchitectureTests|FullyQualifiedName~GuardianIterationRunnerArchitectureTests"
git add src/SmartGuard.Engine/Worker/GuardianIterationContext.cs src/SmartGuard.Engine/Worker/GuardianLoop.cs src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs src/SmartGuard.Configuration/GuardianIterationTiming.cs src/SmartGuard.Configuration/EventNameRegistry.cs src/SmartGuard.Configuration/LogEventChannels.cs Tests/SmartGuard.Engine.Tests/GuardianPowerLifecycleIntegrationTests.cs Tests/SmartGuard.Engine.Tests/GuardianIterationTimingTests.cs Tests/SmartGuard.Engine.Tests/StructuredEngineLoggingTests.cs Tests/SmartGuard.Architecture.Tests/GuardianIterationRunnerArchitectureTests.cs
git commit -m "fix(engine): 按生命周期门控电源计划仲裁"
```

Expected: 两个测试命令失败数均为 0。

---

### Task 5: 让 powercfg 探测和切换结果成为可验证真相

**Files:**
- Create: `src/SmartGuard.Configuration/PowerCfgCommandResult.cs`
- Create: `src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs`
- Modify: `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs`
- Modify: `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs`
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs`
- Modify: `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs`
- Delete: `src/SmartGuard.Engine/Worker/GuardianPlanProbe.cs`
- Modify: `Tests/SmartGuard.Configuration.Tests/PowerCfgProcessRunnerTests.cs`
- Modify: `Tests/SmartGuard.Engine.Tests/PowerCfgParserTests.cs`
- Create: `Tests/SmartGuard.Engine.Tests/PowerPlanOperationTests.cs`
- Modify: `Tests/SmartGuard.Architecture.Tests/GuardianLoopArchitectureTests.cs`

**Interfaces:**
- Produces: `PowerCfgCommandResult`、`PowerPlanProbeResult`、`PowerPlanApplyResult`。
- Invariant: current plan 未知时不写计划；成功日志必须建立在 apply 后重新探测到 target。

- [ ] **Step 1: 写 RED 失败传播和验证测试**

```csharp
[Fact]
public void Nonzero_exit_is_not_parsed_as_a_valid_plan()
{
  var command = new PowerCfgCommandResult(
    true, false, 1, "", "Access is denied.");
  var result = PowerCfgExecutor.ParseProbeResult(command);
  result.Succeeded.Should().BeFalse();
  result.FailureKind.Should().Be(PowerPlanFailureKind.CommandFailed);
}

[Fact]
public async Task Apply_is_successful_only_when_followup_probe_matches_target()
{
  var target = Plans.HighPerformance;
  var runner = new RecordingPowerCfgRunner(
    SetResult(exitCode: 0),
    ProbeResult(Plans.Balanced),
    ProbeResult(Plans.Balanced),
    ProbeResult(Plans.Balanced));

  var result = await PowerCfgExecutor.ApplyAndVerifyAsync(
    target, runner.RunAsync, RecordingDelay.CompleteImmediately, CancellationToken.None);

  result.Succeeded.Should().BeFalse();
  result.ObservedGuid.Should().Be(Plans.Balanced);
  runner.Arguments.Should().Equal(
    $"/setactive {target:D}",
    "/getactivescheme",
    "/getactivescheme",
    "/getactivescheme");
}

[Fact]
public async Task Apply_verification_accepts_bounded_delayed_convergence()
{
  var target = Plans.HighPerformance;
  var delay = new RecordingDelay();
  var runner = new RecordingPowerCfgRunner(
    SetResult(exitCode: 0),
    ProbeResult(Plans.Balanced),
    ProbeResult(target));

  var result = await PowerCfgExecutor.ApplyAndVerifyAsync(
    target, runner.RunAsync, delay.WaitAsync, CancellationToken.None);

  result.Succeeded.Should().BeTrue();
  delay.Requested.Should().Equal(TimeSpan.FromMilliseconds(250));
}
```

- [ ] **Step 2: 运行 RED**

```powershell
dotnet test .\Tests\SmartGuard.Configuration.Tests\SmartGuard.Configuration.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerCfgProcessRunnerTests" --logger "trx;LogFileName=powercfg-result-red.trx"
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerCfgParserTests|FullyQualifiedName~PowerPlanOperationTests"
```

Expected: FAIL；当前 runner 只有字符串结果，apply 无复核。

- [ ] **Step 3: 统一进程结果**

```csharp
public sealed record PowerCfgCommandResult(
  bool Started,
  bool TimedOut,
  int? ExitCode,
  string StandardOutput,
  string StandardError)
{
  public bool Succeeded => Started && !TimedOut && ExitCode == 0;
  public string CombinedOutput => StandardOutput + StandardError;
}
```

`PowerCfgProcessRunner` 保持 5 秒默认上限；timeout kill entire process tree 后返回 `TimedOut=true`。`PowerPlanCatalogProvider` 只解析成功 stdout，失败返回空 catalog。

- [ ] **Step 4: 实现探测与 apply-verify**

```csharp
public enum PowerPlanFailureKind
{
  None,
  ProcessStartFailed,
  TimedOut,
  CommandFailed,
  ParseFailed,
  VerificationMismatch,
}

public readonly record struct PowerPlanProbeResult(
  bool Succeeded,
  PowerSchemeInfo? Plan,
  PowerPlanFailureKind FailureKind,
  string Diagnostic);

public readonly record struct PowerPlanApplyResult(
  bool Succeeded,
  Guid TargetGuid,
  Guid? ObservedGuid,
  PowerPlanFailureKind FailureKind,
  string Diagnostic);
```

Runner 固定行为：

1. probe 失败：保留 `LastKnownGuid`，写 `PowerPlanProbeFailed`，跳过 external 判定和计划写入。
2. `setactive` 返回成功后执行最多 3 次条件探测：立即、累计 250 ms、累计 1 s；任一次匹配即停止，全部不匹配才判 `VerificationMismatch`。等待通过 internal `delayAsync` 注入点执行，生产使用可取消的 `Task.Delay`，不用 `Thread.Sleep`。
3. set/verify 失败：写 `PowerPlanApplyFailed`，不设置 `ScriptJustSwitched`，不发送成功通知。
4. verify 匹配：才更新状态、写 `PowerPlanSwitched`。
5. 亮度恢复失败与计划失败分别记录。

三次探测是单次实际计划切换内的固定上限，不进入普通 15 秒迭代路径；测试必须断言成功提前退出、全不匹配的精确调用次数和取消传播。

删除只保护循环尾部第二次 probe 的 `GuardianPlanProbe`。

- [ ] **Step 5: GREEN、架构测试和提交**

```powershell
dotnet test .\Tests\SmartGuard.Configuration.Tests\SmartGuard.Configuration.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerCfgProcessRunnerTests|FullyQualifiedName~PowerPlanCatalogProviderTests"
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerCfgParserTests|FullyQualifiedName~PowerPlanOperationTests|FullyQualifiedName~GuardianPowerLifecycleIntegrationTests"
dotnet test .\Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj --no-restore --filter "FullyQualifiedName~GuardianLoopArchitectureTests|FullyQualifiedName~PowerLifecycleArchitectureTests"
git add src/SmartGuard.Configuration/PowerCfgCommandResult.cs src/SmartGuard.Configuration/PowerCfgProcessRunner.cs src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs src/SmartGuard.Engine/Worker/GuardianPlanProbe.cs Tests/SmartGuard.Configuration.Tests/PowerCfgProcessRunnerTests.cs Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs Tests/SmartGuard.Engine.Tests/PowerCfgParserTests.cs Tests/SmartGuard.Engine.Tests/PowerPlanOperationTests.cs Tests/SmartGuard.Engine.Tests/GuardianPowerLifecycleIntegrationTests.cs Tests/SmartGuard.Architecture.Tests/GuardianLoopArchitectureTests.cs
git commit -m "fix(engine): 验证电源计划探测与切换结果"
```

Expected: 三个测试命令失败数均为 0。

---

### Task 6: 让心跳使用单调调度并且只在投递成功后推进

**Files:**
- Create: `src/SmartGuard.Engine/Infrastructure/HeartbeatSchedule.cs`
- Create: `src/SmartGuard.Engine/Worker/GuardianLogWriteResult.cs`
- Create: `Tests/SmartGuard.Engine.Tests/HeartbeatScheduleTests.cs`
- Modify: `src/SmartGuard.Engine/Worker/GuardianLoop.Logging.cs`
- Modify: `src/SmartGuard.Engine/Worker/GuardianLoop.cs`
- Modify: `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs`
- Modify: `src/SmartGuard.Configuration/LogFallbackChain.cs`
- Modify: `Tests/SmartGuard.Configuration.Tests/LogFallbackChainTests.cs`
- Delete: `src/SmartGuard.Engine/Infrastructure/HeartLogSampler.cs`
- Delete: `Tests/SmartGuard.Engine.Tests/HeartLogSamplerTests.cs`
- Modify: `Tests/SmartGuard.Engine.Tests/GuardianPowerLifecycleIntegrationTests.cs`

**Interfaces:**
- Produces: `HeartbeatSchedule.IsDue`、`MarkAccepted`、`ResetAfterResume`；`GuardianLogWriteResult.IsAccepted`。
- Consumes: Task 2 生命周期 Resume 指令、现有 `LogPipeline` 和 `LogFallbackChain`。
- Invariant: 挂起期间不补历史心跳；恢复后立即尝试一次；失败不推进窗口。

- [ ] **Step 1: 写 RED 调度测试**

```csharp
[Fact]
public void Rejected_heartbeat_is_retried_on_next_iteration()
{
  var time = new ManualTimeProvider();
  var schedule = new HeartbeatSchedule(time);
  schedule.IsDue(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(60)).Should().BeTrue();

  time.Advance(TimeSpan.FromSeconds(15));

  schedule.IsDue(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(60)).Should().BeTrue();
}

[Fact]
public void Accepted_heartbeat_advances_monotonic_window()
{
  var time = new ManualTimeProvider();
  var schedule = new HeartbeatSchedule(time);
  schedule.MarkAccepted();

  time.Advance(TimeSpan.FromMinutes(9));
  time.SetUtcNow(time.GetUtcNow().AddHours(-12));
  schedule.IsDue(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(60)).Should().BeFalse();

  time.AdvanceTimestamp(TimeSpan.FromMinutes(1));
  schedule.IsDue(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(60)).Should().BeTrue();
}

[Fact]
public void Resume_requests_one_immediate_heartbeat_without_backfill()
{
  var time = new ManualTimeProvider();
  var schedule = new HeartbeatSchedule(time);
  schedule.MarkAccepted();
  time.Advance(TimeSpan.FromHours(8));

  schedule.ResetAfterResume();

  schedule.IsDue(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(60)).Should().BeTrue();
}
```

- [ ] **Step 2: 运行 RED**

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~HeartbeatScheduleTests" --logger "trx;LogFileName=heartbeat-schedule-red.trx"
```

Expected: FAIL with CS0246；旧 `LastHeartbeat`/`HeartLogSampler` 仍使用 wall clock 且过早推进。

- [ ] **Step 3: 实现单一调度 Owner**

```csharp
public sealed class HeartbeatSchedule(TimeProvider? timeProvider = null)
{
  private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
  private long? _lastAcceptedTimestamp;
  private bool _resumeDue = true;

  public bool IsDue(TimeSpan heartbeatInterval, TimeSpan minimumPersistInterval)
  {
    if (_resumeDue || _lastAcceptedTimestamp is null) return true;
    var effectiveInterval = heartbeatInterval >= minimumPersistInterval
      ? heartbeatInterval
      : minimumPersistInterval;
    return _timeProvider.GetElapsedTime(_lastAcceptedTimestamp.Value) >= effectiveInterval;
  }

  public void MarkAccepted()
  {
    _lastAcceptedTimestamp = _timeProvider.GetTimestamp();
    _resumeDue = false;
  }

  public void ResetAfterResume() => _resumeDue = true;
}
```

删除 `GuardianLoopIterationState.LastHeartbeat` 和 `HeartLogSampler`，禁止保留两个心跳窗口。

- [ ] **Step 4: 让日志写入口返回真实接受结果**

```csharp
internal enum GuardianLogWriteResult
{
  SuppressedByLevel,
  AcceptedByPipeline,
  AcceptedByFallback,
  DroppedByBackpressure,
  Rejected,
}

internal static class GuardianLogWriteResultExtensions
{
  public static bool IsAccepted(this GuardianLogWriteResult result)
    => result is GuardianLogWriteResult.AcceptedByPipeline
      or GuardianLogWriteResult.AcceptedByFallback;
}
```

`WriteLog` 规则：

- level filter 返回 `SuppressedByLevel`；
- normal backpressure 返回 `DroppedByBackpressure`；
- `TryEnqueue` 成功返回 `AcceptedByPipeline`；
- enqueue 失败后调用返回 bool 的 `LogFallbackChain.WriteEvent`；
- fallback 任一层成功返回 `AcceptedByFallback`，三层都失败返回 `Rejected`。

`LogFallbackChain.Write/WriteEvent` 从 `void` 改为 `bool`，每层仍独立 catch、最终永不抛出。既有调用者可忽略返回值。

- [ ] **Step 5: Runner 只在接受后推进**

```csharp
if (heartbeatSchedule.IsDue(
      TimeSpan.FromMinutes(config.HeartbeatIntervalMin),
      TimeSpan.FromSeconds(Math.Max(0, config.LogHeartMinIntervalSec))))
{
  var result = writeLog(
    config,
    LogLevel.Heart,
    GuardianLogMessages.FormatHeartbeat(
      label, planName, idle, batteryPercent, isOnAc, config.Paused, bright),
    EventNameRegistry.HeartbeatTick,
    outcome: null);
  if (result.IsAccepted())
    heartbeatSchedule.MarkAccepted();
}
```

Resume directive 调用 `heartbeatSchedule.ResetAfterResume()`。`HeartbeatIntervalMin <= 0` 时完全禁用，不因 Resume 绕过用户配置。

- [ ] **Step 6: GREEN、日志降级回归和提交**

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~HeartbeatScheduleTests|FullyQualifiedName~GuardianPowerLifecycleIntegrationTests|FullyQualifiedName~StructuredEngineLoggingTests"
dotnet test .\Tests\SmartGuard.Configuration.Tests\SmartGuard.Configuration.Tests.csproj --no-restore --filter "FullyQualifiedName~LogFallbackChainTests|FullyQualifiedName~LogPipelineTests|FullyQualifiedName~LogPipelineLifecycleTests"
git add src/SmartGuard.Engine/Infrastructure/HeartbeatSchedule.cs src/SmartGuard.Engine/Infrastructure/HeartLogSampler.cs src/SmartGuard.Engine/Worker/GuardianLogWriteResult.cs src/SmartGuard.Engine/Worker/GuardianLoop.Logging.cs src/SmartGuard.Engine/Worker/GuardianLoop.cs src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs src/SmartGuard.Configuration/LogFallbackChain.cs Tests/SmartGuard.Engine.Tests/HeartbeatScheduleTests.cs Tests/SmartGuard.Engine.Tests/HeartLogSamplerTests.cs Tests/SmartGuard.Engine.Tests/GuardianPowerLifecycleIntegrationTests.cs Tests/SmartGuard.Configuration.Tests/LogFallbackChainTests.cs
git commit -m "fix(logging): 让心跳按成功投递推进"
```

Expected: 两个测试命令失败数均为 0。

---

### Task 7: 给 status.json 设置有界新鲜度而不恢复高频 IO

**Files:**
- Modify: `src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs`
- Modify: `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs`
- Modify: `Tests/SmartGuard.Engine.Tests/StatusPublisherTests.cs`
- Modify: `docs/ARCHITECTURE-CONTRACT.md`

**Interfaces:**
- Produces: `StatusPublisher(string, TimeProvider?, TimeSpan?)`。
- Invariant: 状态有意义变化立即写；无变化最多 60 秒写一次；写成功后才推进新鲜度时间戳。

- [ ] **Step 1: 写 RED 新鲜度和 UTC 测试**

```csharp
[Fact]
public void Unchanged_status_is_refreshed_when_max_staleness_is_reached()
{
  var time = new ManualTimeProvider();
  var publisher = new StatusPublisher(
    _statusPath, time, TimeSpan.FromSeconds(60));
  var payload = CreatePayload(idleSeconds: 0, batteryPercent: 90, Plans.HighPerformance.ToString());

  publisher.Publish(payload);
  var firstWrite = File.GetLastWriteTimeUtc(_statusPath);

  time.Advance(TimeSpan.FromSeconds(59));
  publisher.Publish(payload);
  File.GetLastWriteTimeUtc(_statusPath).Should().Be(firstWrite);

  time.Advance(TimeSpan.FromSeconds(1));
  payload.timestamp = time.GetUtcNow().ToString("O");
  publisher.Publish(payload);
  File.ReadAllText(_statusPath).Should().Contain(payload.timestamp);
}

[Fact]
public void Idle_growth_is_visible_at_refresh_cadence()
{
  var time = new ManualTimeProvider();
  var publisher = new StatusPublisher(
    _statusPath, time, TimeSpan.FromSeconds(60));
  publisher.Publish(CreatePayload(0, 90, Plans.HighPerformance.ToString()));

  time.Advance(TimeSpan.FromSeconds(60));
  publisher.Publish(CreatePayload(60, 90, Plans.HighPerformance.ToString()));

  File.ReadAllText(_statusPath).Should().Contain("\"idleSeconds\": 60");
}
```

- [ ] **Step 2: 运行 RED**

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~StatusPublisherTests" --logger "trx;LogFileName=status-freshness-red.trx"
```

Expected: FAIL；旧发布器会无限跳过仅 idle/timestamp 变化。

- [ ] **Step 3: 实现有界刷新和无删除窗口替换**

```csharp
public void Publish(StatusPayload payload)
{
  var now = _timeProvider.GetTimestamp();
  var meaningfulChange = _lastPayload is null
    || !StatusPayloadEquals(_lastPayload, payload)
    || IdleDroppedSignificantly(_lastPayload, payload);
  var freshnessDue = _lastWriteTimestamp is null
    || _timeProvider.GetElapsedTime(_lastWriteTimestamp.Value, now) >= _maxStaleness;

  if (!meaningfulChange && !freshnessDue) return;

  WritePayload(payload);
  _lastPayload = payload;
  _lastWriteTimestamp = now;
}
```

`WritePayload` 使用同目录 temp 并以 `File.Move(temp, statusPath, overwrite: true)` 替换，禁止先 `File.Delete(statusPath)` 制造文件不存在窗口；异常时尽力删除本次 temp 后重新抛出，由 Engine 现有异常路径记录。Runner 统一生成：

```csharp
timestamp = timeProvider.GetUtcNow().ToString("O"),
```

不新增 status JSON 字段，保持旧 reader 兼容。

- [ ] **Step 4: GREEN、契约更新和提交**

在契约登记：

```markdown
| STATUS-FRESH | 阻断 | `StatusPublisher` 对有意义变化立即写；无变化按单调时钟最多 60 秒刷新一次，且不以 delete+move 暴露缺失窗口。 |
```

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~StatusPublisherTests|FullyQualifiedName~GuardianPowerLifecycleIntegrationTests"
dotnet test .\Tests\SmartGuard.Tray.Tests\SmartGuard.Tray.Tests.csproj --no-restore
dotnet test .\Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj --no-restore --filter "FullyQualifiedName~Status|FullyQualifiedName~Idle"
git add src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs Tests/SmartGuard.Engine.Tests/StatusPublisherTests.cs docs/ARCHITECTURE-CONTRACT.md
git commit -m "fix(engine): 限制运行状态快照陈旧时间"
```

Expected: 三个测试命令失败数均为 0；空闲增加不再永久陈旧。

---

### Task 8: 建立组合测试、真实待机验收和最终门禁

**Files:**
- Create: `scripts/Invoke-PowerLifecycleAcceptance.ps1`
- Create: `Tests/SmartGuard.Engine.Tests/PowerLifecycleScenarioTests.cs`
- Create: `Tests/SmartGuard.Architecture.Tests/PowerLifecycleAcceptanceArchitectureTests.cs`
- Modify: `docs/ARCHITECTURE-CONTRACT.md`

**Interfaces:**
- Consumes: Task 2–7 的 lifecycle、idle、powercfg、heartbeat、status 行为。
- Produces: `.work/test-results/power-lifecycle-acceptance.result.json`、transcript、退出码。
- Invariant: 脚本只观察和验证；不得自动触发睡眠、改系统时间或改计划。计划变更场景由操作者在明确提示后手动执行。

- [ ] **Step 1: 写全序列 RED 测试**

```csharp
[Theory]
[InlineData(false)]
[InlineData(true)]
public async Task Resume_sequence_is_deterministic_on_battery_and_ac(bool onAc)
{
  var scenario = PowerLifecycleScenario.Start(onAc, idleSeconds: 290);
  await scenario.SuspendAsync();
  scenario.OsChangesPlanTo(Plans.Balanced);
  scenario.Advance(TimeSpan.FromHours(1));
  await scenario.ResumeByMouseAsync(apiIdleAtFirstSample: 8);

  scenario.PlanWritesDuringSuspend.Should().Be(0);
  scenario.ExternalWarningsDuringSuspend.Should().Be(0);
  scenario.FirstResumeIdle.Should().Be(0);
  scenario.ResumeHeartbeats.Should().Be(1);
  scenario.StatusAge.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
}

[Fact]
public async Task Plan_probe_failure_does_not_create_false_success_or_notification()
{
  var scenario = PowerLifecycleScenario.Start(onAc: true, idleSeconds: 0);
  scenario.FailNextPlanProbe(PowerPlanFailureKind.TimedOut);

  await scenario.RunIterationAsync();

  scenario.PlanWrites.Should().Be(0);
  scenario.SuccessfulPlanEvents.Should().Be(0);
  scenario.PlanNotifications.Should().Be(0);
  scenario.FailureEvents.Should().ContainSingle();
}
```

- [ ] **Step 2: 运行 RED 后补齐组合 harness**

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerLifecycleScenarioTests" --logger "trx;LogFileName=power-lifecycle-scenario-red.trx"
```

Expected: 首次 FAIL；补齐 harness 后覆盖以下矩阵并 GREEN：

| 场景 | 必须断言 |
|---|---|
| Running → Suspend → OS Balance → Resume(Mouse) | Suspend 零 plan write；恢复基线 idle=0；稳定期后至多一次纠偏。 |
| Duplicate Suspend/Resume | reset/baseline/恢复心跳各至多一次。 |
| Suspend/Resume 在一次消费前连续到达 | 两条转换保序记录；Suspend 不被 Resume 覆盖；无挂起期 plan write。 |
| Resume 信号缺失 | 30 秒后进入受控恢复稳定期；记录 RecoveryForced；先 reset/baseline，稳定前零 plan write。 |
| RecoveryForced 后迟到 Resume | Resume 仍记录；稳定窗口重算；reset/baseline 不重复；稳定前零 plan write。 |
| lifecycle 转换风暴超过容量 | 最新 phase 正确；结构化 overflow 告警恰好一次；无静默丢失声明。 |
| StatusChange AC↔DC | 强制新鲜电源采样；不触发 resume reset。 |
| 无 SystemEvents | 15 秒普通轮询继续工作；日志明确 listener unavailable。 |
| powercfg timeout/nonzero/parse failure/mismatch | 无成功日志、无成功通知、保留可解释状态。 |
| 心跳 queue reject 后恢复 | 拒绝不推进，下轮重试，成功后只写一次。 |
| UTC 前跳/后跳 | idle/heartbeat/status cadence 不受 wall clock 改变。 |
| Engine 重启 | 首轮心跳一次，状态 PID 更新，生命周期从 Running 安全启动。 |

- [ ] **Step 3: 创建真实验收脚本**

脚本参数和结果对象固定为：

```powershell
[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string]$Root,
  [ValidateSet('LidMouseBattery','LidMouseAC','ExternalPlanRunning')]
  [string]$Scenario,
  [string]$ResultPath = '.work/test-results/power-lifecycle-acceptance.result.json',
  [string]$TranscriptPath = '.work/test-results/power-lifecycle-acceptance.transcript.txt'
)

$ErrorActionPreference = 'Stop'
$startedAt = [DateTimeOffset]::Now
$checks = [System.Collections.Generic.List[object]]::new()
[System.IO.Directory]::CreateDirectory(
  [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($ResultPath))) | Out-Null
[System.IO.Directory]::CreateDirectory(
  [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($TranscriptPath))) | Out-Null
Start-Transcript -LiteralPath $TranscriptPath -Force

try {
  $logPath = Join-Path $Root 'SmartGuard.log'
  $statusPath = Join-Path $Root 'SmartGuard.status.json'
  $baselineHealth = Get-Content -LiteralPath $logPath -Encoding utf8 -Tail 2000 |
    ForEach-Object { try { $_ | ConvertFrom-Json -Depth 20 } catch { $null } } |
    Where-Object eventName -eq 'PipelineHealthSampled' |
    Select-Object -Last 1
  $initialLength = (Get-Item -LiteralPath $logPath).Length
  Write-Host "开始场景 $Scenario。脚本不会自动睡眠或修改电源计划。"
  Read-Host '完成操作并保持唤醒，确认出现下一条 LogPipeline health sample 后按 Enter'

  if ((Get-Item -LiteralPath $logPath).Length -lt $initialLength) {
    throw '验收期间主日志发生轮转，当前 cursor 不再有效；请重新运行单个场景。'
  }

  $stream = [System.IO.File]::Open(
    $logPath,
    [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::Read,
    [System.IO.FileShare]::ReadWrite)
  $stream.Seek($initialLength, [System.IO.SeekOrigin]::Begin) | Out-Null
  $reader = [System.IO.StreamReader]::new(
    $stream, [System.Text.UTF8Encoding]::new($false, $true), $false)
  try { $appendedText = $reader.ReadToEnd() }
  finally { $reader.Dispose(); $stream.Dispose() }

  $newLines = $appendedText -split "`r?`n" |
    ForEach-Object {
      try { $_ | ConvertFrom-Json -Depth 20 } catch { $null }
    } |
    Where-Object { $_ -and [DateTimeOffset]$_.timestamp -ge $startedAt.ToUniversalTime() }
  $postHealth = $newLines |
    Where-Object eventName -eq 'PipelineHealthSampled' |
    Select-Object -Last 1

  $status = [System.IO.File]::ReadAllText(
    $statusPath, [System.Text.UTF8Encoding]::new($false, $true)) |
    ConvertFrom-Json

  $powerEvents = Get-WinEvent -FilterHashtable @{
    LogName = 'System'
    StartTime = $startedAt.LocalDateTime
  } -ErrorAction Stop | Where-Object {
    $_.ProviderName -in @(
      'Microsoft-Windows-Kernel-Power',
      'Microsoft-Windows-Power-Troubleshooter') -and
    $_.Id -in @(1,42,107,506,507)
  }

  if ($Scenario -in @('LidMouseBattery','LidMouseAC')) {
    $suspend = $newLines | Where-Object eventName -eq 'PowerSuspendObserved' | Select-Object -Last 1
    $resume = $newLines | Where-Object eventName -eq 'PowerResumeObserved' | Select-Object -First 1
    $suspendObservedAt = $(if ($null -ne $suspend) {
      [DateTimeOffset]$suspend.properties.lifecycleObservedAtUtc
    })
    $resumeObservedAt = $(if ($null -ne $resume) {
      [DateTimeOffset]$resume.properties.lifecycleObservedAtUtc
    })
    $checks.Add([pscustomobject]@{
      name = 'lifecycle-events'
      status = $(if ($null -ne $suspendObservedAt -and $null -ne $resumeObservedAt -and
        $suspendObservedAt -lt $resumeObservedAt) { 'Passed' } else { 'Failed' })
    })
    $checks.Add([pscustomobject]@{
      name = 'windows-suspend-resume-events'
      status = $(if (@($powerEvents | Where-Object Id -in @(42,506)).Count -ge 1 -and
        @($powerEvents | Where-Object Id -in @(1,107,507)).Count -ge 1) {
          'Passed'
        } else { 'Failed' })
    })
    $checks.Add([pscustomobject]@{
      name = 'no-suspend-plan-write'
      status = $(if ($null -ne $suspendObservedAt -and $null -ne $resumeObservedAt -and
        @($newLines | Where-Object {
          $_.eventName -eq 'PowerPlanSwitched' -and
          [DateTimeOffset]$_.timestamp -gt $suspendObservedAt -and
          [DateTimeOffset]$_.timestamp -lt $resumeObservedAt
        }).Count -eq 0) { 'Passed' } else { 'Failed' })
    })
  }
  else {
    $checks.Add([pscustomobject]@{
      name = 'external-change-observed-once'
      status = $(if (@($newLines |
        Where-Object eventName -eq 'ExternalPlanChange').Count -eq 1) {
          'Passed'
        } else { 'Failed' })
    })
  }
  $checks.Add([pscustomobject]@{
    name = 'status-freshness'
    status = $(if (([DateTimeOffset]::Now - [DateTimeOffset]$status.timestamp) -le
      [TimeSpan]::FromSeconds(90)) { 'Passed' } else { 'Failed' })
  })

  $pipelineStatus = if ($null -eq $baselineHealth -or $null -eq $postHealth -or
    [int]$baselineHealth.processId -ne [int]$postHealth.processId) {
    'Blocked'
  }
  elseif (
    [long]$postHealth.properties.droppedNormal -eq [long]$baselineHealth.properties.droppedNormal -and
    [long]$postHealth.properties.droppedHigh -eq [long]$baselineHealth.properties.droppedHigh -and
    [long]$postHealth.properties.failedBatches -eq [long]$baselineHealth.properties.failedBatches) {
    'Passed'
  }
  else { 'Failed' }
  $checks.Add([pscustomobject]@{ name = 'pipeline-health-delta'; status = $pipelineStatus })

  $resultState = if (@($checks | Where-Object status -eq 'Blocked').Count -gt 0) {
    'Blocked'
  }
  elseif (@($checks | Where-Object status -eq 'Failed').Count -gt 0) {
    'Failed'
  }
  else { 'Passed' }
  $result = [pscustomobject]@{
    schemaVersion = 1
    scenario = $Scenario
    startedAt = $startedAt.ToString('O')
    completedAt = [DateTimeOffset]::Now.ToString('O')
    result = $resultState
    checks = $checks
  }
  $json = $result | ConvertTo-Json -Depth 8
  [System.IO.File]::WriteAllText(
    $ResultPath, $json, [System.Text.UTF8Encoding]::new($false))
  if ($resultState -eq 'Blocked') { exit 2 }
  if ($resultState -eq 'Failed') { exit 1 }
}
catch {
  $json = [pscustomobject]@{
    schemaVersion = 1
    scenario = $Scenario
    completedAt = [DateTimeOffset]::Now.ToString('O')
    result = 'Blocked'
    error = $_.Exception.Message
  } | ConvertTo-Json -Depth 8
  [System.IO.File]::WriteAllText(
    $ResultPath, $json, [System.Text.UTF8Encoding]::new($false))
  exit 2
}
finally {
  Stop-Transcript
}
```

实施时保留以上退出码：`0=Passed`、`1=Failed`、`2=Blocked`。`Blocked` 优先级高于 `Failed`；管线前后样本缺失或 `processId` 不同都属于证据链中断，不能比较累计计数。脚本自身的结果写入必须使用显式 UTF-8；它只写 `.work/test-results`，不修改产品配置/状态/计划。

- [ ] **Step 4: 架构门禁锁定验收入口**

```csharp
[Fact]
public void Power_lifecycle_acceptance_has_one_observable_script_entry()
{
  var script = SourceScanHelper.ReadSource("scripts/Invoke-PowerLifecycleAcceptance.ps1");
  script.Should().Contain("power-lifecycle-acceptance.result.json");
  script.Should().Contain("Start-Transcript");
  script.Should().Contain("result = 'Blocked'");
  script.Should().Contain("pipeline-health-delta");
  script.Should().Contain("status -eq 'Blocked'");
  script.Should().NotContain("SetSuspendState");
  script.Should().NotContain("Set-Date");
}
```

- [ ] **Step 5: 运行目标 GREEN 和快速回归**

```powershell
dotnet test .\Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerLifecycleScenarioTests|FullyQualifiedName~GuardianPowerLifecycleIntegrationTests|FullyQualifiedName~IdleTrackerTests|FullyQualifiedName~HeartbeatScheduleTests|FullyQualifiedName~PowerPlanOperationTests|FullyQualifiedName~StatusPublisherTests"
dotnet test .\Tests\SmartGuard.Configuration.Tests\SmartGuard.Configuration.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerCfgProcessRunnerTests|FullyQualifiedName~LogFallbackChainTests|FullyQualifiedName~LogPipeline"
dotnet test .\Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerLifecycle|FullyQualifiedName~GuardianIterationRunnerArchitectureTests|FullyQualifiedName~GuardianLoopArchitectureTests"
```

Expected: 三个命令失败数均为 0。

- [ ] **Step 6: 运行仓库全门禁并核对结构化真相**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1
$summary = Get-Content -Raw -Encoding utf8 '.work/test-results/latest-summary.json' | ConvertFrom-Json
$summary | ConvertTo-Json -Depth 12
```

如果实际 summary 文件名不是 `latest-summary.json`，只允许消费 `Run-Tests.ps1` 本次输出明确登记的 summary 路径，不得猜路径或从 stdout 推断结果。必须报告：

- 父进程退出码；
- Pester Passed/Failed/Skipped；
- .NET Passed/Failed/Skipped；
- Tray Integration Passed/Failed/Skipped/NotRun；
- Installer Integration Passed/Failed/Skipped/NotRun；
- summary JSON 是否存在、可解析、状态是否 Completed。

任一结果缺失、损坏、计数为零或非 Completed，整体 fail closed。

- [ ] **Step 7: 执行真实待机矩阵**

先确认设备支持的睡眠模型：

```powershell
powercfg.exe /a
```

然后依次运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-PowerLifecycleAcceptance.ps1 -Root 'D:\Apps\SmartGuard' -Scenario LidMouseBattery
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-PowerLifecycleAcceptance.ps1 -Root 'D:\Apps\SmartGuard' -Scenario LidMouseAC
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-PowerLifecycleAcceptance.ps1 -Root 'D:\Apps\SmartGuard' -Scenario ExternalPlanRunning
```

验收必须同时核对 Windows System 事件与 SmartGuard 事件：

| 场景 | 通过标准 |
|---|---|
| LidMouseBattery | Lid Suspend 与 Mouse Resume 成对；Suspend 到 Resume 之间无 SmartGuard plan write；首次 resume idle 0–15 秒；5 秒稳定期后至多一次计划动作。 |
| LidMouseAC | 同上；AC 状态来自 fresh battery probe，不能由 Resume 常量伪造。 |
| ExternalPlanRunning | 屏幕开启、Running 阶段手动改计划后，恰好一条 ExternalPlanChange 和至多一次 verified correction。 |
| Heartbeat | 睡眠期间不要求心跳；恢复后 30 秒内一条 Resume 和一条 Heartbeat；无补写风暴。 |
| Status | 恢复后 30 秒内更新；持续运行时 age 不超过 90 秒。 |
| Pipeline | dropped/failed 不增长；若增长，必须由独立故障解释且场景失败。 |

任一场景没有真实操作条件，标记 `NOT RUN`；脚本因权限/事件日志访问失败返回 2 时标记 `BLOCKED`，不得改写为通过。

- [ ] **Step 8: 性能与稳态验证**

```powershell
dotnet test .\Tests\SmartGuard.Engine.PerformanceTests\SmartGuard.Engine.PerformanceTests.csproj --no-restore
```

在已安装 Engine 上保持屏幕开启运行 2 小时，记录开始/结束：

- Working Set、Private Bytes、handle、thread；
- `SmartGuard.status.json` 写入次数，目标不超过 125 次/2h；
- 日志 normal/high queue 最大深度；
- plan probe 平均和最大耗时；
- 每分钟 Engine CPU。

通过标准：无持续增长趋势；无新增线程/handle 泄漏；普通 15 秒迭代不增加额外 powercfg 调用；状态写入不超过每分钟一次保底加有意义变化次数。

- [ ] **Step 9: 更新契约收口状态并提交**

只有目标、全门禁和真实待机矩阵全部满足后，将契约中的 `PWR-LIFE-*` 状态更新为“已关闭”，否则保持“阻断”并记录 `NOT RUN/BLOCKED`。

```powershell
git add scripts/Invoke-PowerLifecycleAcceptance.ps1 Tests/SmartGuard.Engine.Tests/PowerLifecycleScenarioTests.cs Tests/SmartGuard.Architecture.Tests/PowerLifecycleAcceptanceArchitectureTests.cs docs/ARCHITECTURE-CONTRACT.md
git commit -m "test(engine): 收口电源生命周期真实验收"
```

---

## 阶段门禁与停止线

| Gate | 进入条件 | 通过条件 | 失败动作 |
|---|---|---|---|
| G0 Root Cause | 本计划现场证据可重放 | 睡眠空档、误判、计划竞争均有事件时间线 | 证据不一致则停止实现，重新取证 |
| G1 Contract | Task 1 RED 保存 | Owner/禁止项契约落盘且契约门禁 GREEN；生产行为尚未实现 | 不得先改生产代码 |
| G2 Pure Model | Task 2–3 | 状态机/idle 单调测试全绿 | 不得接入 GuardianLoop |
| G3 Orchestration | Task 4 | Suspend 零仲裁、Resume 重建、Running 纠偏全绿 | 回到 directive 设计，不加条件补丁 |
| G4 Side Effects | Task 5–7 | plan/log/status 结果可验证且失败不伪成功 | 不得以日志文本代替结果 |
| G5 Automated | Task 8 目标测试 | 目标、Architecture、Configuration、Engine 全绿 | 不运行真实安装验收 |
| G6 Full Gate | `Run-Tests.ps1` | exit 0 + 完整 summary JSON + 所有 required layer 有结论 | fail closed |
| G7 Real System | 已部署候选 build | Battery/AC/Lid/Mouse/External 三场景通过 | 未运行则不宣称修复完成 |
| G8 Soak | G7 通过 | 2 小时资源和 IO 预算无回退 | Regression，停止发布 |

立即停止并返回调查/设计的条件：

- Windows 未发出可观察 Resume，且 30 秒有界自恢复也未能进入 ResumeStabilizing；
- 状态机开始包含 300/900 阈值或计划 GUID；
- 为修一个场景新增第二计时器、第二 AC/DC reader 或第二计划执行入口；
- 需要用超过 30 秒恢复稳定期才能让测试通过；
- 测试必须放宽为“最终某次成功”而不能断言次数和顺序；
- Suspend 期间仍出现 `/setactive`；
- powercfg 结果未知但日志仍显示成功；
- 自动化全绿但真实 Lid/Mouse 仍能重现 14:57:48 类误判。

## 非目标

- 不改变三档阈值、低电量规则、默认 GUID 或用户配置格式。
- 不阻止 Windows 进入 Modern Standby，不申请 Power Request，不让 Engine 在睡眠中持续运行。
- 不修改 Windows 合盖策略、系统电源方案定义、OEM 电源软件或 BIOS。
- 不增加 UI 页面、设置开关、通知样式或日志筛选功能。
- 不借机重写 BatteryInfoProvider；其未知值语义另立 RED 复现后处理。
- 不把 Engine 改成 Windows Service，不改变计划任务 Principal 或 UAC。
- 不补写睡眠期间每个十分钟心跳；只记录 Suspend/Resume 边界和恢复心跳。
- 不清理既有未跟踪测试材料，不改写 Git 历史。

## 风险与缓解

1. **Resume 信号缺失**：状态机在 30 秒期限后进入同一条 ResumeStabilizing 安全路径，写 `PowerLifecycleRecoveryForced`，先 reset/baseline 后再允许仲裁；它不伪造 Windows Resume，也不另建 watcher。真实验收仍把缺失的 Windows Resume 记为失败，不能用自恢复掩盖平台问题。
2. **恢复稳定期太短**：固定 5 秒只用于吸收 OS plan baseline；如真实矩阵失败，先记录 OS 事件/plan 序列，不直接加长。
3. **把 Resume 当新 awake epoch**：会在无人交互唤醒后暂时从 0 计 idle；这是明确安全取舍，最多 5/15 分钟后进入平衡/节能，不会立即和 OS 竞争。
4. **powercfg 复核增加进程调用**：只在实际切换后增加最多三次有界 probe，匹配即提前退出，不在普通无变化迭代增加；性能测试必须证明。
5. **status 每分钟保底写**：约 60 次/小时，远低于旧 15 秒全量写；2 小时实测限制 125 次加有意义变化。
6. **日志投递结果只证明入口接受**：异步落盘真相继续由 LogPipeline health/fallback 负责；不在每条心跳强制 flush。
7. **事件线程竞态**：callback 只写锁保护内存并 release semaphore；相邻重复信号合并，Suspend/Resume 对通过容量 8 的保序批次交付。溢出保留最新 phase 并写告警，不声称完整历史。
8. **旧日志事件兼容**：新增事件名，不删除历史解析别名；UI 未知字段仍按现有“—”显示。

## 回滚

每个 Task 一个 Conventional Commit，依赖方向为：

```text
Task 8 -> Task 7 -> Task 6 -> Task 5 -> Task 4 -> Task 3 -> Task 2 -> Task 1
```

回滚使用 `git revert <commit>`，禁止 reset/改写历史。由于没有配置 schema 迁移：

- 回滚 Task 8 只移除验收入口和收口登记；
- 回滚 Task 7 恢复旧状态去重，不影响 reader；
- 回滚 Task 6 必须同时恢复 `HeartLogSampler` 和 `LastHeartbeat`，避免无心跳 Owner；
- 回滚 Task 5 必须同时恢复 `GuardianPlanProbe`；
- 回滚 Task 4 前必须先回滚 Task 5–7；
- 回滚 Task 2 前必须先回滚 Task 3–4，并恢复 resolver/interpreter 及测试；
- 任一回滚后重新运行 `Run-Tests.ps1`，真实系统结论自动失效并标记 `NOT RUN`。

## grill-me 自主审查记录（2026-08-22）

**审查模式：** 按推荐项自主裁决；只有会改变产品语义、架构 Owner、安全边界或验收口径且无法从仓库证据解答的问题才上抛。本文审查未留下此类未决阻塞项。

**结论：** `PASS WITH REQUIRED CHANGES — ADOPTED`。原方向“typed lifecycle + 单一状态机 + PolicyEngine 不变”成立，但原稿存在 6 个会导致永久停工、丢事件或假绿的阻断缺口；本轮已全部写回对应 Task，不把它们留作实施阶段临场决定。

| 决策分支 | 反证问题 | 推荐裁决 | 仓库/方案依据 | 已采纳修订 |
|---|---|---|---|---|
| Owner | 是否需要另建 watchdog/service 才能处理漏 Resume？ | 否。由 `PowerLifecycleStateMachine` 使用已有时钟和主循环等待完成有界自恢复。 | 项目要求同一事实一个 Owner，禁止为单场景新增第二生命周期。 | `Suspending` 30 秒到期进入 ResumeStabilizing；写 RecoveryForced，不新增线程/timer。 |
| 顺序与幂等 | Suspend 后进程暂停，Resume 回调先于循环消费，或 RecoveryForced 后 Resume 迟到时会怎样？ | 单值不可接受；改为容量 8 的保序批次。迟到 Resume 要记录但不得重复 reset/baseline。 | 当前 `GuardianLoop` 的 semaphore 只表示“需要醒来”，并不保存事件身份；循环会排空多个 release。 | `TransitionToLog` 改为 `TransitionsToLog`；新增覆盖、迟到、重复、溢出测试。 |
| 失败收敛 | Resume 永久丢失是否可以让 Engine 一直停在 Suspending？ | 不可以。核心能力永久静默停工违反架构不变量。 | 原风险条目承认可能永久阻断仲裁，且无健康状态。 | 加入 30 秒自恢复、降级事件和“稳定前零 plan write”门禁。 |
| 副作用真相 | `setactive` 成功后一次立即 probe 不匹配，能否直接判失败？ | 不直接判；做最多三次、总计 1 秒的条件探测，匹配即退出。 | `powercfg` 进程返回与 OS 可观察 active scheme 之间可能存在短暂收敛窗口。 | Task 5 增加 0/250/1000 ms 有界验证、取消与精确次数测试。 |
| 可观测时间 | 生命周期事件恢复后才落盘时，顶层日志 timestamp 能否代表 Suspend 时刻？ | 不能。必须同时持久化原始 `ObservedAtUtc`。 | 事件回调只更新内存，异步管线的落盘/创建时间可能晚于真实 OS 信号。 | lifecycle properties 增加 `lifecycleObservedAtUtc`；真实验收用它划定边界。 |
| 验收真相 | 缺少 health sample 或检查值为空时，能否因“没有 false”而通过？ | 不能。结果三态化，Blocked 优先于 Failed，缺证据 fail closed。 | 原 PowerShell 聚合只统计 `$false`，`$null` 会漏过；项目要求结构化回执缺失即失败关闭。 | 脚本改用 `Passed/Failed/Blocked`，加入管线前后样本并保持 0/1/2 退出码。 |
| API 面 | 生命周期纯内部类型是否应声明 public？ | 不应。仅 Engine 和 friend tests 使用，保持 internal。 | 没有跨程序集消费者，公共类型会制造无必要兼容承诺。 | Task 2 类型声明改为 internal；跨程序集的 powercfg 结果仍按实际边界单独处理。 |
| TDD Gate | Task 1 能否提交故意 RED 的测试？ | 不能。保存 RED 证据后必须让契约门禁 GREEN 再提交。 | 项目禁止主线故意失败；生产行为 RED 从 Task 2 开始。 | 修正 Task 1 标题和 G1 通过条件。 |

### 未上抛的非阻塞裁决

- 5 秒恢复稳定期和 30 秒漏 Resume 上限是当前推荐初值，不是经真实硬件证明的最终常数；Task 8 必须用 Lid/Mouse/AC/DC 证据验证。失败时回到时间线取证，不通过放宽断言解决。
- 管线健康样本默认 5 分钟一次，因此真实脚本要求操作者保持唤醒直到出现下一条样本；样本缺失记 `BLOCKED`，不为了缩短验收修改生产采样频率。
- 生命周期队列容量 8 不是吞吐设计；正常场景最多需要一对 Suspend/Resume。任何溢出都视为异常健康信号并进入调查，不通过扩大为无界队列解决。
- 本轮只审查并修订实施计划；未改生产代码，未宣称自动化或真实待机验收已通过。

### 阻塞项状态

- 需要用户选择的阻塞项：**0**。
- 已由推荐裁决关闭的方案阻塞项：**6**（事件覆盖、漏 Resume 永久停工、一次 probe 假失败、观察时间失真、验收空证据假绿、Task 1 RED 提交语义冲突）。
- 实施停止线保持有效：若真实系统证明 30 秒自恢复仍无法收敛、OS 事件顺序与假设不符，或有界验证造成不可接受延迟，停止实现并带结构化证据回到本节重新审查。

## 执行收口记录（2026-08-23）

本计划已按 Task 1–8 逐项内联执行；实现与测试提交已落库：

- `3ccc972`：架构契约与生命周期门禁；`2b68069`：生命周期状态机/主循环仲裁；`a9fb6de`：powercfg 探测与切换复核；`ffe8222`：心跳成功投递推进；`f3bd5af`：状态新鲜度上限；`208df21`：真实验收入口；`2a2da8d`：Pester 套件闭合；`1f1c07b`、`6503794`、`30ba67c`：Windows PowerShell 兼容与时间戳测试稳定性。
- Task 8 目标测试已通过：Engine 42/42、Configuration 35/35、Architecture 9/9；性能复测 3/3（证据见 `.work/test-results/power-lifecycle/task-08-verification-summary.txt`）。
- 最新两次 `Run-Tests.ps1` 均为 **FAILED CLOSED**：父退出码 1，Pester 121/121，.NET 1540/1541，Tray/Installer 因上游失败而 `NotRun`。失败测试在两次运行中分别为 `User_clicks_refresh_button_and_sees_new_log_lines` 与 `User_typing_custom_range_waits_for_debounce_before_updating_view`；两者分别定向连续 5/5 通过，独立 Settings 套件已有 546/546 通过证据。这排除了固定单测回归，指向全套 WPF host 的间歇性调度/资源问题；未修改生产超时或用重试掩盖门禁。
- 真实待机矩阵已执行脚本入口但当前条件无 System 电源事件，三个场景均为 `Blocked`/退出码 2；未执行合盖、鼠标唤醒或 AC/DC 拔插，故 `PWR-LIFE-ACCEPT` 保持阻断。
- 依据 G6/G7/G8 停止线，当前交付状态为：**实现已落库，自动化全门禁阻塞，真实硬件验收未运行；不得宣称全绿或已完成发布**。下一步仅有两个可接受动作：在稳定的 WPF 测试环境重新运行完整门禁，及在真实 Engine 运行时执行 Lid/Mouse/AC/DC 矩阵；不得放宽断言或扩大超时。

## 自审记录

- Spec coverage：共同根因、空闲、计划、心跳、状态新鲜度、powercfg 真相、真实待机、性能、回滚均有对应 Task。
- Placeholder scan：计划不含未定义的延后实现项；生命周期批次、自恢复、条件探测和三态验收均已落到具体 Task、测试与门禁。
- Type consistency：`PowerLifecycleSignal -> StateMachine -> bounded Directive batch -> GuardianIterationContext` 单向；`PolicyEngine` 签名不变；`HeartbeatSchedule` 只由 Runner 消费；`PowerCfgCommandResult` 到 probe/apply result 单向。
- Scope control：无配置字段、无新 UI、无第二策略、无阻止睡眠能力。
- Honest status：本文是实施计划，不代表生产修复、自动化全绿或真实待机验收已经完成。
