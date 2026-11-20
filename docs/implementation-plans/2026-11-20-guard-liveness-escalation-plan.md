# 守护长时间停工告警 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**状态：** 第 1 版草案（2026-11-20），待外部审查与用户批准后实施。设计见 [2026-11-19-guard-liveness-escalation.md](2026-11-19-guard-liveness-escalation.md)，第 6 节 D1–D3 已由用户决定（10 分钟、停工时弹一次通知、不自动重启）。设计文档注明“经外部审查后再出实施计划”，设计本身尚未经过外部审查，本计划与设计一并送审。

**基线：** main `8967db70`（第三阶段 PR #7 已合并，main `full-gate` 通过）。已核实：
- 计划读取只在 `GuardianIterationRunner.RunAsync` 一处（`ReadCurrentPlanAsync`），之后分三条发布路径：配置不可用（`CreateDegradedObservationPayload`）、探测失败（`CreateProbeFailedObservationPayload`）、正常轮次（`RunAsync` 末尾 `new StatusPayload`）。
- 唤醒后第一轮由 `GuardianCycleCoordinator` 传入 `GuardianIterationContext.ResetIdleEpoch = true`；恢复稳定期与生命周期撤销时 `PolicyMutationAllowed()` 为 false（`blocked-by-lifecycle`）。
- 通知通道：Engine 把 `NotificationEvent` 放进状态（`NotificationEventRetention`，保留 60 秒），托盘 `TrayNotificationEvaluator` 按事件 id 去重；托盘偏好 `TrayNotificationPreferences.ShouldNotify` 对未登记的 kind 退回“计划切换”偏好。
- 状态相等判定与 Clone 有两处：`StatusPublisher.StatusPayloadEquals/Clone`、`TrayDisplayState.StatusEquals/Clone`（契约 STATUS-GUARD-CODES 要求新守护字段同时进入两处）。
- 事件名登记在 `EventNameRegistry`，通道由 `LogEventChannels.ForEventName` 决定。
- 默认配置 `CheckIntervalSec = 15`；`ForegroundRestrictionEnabled = false`。

**Goal:** Engine 存活且可守护、但计划读取在连续 10 分钟清醒时间内从未成功时，以 `guardHealth=stalled` 发布状态，托盘与设置窗口显示“守护已停工”，日志与托盘通知各一次；计划读取再次成功即恢复。

**Architecture:**
- **`GuardLivenessTracker`（新，Engine/Worker）：** 唯一职责是根据每轮“是否可守护、计划读取是否成功、是否刚唤醒”计算守护健康码与进入/恢复转换。纯内存、O(1)、注入 `TimeProvider`，由 `GuardianIterationRunner` 拥有，主轮次单线程调用。
- **`GuardianIterationRunner`：** 计划读取后调用一次 tracker；三条发布路径都带上 `guardHealth`；进入停工时写一条 `GuardStalled` 并放入一个 `guard_stalled` 通知，恢复时写一条 `GuardStallRecovered`。
- **契约类型（Contracts）：** `StatusPayload.guardHealth`（`GuardHealthStatus`）与 `GuardHealthCodes`；`NotificationKinds.GuardStalled`。
- **UI：** 托盘 `TrayStatusFormatter`、设置 `ForegroundGuardStatusText` 在状态新鲜且为 `stalled` 时显示“守护已停工（自 HH:mm，原因）”，替代“状态探测失败”；托盘对 `guard_stalled` 通知始终弹出。

**Tech Stack:** .NET 8、C#、xUnit 2.5.3、FluentAssertions 8。证据目录 `.work/guard-liveness/`。

---

## 判定规则（实现的唯一依据）

每个完整轮次调用一次 `Observe(applicable, planReadSucceeded, failureKind, resumed)`：

1. **计划读取成功：** 清空最近失败分类，以当前时刻重开计时窗口；若此前处于停工，产生一次 `Recovered`（时长 = 窗口起点到现在）。
2. **计划读取失败：** 记录最近失败分类；然后：
   - 尚无窗口（Engine 刚启动），或未停工且（刚唤醒或本轮不可守护）→ 以当前时刻重开窗口；
   - 未停工、本轮可守护、自窗口起点的单调时长 ≥ 10 分钟 → 进入停工，产生一次 `Entered`。
3. **发布码：** 本轮不可守护 → `not-applicable`；已停工 → `stalled`（`stalledSinceUtc` = 窗口起点的 UTC 时刻）；否则 `healthy`。

**可守护** = 配置可用 且 未暂停 且 `PolicyMutationAllowed()`（排除 `paused`、`config-unavailable`、`blocked-by-lifecycle`，与设计 3.2 一致）。

**两条由“恢复以读取成功为准”推出的规则（设计未明写，计划补充）：**
- 停工一旦进入，只有计划读取成功才能退出。唤醒与不可守护只重开**未停工**时的窗口，不撤销已进入的停工，否则会变成“以时间推断恢复”。
- 不可守护轮次中的读取成功同样算恢复：读取是事实，与本轮是否守护无关。

---

## 相对设计的取舍（送审时请一并确认）

| # | 设计原文 | 本计划 | 理由 |
|---|----------|--------|------|
| 1 | 托盘显示“守护已停工 N 分钟” | 显示“守护已停工（自 HH:mm，原因）” | 托盘只在状态字段变化时重算文案（`TrayDisplayState.Apply` 相等即跳过），“N 分钟”会停在第一次的数字；起始时刻是稳定事实，不需要定时刷新 |
| 2 | `guardHealth` 含 `observedAtUtc` | 不含 | 守护健康是守护负载的一部分，新鲜度跟随状态 `timestamp`（`GuardStatusFreshness`）；`logHealth` 需要独立时间是因为它有轻路径 `PublishLogHealth`，守护健康没有 |
| 3 | D2 复用现有 Toast 通道 | `guard_stalled` 不受“计划切换通知”开关控制，始终弹出 | 现有偏好只有“计划切换”“外部变更”两个开关，未登记的 kind 退回“计划切换”开关；用户关闭计划切换通知不代表不想知道守护停工。**需要用户确认** |
| 4 | 日志“同一次停工期间不重复” | 进入/恢复各写一次；写入被拒绝不重试 | 状态与通知是主通道；重试需要额外的待写状态，属 YAGNI。日志写入失败本身已由 `logHealth` 可见 |

---

## 变更影响（Change Impact Matrix）

| 影响域 | 结论 |
|--------|------|
| Function | 新增停工判定、展示与一次性通知；守护决策、探测预算、冷却与合并告警不变 |
| Architecture | 新增 `GuardLivenessTracker`（Engine/Worker）；Owner 为 `GuardianIterationRunner`，发布仍经 `StatusPublisher`，读取仍经共享 reader |
| Data | `status.json` 新增可选对象 `guardHealth`（约 100 字节）；旧读者忽略；无迁移、无配置项 |
| Performance | 每轮常数次比较与两次时间读取；无新线程、定时器、队列或 IO |
| Resource | tracker 固定 5 个字段 |
| Concurrency & Time | 单线程（主轮次）；窗口用注入 `TimeProvider` 单调时间；`stalledSinceUtc` 为 UTC；唤醒重开未停工窗口 |
| Reliability | “进程活着但核心能力持续停工”首次有确定健康码；恢复只认读取成功 |
| Observability | 新事件 `GuardStalled`（Warn）、`GuardStallRecovered`（Info），通道 Recovery；新通知 kind `guard_stalled` |
| Compatibility | 新 UI 读旧状态：缺字段时文案与今天相同（“状态探测失败”），不宣称健康；旧 UI 读新状态：忽略新字段 |
| Security / Operations | 无变化；不自动重启（D3） |

---

## 文件结构

| 文件 | 动作 | 责任 |
|------|------|------|
| `src/SmartGuard.Contracts/GuardHealthStatus.cs` | 新建 | `GuardHealthStatus` 记录与 `GuardHealthCodes`（健康码、失败分类码） |
| `src/SmartGuard.Contracts/StatusModels.cs` | 修改 | `StatusPayload.guardHealth`；`NotificationKinds.GuardStalled` |
| `src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs` | 修改 | 相等判定与 Clone 纳入 `guardHealth` |
| `src/SmartGuard.Tray/TrayDisplayState.cs` | 修改 | 相等判定与 Clone 纳入 `guardHealth` |
| `src/SmartGuard.Engine/Worker/GuardLivenessTracker.cs` | 新建 | 判定规则 |
| `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs` | 修改 | 计划读取后调用 tracker；三处负载带 `guardHealth` |
| `src/SmartGuard.Engine/Worker/GuardianIterationRunner.Liveness.cs` | 新建 | tracker 字段、进入/恢复日志与通知 |
| `src/SmartGuard.Configuration/EventNameRegistry.cs`、`LogEventChannels.cs` | 修改 | 两个事件名与 Recovery 通道 |
| `src/SmartGuard.Tray/TrayStatusFormatter.cs`、`TrayNotificationPreferences.cs` | 修改 | 停工子句；`guard_stalled` 始终通知 |
| `src/SmartGuard.Settings/ForegroundGuardStatusText.cs` | 修改 | 停工文案 |
| `Tests/SmartGuard.Engine.Tests/StatusPublisherTests.cs` | 修改 | 只变 `guardHealth` 也写入；轻路径保留 `guardHealth` |
| `Tests/SmartGuard.Tray.Tests/TrayGuardHealthDisplayTests.cs` | 新建 | 托盘相等/Clone、文案、通知偏好 |
| `Tests/SmartGuard.Engine.Tests/GuardLivenessTrackerTests.cs` | 新建 | 判定规则 |
| `Tests/SmartGuard.Engine.Tests/GuardLivenessRunnerTests.cs` | 新建 | 轮次集成：状态、日志、通知各一次 |
| `Tests/SmartGuard.Settings.Tests/ForegroundGuardStatusTextTests.cs` | 修改 | 停工文案 |
| `Tests/SmartGuard.Architecture.Tests/GuardLivenessArchitectureTests.cs` | 新建 | 门禁 |
| `docs/ARCHITECTURE-CONTRACT.md`、设计文档 | 修改 | `GUARD-INV-LIVENESS`、真源行、实施记录 |

**通用约束：**
- UTF-8 无 BOM；精确补丁；只暂存本任务文件；Conventional Commits，结尾 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。
- 每个 RED 前先 `dotnet build` 受影响测试项目并确认退出码为 0；RED 必须因行为缺口失败。
- 每个 GREEN 运行所有受影响项目，**包括 Architecture**（源码扫描门禁可能被任何任务改动触发）。
- 本计划不修改 `.ps1`。测试只读真实电源设置：轮次集成测试一律包在 `PowerSchemeTestScope.WithNative(new FakePowrProfNative(), …)` 中，并注入 `readCurrentPlan`。
- 受影响测试项目：Configuration、Engine、Tray、Settings、Architecture。

---

### Task 0: 基线

- [ ] **Step 1:**

```powershell
New-Item -ItemType Directory -Force .work\guard-liveness | Out-Null
foreach ($p in 'Configuration','Engine','Tray','Settings','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task0-baseline-$p.trx" --results-directory .work/guard-liveness }
foreach ($p in 'Configuration','Engine','Tray','Settings','Architecture') { [xml]$x = Get-Content -Encoding UTF8 ".work/guard-liveness/task0-baseline-$p.trx"; $c = $x.TestRun.ResultSummary.Counters; "$p total=$($c.total) passed=$($c.passed) failed=$($c.failed)" }
```

Expected: 全部通过（Configuration 有 1 个受写入保护的未执行用例），记录各项目计数。计数一律从 TRX 读取，不依赖本地化的控制台输出。

---

### Task 1: 状态字段与两处相等判定

**Files:** `GuardHealthStatus.cs`（新建）、`StatusModels.cs`、`StatusPublisher.cs`、`TrayDisplayState.cs`、`StatusPublisherTests.cs`、`TrayGuardHealthDisplayTests.cs`（新建）

- [ ] **Step 1: 契约类型（脚手架，无行为）**

`src/SmartGuard.Contracts/GuardHealthStatus.cs`：

```csharp
using System.Text.Json.Serialization;

namespace SmartGuard.Contracts;

/// <summary>
/// 守护健康（GUARD-INV-LIVENESS）：Engine 存活且可守护时，计划读取是否在连续清醒时间内持续失败。
/// 新鲜度跟随所在状态的 timestamp（GuardStatusFreshness）；旧状态缺字段为未知，UI 不据此报警也不宣称健康。
/// </summary>
public sealed record GuardHealthStatus(
  [property: JsonPropertyName("code")] string Code,
  [property: JsonPropertyName("stalledSinceUtc")] DateTimeOffset? StalledSinceUtc,
  [property: JsonPropertyName("lastFailureKind")] string? LastFailureKind);

public static class GuardHealthCodes
{
  public const string Healthy = "healthy";
  public const string Stalled = "stalled";
  public const string NotApplicable = "not-applicable";

  /// <summary>最近一次计划读取失败的稳定分类；展示文案只在 UI 层映射。</summary>
  public static class FailureKinds
  {
    public const string ApiError = "api-error";
    public const string CallPending = "call-pending";
    public const string TimedOut = "timed-out";
    public const string Other = "other";
  }

  /// <summary>只有新鲜状态里登记过的 stalled 才算停工；其余一律不是确定的停工。</summary>
  public static bool IsStalled(StatusPayload? status, DateTimeOffset nowUtc)
    => status?.guardHealth is { Code: Stalled, StalledSinceUtc: not null }
      && GuardStatusFreshness.IsFresh(status.timestamp, nowUtc);
}
```

`StatusModels.cs`：`StatusPayload` 末尾 `logHealth` 之后加入

```csharp
  /// <summary>可选守护健康（GUARD-INV-LIVENESS）；旧状态缺字段时未知。</summary>
  public GuardHealthStatus? guardHealth { get; set; }
```

`NotificationKinds` 加入 `public const string GuardStalled = "guard_stalled";`。

- [ ] **Step 2: 写失败测试**

`StatusPublisherTests` 追加：

```csharp
    [Fact]
    public void Publish_writes_when_only_guard_health_changes()
    {
        var publisher = new StatusPublisher(_statusPath);
        var planGuid = Guid.NewGuid().ToString();
        publisher.Publish(CreatePayload(idleSeconds: 10, batteryPercent: 80, planGuid));
        var stalled = CreatePayload(idleSeconds: 10, batteryPercent: 80, planGuid);
        stalled.guardHealth = new GuardHealthStatus(GuardHealthCodes.Stalled,
            new DateTimeOffset(2026, 11, 20, 1, 0, 0, TimeSpan.Zero), GuardHealthCodes.FailureKinds.CallPending);

        publisher.Publish(stalled);

        File.ReadAllText(_statusPath).Should().Contain("\"stalled\"", "a stall is a meaningful change and must not wait for the 60 s refresh");
    }

    [Fact]
    public void Log_health_light_path_keeps_the_guard_health()
    {
        var publisher = new StatusPublisher(_statusPath);
        var payload = CreatePayload(idleSeconds: 10, batteryPercent: 80, Guid.NewGuid().ToString());
        payload.guardHealth = new GuardHealthStatus(GuardHealthCodes.Stalled,
            new DateTimeOffset(2026, 11, 20, 1, 0, 0, TimeSpan.Zero), GuardHealthCodes.FailureKinds.CallPending);
        publisher.Publish(payload);

        publisher.PublishLogHealth(new LogHealthStatus(LogHealthCodes.Degraded, DateTimeOffset.UtcNow, 1, 1, 0, 0, 0, 0));

        File.ReadAllText(_statusPath).Should().Contain("\"degraded\"").And.Contain("\"stalled\"",
            "the light path clones the last guard payload and must not drop guardHealth");
    }
```

`Tests/SmartGuard.Tray.Tests/TrayGuardHealthDisplayTests.cs`（本步只放相等/Clone 两例，Task 4 再追加文案与通知用例）：

```csharp
using SmartGuard.Contracts;

namespace SmartGuard.Tray.Tests;

public class TrayGuardHealthDisplayTests
{
  private static readonly DateTimeOffset StalledSince = new(2026, 11, 20, 1, 0, 0, TimeSpan.Zero);

  [Fact]
  public void Apply_updates_when_only_guard_health_changes()
  {
    var state = new TrayDisplayState();
    state.Apply(Status(health: null)).Should().BeTrue();
    state.Apply(Status(Stalled())).Should().BeTrue("a stall alone must refresh the tray");
  }

  [Fact]
  public void Clone_preserves_guard_health_so_equal_statuses_stay_skipped()
  {
    var state = new TrayDisplayState();
    state.Apply(Status(Stalled())).Should().BeTrue();
    state.Apply(Status(Stalled())).Should().BeFalse("the stored clone must keep guardHealth for the equality check");
  }

  internal static GuardHealthStatus Stalled(string kind = GuardHealthCodes.FailureKinds.CallPending)
    => new(GuardHealthCodes.Stalled, StalledSince, kind);

  internal static StatusPayload Status(GuardHealthStatus? health, string reason = GuardStatusCodes.Reasons.ProbeFailed)
    => new()
    {
      timestamp = DateTimeOffset.UtcNow.ToString("O"),
      currentPlan = "平衡",
      batteryPercent = 50,
      brightness = 60,
      guardSource = GuardStatusCodes.Sources.None,
      guardReason = reason,
      logHealth = new LogHealthStatus(LogHealthCodes.Healthy, DateTimeOffset.UtcNow, 1, 0, 0, 0, 0, 0),
      guardHealth = health,
    };
}
```

- [ ] **Step 3: RED**

```powershell
foreach ($p in 'Engine','Tray') { dotnet build "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore -v q; "build $p exit=$LASTEXITCODE" }
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-build --filter "FullyQualifiedName~StatusPublisherTests" --logger "trx;LogFileName=task1-red-engine.trx" --results-directory .work/guard-liveness
dotnet test Tests/SmartGuard.Tray.Tests/SmartGuard.Tray.Tests.csproj --no-build --filter "FullyQualifiedName~TrayGuardHealthDisplayTests" --logger "trx;LogFileName=task1-red-tray.trx" --results-directory .work/guard-liveness
```

Expected（4 例全部失败，均因行为缺口）：
- `Publish_writes_when_only_guard_health_changes`：相等判定忽略 `guardHealth`，第二次发布被跳过，文件中没有 `stalled`。
- `Log_health_light_path_keeps_the_guard_health`：`Clone` 丢掉 `guardHealth`。
- `Apply_updates_when_only_guard_health_changes`：托盘相等判定忽略它，返回 false。
- `Clone_preserves_guard_health…`：两个状态都带 `stalled`，判定却仍相等。这一例在旧代码上**通过**（字段被忽略，两次都相等），记为保护性用例；它在 GREEN 后锁定“Clone 保留字段”。

- [ ] **Step 4: 实现**

`StatusPublisher.StatusPayloadEquals` 在 `logHealth` 那一行之后加入 `&& Equals(a.guardHealth, b.guardHealth)`；`Clone` 加入 `guardHealth = payload.guardHealth,`（记录不可变，可共享引用）。

`TrayDisplayState.StatusEquals` 加入 `&& Equals(left.guardHealth, right.guardHealth)`；`Clone` 加入 `guardHealth = status.guardHealth,`。

- [ ] **Step 5: GREEN**

```powershell
foreach ($p in 'Engine','Tray','Settings','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task1-green-$p.trx" --results-directory .work/guard-liveness }
```

Expected: 全部通过。

- [ ] **Step 6: 提交** `feat(status): carry guard health through status equality and clones`

---

### Task 2: `GuardLivenessTracker` 判定

**Files:** 新建 `src/SmartGuard.Engine/Worker/GuardLivenessTracker.cs`、`Tests/SmartGuard.Engine.Tests/GuardLivenessTrackerTests.cs`

- [ ] **Step 1: 骨架（使 RED 可编译）**

```csharp
using SmartGuard.Contracts;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal enum GuardLivenessTransition { None, Entered, Recovered }

internal readonly record struct GuardLivenessObservation(
  GuardLivenessTransition Transition, TimeSpan Duration, string? LastFailureKind);

/// <summary>Scaffold only; the rule arrives with its tests.</summary>
internal sealed class GuardLivenessTracker(TimeProvider time)
{
  internal static readonly TimeSpan Threshold = TimeSpan.FromMinutes(10);

  internal GuardHealthStatus Current { get; private set; } = new(GuardHealthCodes.Healthy, null, null);

  internal GuardLivenessObservation Observe(bool applicable, bool planReadSucceeded, string? failureKind, bool resumed)
    => new(GuardLivenessTransition.None, TimeSpan.Zero, null);

  internal static string FailureKindCode(PowerPlanFailureKind kind) => GuardHealthCodes.FailureKinds.Other;
}
```

- [ ] **Step 2: 写失败测试**

```csharp
using SmartGuard.Contracts;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class GuardLivenessTrackerTests
{
  private static readonly TimeSpan Round = TimeSpan.FromSeconds(15);

  private static (ManualTimeProvider Clock, GuardLivenessTracker Tracker) Create()
  {
    var clock = new ManualTimeProvider();
    return (clock, new GuardLivenessTracker(clock));
  }

  // One failing, applicable round per call; returns the transition of the last round.
  private static GuardLivenessObservation FailFor(ManualTimeProvider clock, GuardLivenessTracker tracker, TimeSpan span,
    string kind = GuardHealthCodes.FailureKinds.CallPending)
  {
    var last = tracker.Observe(true, false, kind, false);
    for (var elapsed = TimeSpan.Zero; elapsed < span; elapsed += Round)
    {
      clock.Advance(Round);
      last = tracker.Observe(true, false, kind, false);
    }
    return last;
  }

  [Fact]
  public void Failing_reads_stay_healthy_below_the_threshold_and_stall_at_it_exactly_once()
  {
    var (clock, tracker) = Create();
    FailFor(clock, tracker, GuardLivenessTracker.Threshold - Round);
    tracker.Current.Code.Should().Be(GuardHealthCodes.Healthy);

    clock.Advance(Round);
    tracker.Observe(true, false, GuardHealthCodes.FailureKinds.CallPending, false)
      .Transition.Should().Be(GuardLivenessTransition.Entered);
    tracker.Current.Should().Be(new GuardHealthStatus(GuardHealthCodes.Stalled,
      new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero), GuardHealthCodes.FailureKinds.CallPending));

    clock.Advance(Round);
    tracker.Observe(true, false, GuardHealthCodes.FailureKinds.CallPending, false)
      .Transition.Should().Be(GuardLivenessTransition.None, "a stall is entered once");
    tracker.Current.Code.Should().Be(GuardHealthCodes.Stalled);
  }

  [Fact]
  public void One_success_restarts_the_window()
  {
    var (clock, tracker) = Create();
    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    clock.Advance(Round);
    tracker.Observe(true, true, null, false);
    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    tracker.Current.Code.Should().Be(GuardHealthCodes.Healthy);
  }

  [Fact]
  public void Not_applicable_rounds_publish_not_applicable_and_restart_the_window()
  {
    var (clock, tracker) = Create();
    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    clock.Advance(TimeSpan.FromHours(1));
    tracker.Observe(false, false, GuardHealthCodes.FailureKinds.CallPending, false);
    tracker.Current.Code.Should().Be(GuardHealthCodes.NotApplicable);

    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    tracker.Current.Code.Should().Be(GuardHealthCodes.Healthy, "time while guarding was not expected never counts");
  }

  [Fact]
  public void Resume_restarts_a_window_that_has_not_stalled()
  {
    var (clock, tracker) = Create();
    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    clock.Advance(TimeSpan.FromHours(8));   // the monotonic clock may or may not count sleep
    tracker.Observe(true, false, GuardHealthCodes.FailureKinds.CallPending, resumed: true);
    tracker.Current.Code.Should().Be(GuardHealthCodes.Healthy);
    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    tracker.Current.Code.Should().Be(GuardHealthCodes.Healthy);
  }

  [Fact]
  public void A_stall_survives_resume_and_pauses_and_ends_only_with_a_successful_read()
  {
    var (clock, tracker) = Create();
    FailFor(clock, tracker, GuardLivenessTracker.Threshold);
    tracker.Current.Code.Should().Be(GuardHealthCodes.Stalled);

    clock.Advance(Round);
    tracker.Observe(true, false, GuardHealthCodes.FailureKinds.ApiError, resumed: true);
    tracker.Current.Code.Should().Be(GuardHealthCodes.Stalled, "recovery is never inferred from time or a wake");
    tracker.Current.LastFailureKind.Should().Be(GuardHealthCodes.FailureKinds.ApiError);
    clock.Advance(Round);
    tracker.Observe(false, false, GuardHealthCodes.FailureKinds.ApiError, false);
    tracker.Current.Code.Should().Be(GuardHealthCodes.NotApplicable);
    clock.Advance(Round);
    tracker.Observe(true, false, GuardHealthCodes.FailureKinds.ApiError, false);
    tracker.Current.Code.Should().Be(GuardHealthCodes.Stalled);

    clock.Advance(Round);
    var recovered = tracker.Observe(false, true, null, false);   // a read in a paused round still counts
    recovered.Transition.Should().Be(GuardLivenessTransition.Recovered);
    recovered.Duration.Should().Be(GuardLivenessTracker.Threshold + 4 * Round);
    tracker.Current.Code.Should().Be(GuardHealthCodes.NotApplicable);
    clock.Advance(Round);
    tracker.Observe(true, true, null, false).Transition.Should().Be(GuardLivenessTransition.None);
    tracker.Current.Should().Be(new GuardHealthStatus(GuardHealthCodes.Healthy, null, null));
  }

  // PowerPlanFailureKind is internal and xUnit only runs public methods, so the kind travels as an int.
  [Theory]
  [InlineData((int)PowerPlanFailureKind.ApiFailed, GuardHealthCodes.FailureKinds.ApiError)]
  [InlineData((int)PowerPlanFailureKind.CallPending, GuardHealthCodes.FailureKinds.CallPending)]
  [InlineData((int)PowerPlanFailureKind.TimedOut, GuardHealthCodes.FailureKinds.TimedOut)]
  [InlineData((int)PowerPlanFailureKind.NotAdmitted, GuardHealthCodes.FailureKinds.Other)]
  public void Failure_kinds_map_to_stable_codes(int kind, string code)
    => GuardLivenessTracker.FailureKindCode((PowerPlanFailureKind)kind).Should().Be(code);
}
```

（`FailFor` 首轮在调用时刻、之后每 15 秒一轮；“失败 9 分钟”约 37 轮。）

- [ ] **Step 3: RED**

```powershell
dotnet build Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore -v q; "build exit=$LASTEXITCODE"
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-build --filter "FullyQualifiedName~GuardLivenessTrackerTests" --logger "trx;LogFileName=task2-red.trx" --results-directory .work/guard-liveness
```

Expected：
- `Failing_reads…`、`A_stall_survives…`：骨架恒为 `healthy`/`None`，失败。
- `Not_applicable_rounds…`：骨架不产生 `not-applicable`，失败。
- `Failure_kinds…`：除 `NotAdmitted → other` 外三例失败。
- `One_success…`、`Resume_restarts…`：骨架恒为 `healthy`，通过——记为保护性用例（GREEN 后锁定“成功与唤醒重开窗口”，防止误报）。

- [ ] **Step 4: 实现**（整体替换骨架类体）

```csharp
/// <summary>
/// Owns the guard liveness rule (GUARD-INV-LIVENESS). Called once per full round on the main round thread.
/// Stall = applicable rounds whose plan reads never succeeded for Threshold of monotonic time since the later
/// of the last success, the last wake or the last non-applicable round. A stall ends only with a successful
/// read; wakes and non-applicable rounds never end it.
/// </summary>
internal sealed class GuardLivenessTracker(TimeProvider time)
{
  internal static readonly TimeSpan Threshold = TimeSpan.FromMinutes(10);

  private long? _windowStart;
  private DateTimeOffset _windowStartUtc;
  private bool _stalled;
  private string? _lastFailureKind;

  internal GuardHealthStatus Current { get; private set; } = new(GuardHealthCodes.Healthy, null, null);

  internal GuardLivenessObservation Observe(bool applicable, bool planReadSucceeded, string? failureKind, bool resumed)
  {
    var now = time.GetTimestamp();
    var nowUtc = time.GetUtcNow();
    var transition = GuardLivenessTransition.None;
    var duration = TimeSpan.Zero;
    if (planReadSucceeded)
    {
      if (_stalled)
      {
        transition = GuardLivenessTransition.Recovered;
        duration = time.GetElapsedTime(_windowStart!.Value, now);
      }
      _stalled = false;
      _lastFailureKind = null;
      Restart(now, nowUtc);
    }
    else
    {
      _lastFailureKind = failureKind;
      if (_windowStart is null || (!_stalled && (resumed || !applicable)))
        Restart(now, nowUtc);
      else if (!_stalled && applicable && time.GetElapsedTime(_windowStart.Value, now) >= Threshold)
      {
        _stalled = true;
        transition = GuardLivenessTransition.Entered;
        duration = time.GetElapsedTime(_windowStart.Value, now);
      }
    }
    Current = !applicable ? new(GuardHealthCodes.NotApplicable, null, null)
      : _stalled ? new(GuardHealthCodes.Stalled, _windowStartUtc, _lastFailureKind)
      : new(GuardHealthCodes.Healthy, null, null);
    return new(transition, duration, _lastFailureKind);
  }

  private void Restart(long now, DateTimeOffset nowUtc)
  {
    _windowStart = now;
    _windowStartUtc = nowUtc;
  }

  internal static string FailureKindCode(PowerPlanFailureKind kind) => kind switch
  {
    PowerPlanFailureKind.ApiFailed => GuardHealthCodes.FailureKinds.ApiError,
    PowerPlanFailureKind.CallPending => GuardHealthCodes.FailureKinds.CallPending,
    PowerPlanFailureKind.TimedOut => GuardHealthCodes.FailureKinds.TimedOut,
    _ => GuardHealthCodes.FailureKinds.Other,
  };
}
```

- [ ] **Step 5: GREEN**：Engine、Architecture 全部通过。
- [ ] **Step 6: 提交** `feat(engine): judge guard liveness from plan reads over awake time`

---

### Task 3: 轮次集成、事件名与通知

**Files:** `GuardianIterationRunner.cs`、新建 `GuardianIterationRunner.Liveness.cs`、`EventNameRegistry.cs`、`LogEventChannels.cs`、新建 `Tests/SmartGuard.Engine.Tests/GuardLivenessRunnerTests.cs`

- [ ] **Step 1: 事件名（脚手架，使 RED 可编译）**

`EventNameRegistry` 加入常量 `GuardStalled`、`GuardStallRecovered`，**暂不** `Register`、暂不映射通道（Step 4 再加，使登记缺口成为 RED）。

- [ ] **Step 2: 写失败测试**

```csharp
using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

// Shares the collection of the other round tests: PowerSchemeTestScope swaps a process-wide executor.
[Collection("LoggingFallbackTests")]
public sealed class GuardLivenessRunnerTests
{
  private static readonly TimeSpan Round = TimeSpan.FromSeconds(15);

  private sealed class Harness : IDisposable
  {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SmartGuardLiveness_" + Guid.NewGuid().ToString("N"));
    internal readonly ManualTimeProvider Clock = new();
    internal readonly List<(string Name, IReadOnlyDictionary<string, JsonElement>? Properties)> Events = [];
    internal readonly GuardianLoopIterationState State = new() { PlanCatalog = new Dictionary<Guid, string>() };
    internal readonly GuardConfig Config;
    internal readonly GuardianIterationRunner Runner;
    internal bool ReadSucceeds;
    internal string StatusPath => Path.Combine(_root, "status.json");

    internal Harness(bool paused = false)
    {
      Directory.CreateDirectory(_root);
      Config = GuardConfig.CreateDefault(_root);
      Config.Paused = paused;
      // An empty program list keeps every round a non-switching decision: liveness never depends on it.
      Config.ForegroundRestrictionEnabled = true;
      Runner = new GuardianIterationRunner(new IdleTracker(), new BrightnessService(), new StatusPublisher(StatusPath, Clock),
        () => GuardConfigReadResult.FromConfig(Config),
        (_, _, _, eventName, properties, _) => { Events.Add((eventName, properties)); return GuardianLogWriteResult.AcceptedByPipeline; },
        _ => { }, () => true, new HeartbeatSchedule(), readIdleSeconds: () => 0,
        readBatteryInfo: _ => (90, true),
        readCurrentPlan: () => ReadSucceeds
          ? new(true, new PowerSchemeInfo(Config.ActivePlanGuid, null), PowerPlanFailureKind.None, "")
          : new(false, null, PowerPlanFailureKind.CallPending, ""),
        readBrightness: () => 50, timeProvider: Clock,
        applyPlan: (_, _) => throw new InvalidOperationException("Fixture forbids physical plan writes"));
    }

    internal async Task RoundsAsync(TimeSpan span, GuardianIterationContext? context = null)
    {
      await Runner.RunAsync(State, context ?? GuardianIterationContext.Running, CancellationToken.None);
      for (var elapsed = TimeSpan.Zero; elapsed < span; elapsed += Round)
      {
        Clock.Advance(Round);
        await Runner.RunAsync(State, context ?? GuardianIterationContext.Running, CancellationToken.None);
      }
    }

    internal StatusPayload Status()
      => JsonSerializer.Deserialize<StatusPayload>(File.ReadAllText(StatusPath))!;

    internal int Count(string name) => Events.Count(e => e.Name == name);

    public void Dispose() => Directory.Delete(_root, true);
  }

  private static Task InFakeNative(Func<Task> run) => PowerSchemeTestScope.WithNative(new FakePowrProfNative(), run);

  [Fact]
  public Task Ten_minutes_of_failed_reads_publish_one_stall_one_warning_and_one_notification() => InFakeNative(async () =>
  {
    using var h = new Harness();
    await h.RoundsAsync(GuardLivenessTracker.Threshold - Round);
    h.Status().guardHealth!.Code.Should().Be(GuardHealthCodes.Healthy);
    h.Count(EventNameRegistry.GuardStalled).Should().Be(0);

    await h.RoundsAsync(TimeSpan.FromMinutes(2));
    var status = h.Status();
    status.guardHealth!.Code.Should().Be(GuardHealthCodes.Stalled);
    status.guardHealth.LastFailureKind.Should().Be(GuardHealthCodes.FailureKinds.CallPending);
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.ProbeFailed);
    status.notificationEvent!.kind.Should().Be(NotificationKinds.GuardStalled);
    h.Count(EventNameRegistry.GuardStalled).Should().Be(1, "one stall is logged once");
    h.Events.Single(e => e.Name == EventNameRegistry.GuardStalled).Properties!["lastFailureKind"].GetString()
      .Should().Be(GuardHealthCodes.FailureKinds.CallPending);
  });

  [Fact]
  public Task A_successful_read_ends_the_stall_once() => InFakeNative(async () =>
  {
    using var h = new Harness();
    await h.RoundsAsync(GuardLivenessTracker.Threshold + Round);
    h.ReadSucceeds = true;
    await h.RoundsAsync(TimeSpan.FromMinutes(1));
    h.Status().guardHealth!.Code.Should().Be(GuardHealthCodes.Healthy);
    h.Count(EventNameRegistry.GuardStallRecovered).Should().Be(1);
    h.Count(EventNameRegistry.GuardStalled).Should().Be(1);
  });

  [Fact]
  public Task Paused_guarding_is_not_applicable_and_never_stalls() => InFakeNative(async () =>
  {
    using var h = new Harness(paused: true);
    await h.RoundsAsync(TimeSpan.FromMinutes(30));
    h.Status().guardHealth!.Code.Should().Be(GuardHealthCodes.NotApplicable);
    h.Count(EventNameRegistry.GuardStalled).Should().Be(0);
  });

  [Fact]
  public Task Lifecycle_blocked_rounds_are_not_applicable() => InFakeNative(async () =>
  {
    using var h = new Harness();
    await h.RoundsAsync(TimeSpan.FromMinutes(30),
      new GuardianIterationContext(false, false, false, false, PowerLifecyclePhase.ResumeStabilizing));
    h.Status().guardHealth!.Code.Should().Be(GuardHealthCodes.NotApplicable);
    h.Count(EventNameRegistry.GuardStalled).Should().Be(0);
  });

  [Theory]
  [InlineData("GuardStalled")]
  [InlineData("GuardStallRecovered")]
  public void Stall_events_are_registered_on_the_recovery_channel(string eventName)
  {
    EventNameRegistry.IsRegistered(eventName).Should().BeTrue();
    LogEventChannels.ForEventName(eventName).Should().Be(LogChannelRegistry.Recovery);
  }
}
```

- [ ] **Step 3: RED**

```powershell
dotnet build Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore -v q; "build exit=$LASTEXITCODE"
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-build --filter "FullyQualifiedName~GuardLivenessRunnerTests" --logger "trx;LogFileName=task3-red.trx" --results-directory .work/guard-liveness
```

Expected：
- 4 个轮次用例失败：`guardHealth` 为 null（runner 尚未调用 tracker 也未写入负载）。
- 2 个事件登记用例失败：未登记，通道落到默认 Diagnostics。

- [ ] **Step 4: 实现**

`EventNameRegistry` 静态构造函数加入 `Register(GuardStalled); Register(GuardStallRecovered);`。`LogEventChannels` 的 Recovery 分支加入 `or EventNameRegistry.GuardStalled or EventNameRegistry.GuardStallRecovered`。

新建 `src/SmartGuard.Engine/Worker/GuardianIterationRunner.Liveness.cs`：

```csharp
using System.Diagnostics;
using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal sealed partial class GuardianIterationRunner
{
  // Owned by the main round; shares the runner's TimeProvider so the window and the round use one clock.
  private readonly GuardLivenessTracker _liveness = new(timeProvider ?? TimeProvider.System);

  private void ObserveLiveness(GuardConfig? config, GuardianLoopIterationState state, bool applicable,
    PowerPlanProbeResult planProbe, bool resumed)
  {
    var observed = _liveness.Observe(applicable, planProbe.Succeeded,
      planProbe.Succeeded ? null : GuardLivenessTracker.FailureKindCode(planProbe.FailureKind), resumed);
    var minutes = (long)observed.Duration.TotalMinutes;
    var stalledMs = JsonSerializer.SerializeToElement((long)observed.Duration.TotalMilliseconds);
    if (observed.Transition == GuardLivenessTransition.Entered)
    {
      var reason = DescribeStallReason(observed.LastFailureKind);
      var properties = new Dictionary<string, JsonElement>
      {
        ["stalledMs"] = stalledMs,
        ["lastFailureKind"] = JsonSerializer.SerializeToElement(observed.LastFailureKind ?? GuardHealthCodes.FailureKinds.Other),
      };
      if (PowerSchemeService.InFlightSince is long since)
        properties["schemeCallInFlightMs"] = JsonSerializer.SerializeToElement((long)Stopwatch.GetElapsedTime(since).TotalMilliseconds);
      writeLog(config, LogLevel.Warn,
        $"守护已停工：已连续 {minutes} 分钟无法读取当前电源计划（{reason}），自动守护暂时无法切换计划。",
        EventNameRegistry.GuardStalled, properties, null);
      // One notification per stall: the tray dedupes by event id; this path bypasses the plan-change preference.
      var stall = new NotificationEvent
      {
        kind = NotificationKinds.GuardStalled,
        title = "智能电源守护已停工",
        body = $"已连续 {minutes} 分钟无法读取电源计划（{reason}），自动守护暂时无法工作。",
      };
      var now = DateTime.Now;
      state.LastEmittedNotification = stall;
      state.LastEmittedNotificationAt = now;
      state.NotificationRetention = NotificationEventRetention.Advance(stall, state.NotificationRetention, now);
    }
    else if (observed.Transition == GuardLivenessTransition.Recovered)
    {
      writeLog(config, LogLevel.Info, $"守护已恢复：电源计划读取恢复正常，此前停工 {minutes} 分钟。",
        EventNameRegistry.GuardStallRecovered,
        new Dictionary<string, JsonElement> { ["stalledMs"] = stalledMs }, null);
    }
  }

  private static string DescribeStallReason(string? failureKind) => failureKind switch
  {
    GuardHealthCodes.FailureKinds.CallPending => "电源服务调用一直未返回",
    GuardHealthCodes.FailureKinds.ApiError => "电源服务调用失败",
    GuardHealthCodes.FailureKinds.TimedOut => "电源服务调用超时",
    _ => "原因未知",
  };
}
```

`GuardianIterationRunner.RunAsync` 中，`var current = activePlanInfo?.Guid;` 之后加入：

```csharp
    // GUARD-INV-LIVENESS: one observation per full round, before any branch publishes.
    ObserveLiveness(configOpt, state,
      applicable: configAvailable && !configOpt!.Paused && PolicyMutationAllowed(),
      planProbe, resumed: context.ResetIdleEpoch);
```

三处负载各加一行 `guardHealth = _liveness.Current,`：`RunAsync` 末尾的 `new StatusPayload`、`CreateDegradedObservationPayload`、`CreateProbeFailedObservationPayload`。

（进入停工只可能发生在读取失败的轮次，即探测失败路径；该路径直接发布 `state.NotificationRetention.Event`，因此通知在同一轮进入状态。正常轮次末尾的 `NotificationEventRetention.Advance` 在 60 秒保留期后自然清除它。）

- [ ] **Step 5: GREEN**

```powershell
foreach ($p in 'Configuration','Engine','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task3-green-$p.trx" --results-directory .work/guard-liveness }
```

Expected: 全部通过；`GuardianProbeHardeningTests`、`GuardianPowerLifecycleIntegrationTests` 等经同一 runner 的用例不受影响（它们不断言 `guardHealth`）。

- [ ] **Step 6: 提交** `feat(engine): publish guard stalls with one warning and one notification`

---

### Task 4: 托盘与设置窗口展示

**Files:** `TrayStatusFormatter.cs`、`TrayNotificationPreferences.cs`、`ForegroundGuardStatusText.cs`、`TrayGuardHealthDisplayTests.cs`、`ForegroundGuardStatusTextTests.cs`

- [ ] **Step 1: 写失败测试**

`TrayGuardHealthDisplayTests` 追加：

```csharp
  [Fact]
  public void Stall_replaces_the_probe_failure_clause_with_its_start_and_reason()
  {
    var line = TrayStatusFormatter.FormatStatusLine(Status(Stalled()));
    line.Should().Contain($"守护已停工（自 {StalledSince.ToLocalTime():HH:mm}，电源服务调用一直未返回）")
      .And.NotContain("状态探测失败");
    TrayStatusFormatter.FormatTooltip(Status(Stalled())).Should().Contain("守护已停工");
  }

  [Theory]
  [InlineData(GuardHealthCodes.FailureKinds.ApiError, "电源服务调用失败")]
  [InlineData(GuardHealthCodes.FailureKinds.TimedOut, "电源服务调用超时")]
  [InlineData("unregistered", "原因未知")]
  public void Stall_reason_codes_map_to_brief_text(string kind, string text)
    => TrayStatusFormatter.FormatStatusLine(Status(Stalled(kind))).Should().Contain(text);

  [Fact]
  public void Old_or_stale_status_never_shows_a_stall()
  {
    TrayStatusFormatter.FormatStatusLine(Status(health: null)).Should().Contain("状态探测失败").And.NotContain("停工");
    var stale = Status(Stalled());
    stale.timestamp = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
    TrayStatusFormatter.FormatStatusLine(stale).Should().NotContain("停工");
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(true, true)]
  public void Stall_notification_shows_regardless_of_plan_notification_preferences(bool onPlan, bool onExternal)
    => TrayNotificationPreferences.ShouldNotify(NotificationKinds.GuardStalled, onPlan, onExternal).Should().BeTrue();
```

`ForegroundGuardStatusTextTests` 追加：

```csharp
  [Fact]
  public void Stall_shows_its_start_and_reason_instead_of_probe_failure()
  {
    var since = new DateTimeOffset(2026, 9, 30, 11, 40, 0, TimeSpan.Zero);
    var status = Fresh(GuardStatusCodes.Sources.None, GuardStatusCodes.Reasons.ProbeFailed);
    status.guardHealth = new GuardHealthStatus(GuardHealthCodes.Stalled, since, GuardHealthCodes.FailureKinds.CallPending);
    ForegroundGuardStatusText.Describe(status, Now)
      .Should().Be($"守护已停工（自 {since.ToLocalTime():HH:mm}，电源服务调用一直未返回）");
  }

  [Fact]
  public void Healthy_guard_health_keeps_the_existing_reason_text()
  {
    var status = Fresh(GuardStatusCodes.Sources.None, GuardStatusCodes.Reasons.ProbeFailed);
    status.guardHealth = new GuardHealthStatus(GuardHealthCodes.Healthy, null, null);
    ForegroundGuardStatusText.Describe(status, Now).Should().Be("状态探测失败");
  }
```

- [ ] **Step 2: RED**

```powershell
foreach ($p in 'Tray','Settings') { dotnet build "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore -v q; "build $p exit=$LASTEXITCODE" }
dotnet test Tests/SmartGuard.Tray.Tests/SmartGuard.Tray.Tests.csproj --no-build --filter "FullyQualifiedName~TrayGuardHealthDisplayTests" --logger "trx;LogFileName=task4-red-tray.trx" --results-directory .work/guard-liveness
dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj --no-build --filter "FullyQualifiedName~ForegroundGuardStatusTextTests" --logger "trx;LogFileName=task4-red-settings.trx" --results-directory .work/guard-liveness
```

Expected：
- 失败：停工文案 1 例、原因映射 3 例、通知偏好中 `(false, false)` 一例（未登记 kind 退回计划切换开关）、设置窗口停工文案 1 例。
- 通过（保护性）：`Old_or_stale…`、通知偏好 `(true, true)`、`Healthy_guard_health…`、Task 1 的两例。

- [ ] **Step 3: 实现**

`TrayStatusFormatter.FormatGuardClause` 中，在 `IsKnownSource/IsKnownReason` 检查之前（新鲜度检查之后）加入：

```csharp
    if (GuardHealthCodes.IsStalled(status, nowUtc ?? DateTimeOffset.UtcNow))
      return FormatStall(status.guardHealth!);
```

并加入：

```csharp
  // GUARD-INV-LIVENESS display: a stable start time, not a running minute count (the tray only re-renders on change).
  internal static string FormatStall(GuardHealthStatus health)
    => $"守护已停工（自 {health.StalledSinceUtc!.Value.ToLocalTime():HH:mm}，{DescribeStallReason(health.LastFailureKind)}）";

  private static string DescribeStallReason(string? kind) => kind switch
  {
    GuardHealthCodes.FailureKinds.CallPending => "电源服务调用一直未返回",
    GuardHealthCodes.FailureKinds.ApiError => "电源服务调用失败",
    GuardHealthCodes.FailureKinds.TimedOut => "电源服务调用超时",
    _ => "原因未知",
  };
```

`TrayNotificationPreferences.ShouldNotify` 的 switch 首行加入 `NotificationKinds.GuardStalled => true,`。

`ForegroundGuardStatusText.Describe` 中，`if (status.paused) return "已暂停";` 之后加入：

```csharp
    if (GuardHealthCodes.IsStalled(status, nowUtc ?? DateTimeOffset.UtcNow))
      return $"守护已停工（自 {status.guardHealth!.StalledSinceUtc!.Value.ToLocalTime():HH:mm}，{DescribeStallReason(status.guardHealth.LastFailureKind)}）";
```

以及同样的私有 `DescribeStallReason`（Tray 与 Settings 各自维护展示文案，沿用 `guardReason` 映射在两处各有一份的既有约定；稳定代码只在 `GuardHealthCodes` 一处）。

- [ ] **Step 4: GREEN**

```powershell
foreach ($p in 'Tray','Settings','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task4-green-$p.trx" --results-directory .work/guard-liveness }
```

- [ ] **Step 5: 提交** `feat(ui): show guard stalls in the tray and settings and always notify`

---

### Task 5: 契约与门禁

**Files:** `docs/ARCHITECTURE-CONTRACT.md`、新建 `Tests/SmartGuard.Architecture.Tests/GuardLivenessArchitectureTests.cs`、设计文档

- [ ] **Step 1: 门禁 RED**

```csharp
using System.Text;
using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class GuardLivenessArchitectureTests
{
  [Fact]
  public void Only_the_iteration_runner_assigns_guard_health()
  {
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;
      var name = Path.GetFileName(file);
      // The two clones copy the field; the runner is the only producer.
      if (name is "GuardianIterationRunner.cs" or "StatusPublisher.cs" or "TrayDisplayState.cs") continue;
      File.ReadAllText(file, Encoding.UTF8).Should().NotContain("guardHealth =", $"{file} is not the guard health owner");
    }
  }

  [Fact]
  public void Liveness_rule_lives_in_one_tracker()
  {
    var runner = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianIterationRunner.Liveness.cs");
    runner.Should().Contain("_liveness.Observe(").And.NotContain("Threshold");
    SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardLivenessTracker.cs")
      .Should().Contain("TimeSpan.FromMinutes(10)");
  }

  [Fact]
  public void Contract_registers_the_liveness_invariant()
  {
    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    contract.Should().Contain("GUARD-INV-LIVENESS").And.Contain("guardHealth").And.Contain("GuardLivenessTracker");
  }
}
```

Expected: 第 1、2 例在 Task 3 后通过（保护性）；第 3 例失败。

- [ ] **Step 2: 契约**
  - 真源注册表“运行时状态”一行加入 `guardHealth`（`GuardHealthCodes` 登记表；`GuardianIterationRunner` 经 `GuardLivenessTracker` 计算，`StatusPublisher` 发布，Tray/Settings 经共享 reader 读取）。
  - `STATUS-GUARD-CODES` 加入：`guardHealth` 进入 Publisher 与 Tray 的相等判定与 Clone。
  - 3.1 不变量表加入 `GUARD-INV-LIVENESS`（阻断）：Engine 存活且可守护（配置可用、未暂停、生命周期允许）时，计划读取在连续 10 分钟单调清醒时间内未成功，必须发布 `guardHealth=stalled`（`stalledSinceUtc` 为窗口起点、`lastFailureKind` 为最近失败分类），并各记录一次 `GuardStalled`（Warn）与 `guard_stalled` 通知（不受计划切换通知开关控制）；唤醒与不可守护轮次只重开未停工的窗口；停工只以计划读取成功结束，记录一次 `GuardStallRecovered`；不自动重启。判定唯一实现为 `GuardLivenessTracker`。门禁：`GuardLivenessArchitectureTests`、`GuardLivenessTrackerTests`、`GuardLivenessRunnerTests`。
  - 设计文档状态改为“已出实施计划”，链接本计划；末尾加“实施与验证记录”标题（Task 6 填写）。

- [ ] **Step 3: GREEN 与文档校验**

```powershell
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --logger "trx;LogFileName=task5-green.trx" --results-directory .work/guard-liveness
foreach ($p in 'docs/ARCHITECTURE-CONTRACT.md','docs/implementation-plans/2026-11-19-guard-liveness-escalation.md') { $b=[IO.File]::ReadAllBytes($p); $null=[Text.UTF8Encoding]::new($false,$true).GetString($b); "$p bom=$($b[0] -eq 0xEF) ok" }
git diff --check -- docs
```

- [ ] **Step 4: 提交** `docs(contract): register the guard liveness invariant`

---

### Task 6: 全量验证、安装与验收

- [ ] **Step 1: 全量测试**：`Run-Tests.ps1`，核对 summary JSON 四层计数。运行前后各记录一次本机当前计划（PowerShell 中 `powercfg /getactivescheme`），证明测试未改动电源设置。
- [ ] **Step 2: 安装包**：同第三阶段 Task 7 Step 2（`SmartGuard.Packaging stage` 与 `installer`），记录 SHA256；版本提升单独提交；安装前备份 `D:\Apps\SmartGuard\bin` 与配置到 `.work/guard-liveness/original-install`。
- [ ] **Step 3: 用户安装**：确认 UAC；核对 Engine、Configuration、Settings、Tray、Contracts DLL 与候选一致、配置哈希未变、Engine/Tray 在交互会话运行。
- [ ] **Step 4: 真实路径验收（正常路径不回退）**
  - 运行至少 10 分钟：`status.json` 含 `"guardHealth"` 且 `code` 为 `healthy`；日志无 `GuardStalled`；托盘状态文字与安装前一致（无“停工”）。
  - 打开设置窗口一次：守护状态文字与安装前一致。
  - 托盘暂停守护一次：`guardHealth.code` 变为 `not-applicable`；恢复守护后回到 `healthy`。
  - **NOT RUN：** 真实停工需要让电源服务调用持续失败 10 分钟，真机无法安全制造；进入/恢复、通知与文案由 Task 2–4 用例覆盖。
- [ ] **Step 5: 记录并提交**：设计文档“实施与验证记录”写入各任务 RED/GREEN、全量计数、安装哈希、验收结果与 `NOT RUN` 项。提交 `docs(engine): record guard liveness verification`。

---

## 自查记录

- **设计覆盖：**
  - 目标 1（停工定义）→ Task 2 判定规则；可守护范围 → Task 3 `applicable`。
  - 目标 2（状态码、托盘与设置文案、日志各一次）→ Task 1、3、4；D2 通知 → Task 3、4。
  - 目标 3（恢复以读取成功为准）→ Task 2 `A_stall_survives…`、Task 3 `A_successful_read…`。
  - 3.4 失败路径：持续失败、间歇成功、睡眠、Engine 重启（tracker 无持久状态，随 runner 新建）、状态发布失败（沿用现有处理，健康码随下次成功发布出现）→ Task 2、3。
  - 第 5 节契约 → Task 5；第 7 节测试要点 → Task 2–5。
- **D1–D3：** 阈值常量 `GuardLivenessTracker.Threshold = 10 分钟`；每次停工一条通知；不自动重启（无任何退出或重启代码）。
- **RED 合规：** 新类型、字段与事件常量先以脚手架引入；每个 RED 因行为缺口失败；保护性用例逐一标明。
- **类型一致：** `GuardHealthStatus(Code, StalledSinceUtc, LastFailureKind)`；`GuardHealthCodes.{Healthy, Stalled, NotApplicable, FailureKinds.*, IsStalled}`；`GuardLivenessTracker.{Threshold, Current, Observe, FailureKindCode}`；`GuardLivenessObservation(Transition, Duration, LastFailureKind)`；`EventNameRegistry.{GuardStalled, GuardStallRecovered}`；`NotificationKinds.GuardStalled = "guard_stalled"`。
- **已知风险：**
  - `FormatStall` 用本机时区显示起始时刻；测试以同一转换计算期望值，不依赖运行机时区。
  - 停工通知放入状态后，若同一保留期内正常轮次又产生计划切换通知，后者会替换前者；停工只在读取失败时进入，读取失败的轮次不会切换计划，因此不会在同一轮冲突。
