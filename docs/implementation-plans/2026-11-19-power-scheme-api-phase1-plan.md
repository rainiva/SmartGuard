# 电源方案 API 迁移 — 第一阶段（Engine 核心链路）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**状态：** 已按 2026-11-19 外部审查修订（见文末“外部审查修订记录”），待用户批准后实施。设计见 [2026-11-19-power-scheme-api-migration.md](2026-11-19-power-scheme-api-migration.md)。

**Goal:** Engine 的计划读取、切换验证、亮度设置项同步、首次初始化与计划名称加载全部经 `PowerSchemeService` 调用 Windows 电源 API，Engine 不再启动 `powercfg.exe`。

**Architecture:** `SmartGuard.Configuration` 新增唯一入口 `PowerSchemeService`：原生层 `PowrProfNative`（经 `IPowrProfNative` 可替换）+ 单飞执行器 `PowerSchemeExecutor`（真实原生层每进程一个；截止 2 秒、列举 5 秒；在途期间拒绝为 `call-pending`；门由工作项在原生调用返回后、结果对调用方可见前释放；工作项进入原生层前复核“调用方未放弃、未取消、写操作仍获准入”，任一不满足则不触达原生层）。测试经 `AsyncLocal` 作用域替换为各自的执行器，互不共享门与替身；测试程序集默认禁止真实写入。Engine 的 `PowerCfgExecutor` 保留业务职责（探测、切换并验证、亮度同步、首次初始化），底层改调该入口；`GuardianIterationRunner` 用 `ReportSchemeStatus` 取代基于进程结果的报告并删除 powercfg 闩锁；`PowerPlanCatalogProvider.TryLoadOnceAsync` 改走电源 API。Settings/Tray 仍走 powercfg，留待第二阶段。

**Tech Stack:** .NET 8、C#、P/Invoke（PowrProf.dll）、xUnit 2.5.3、FluentAssertions 8。

**证据目录：** `.work/power-scheme-api-phase1/`。

**通用约束：**

- 源文件 UTF-8 无 BOM，精确补丁编辑；每个任务只暂存本任务文件；Conventional Commits，结尾 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`；分支 `fix/logging-observability`。
- RED 依 `PROJECT-RULES.md` §5.1 必须因行为缺口失败：Task 0 先引入不改变行为的骨架。
- **系统修改边界（由代码强制，不靠约定）：** 自动化测试只读真实电源设置。Configuration.Tests 与 Engine.Tests 各有一个 `[ModuleInitializer]`（`PowerWriteGuard`）把 `PowerSchemeService.RealWritesAllowed` 置为 `false`，此后任何经真实执行器的 `SetActive`/`WriteValueIndex` 在调用方同步抛出 `InvalidOperationException`，不进入原生层。仅当环境变量 `SMARTGUARD_POWER_WRITE_TESTS=1` 时放开，用于 Task 2 由用户在本机执行的临时方案写入测试。
- **旧路径封锁：** Engine 的 `PowerSchemeTestScope` 在进入时同时把 `PowerCfgProcessRunner.RunOverrideForTests` 与 `RunAsyncOverrideForTests` 设为返回失败，退出时复位。迁移任务的 RED 即使落入旧实现，也不会启动 `powercfg.exe`。
- **替身隔离：** 替身经 `PowerSchemeService.UseExecutorForTests`（`AsyncLocal`）只对当前测试的异步流生效，门与健康事实属于该替身执行器，不影响并行测试。替换替身的测试类仍放入既有禁止并行集合（Configuration.Tests 用 `PowerPlanCatalogProvider`，Engine.Tests 用 `LoggingFallbackTests`），因为旧路径封锁使用的 `PowerCfgProcessRunner` 覆盖仍是进程级静态量（与现有 `GuardianProbeHardeningTests` 的暴露面相同）。
- **逐任务编译：** 每个 RED 步骤先执行 `dotnet build`（Configuration、Engine、Architecture 三个测试项目），确认编译状态与预期一致后再取 RED，不把编译问题留到全量测试。

---

## 文件结构

| 文件 | 动作 | 责任 |
|------|------|------|
| `src/SmartGuard.Configuration/PowrProfNative.cs` | 新建 | `IPowrProfNative` 与唯一的电源方案 P/Invoke 实现 |
| `src/SmartGuard.Configuration/PowerSchemeService.cs` | 新建 | 结果类型、固定设置项、单飞执行器 `PowerSchemeExecutor`、对外操作、健康事实、测试执行器作用域与真实写入开关 |
| `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs` | 修改 | `TryLoadOnceAsync` 改走 `PowerSchemeService` |
| `src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs` | 修改 | 新失败分类与 `Win32Error` |
| `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs` | 修改 | 业务操作改调 `PowerSchemeService`；删除 powercfg 解析 |
| `src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs` | 修改 | `WmiProbeObservation` 增加 `Win32Error` |
| `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs` | 修改 | 切换、亮度同步、失败文案 |
| `src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs` | 修改 | 计划读取、状态报告、目录加载；删除 powercfg 闩锁 |
| `src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs` | 修改 | 电源 API 操作的日志文案与 `win32Error` |
| `src/SmartGuard.Engine/Worker/GuardianFirstRunInitializer.cs` | 修改 | 日志文案 |
| `docs/ARCHITECTURE-CONTRACT.md`、迁移设计 | 修改 | 唯一入口、`PWR-INV-SCHEME-API` |
| `Tests/SmartGuard.Configuration.Tests/FakePowrProfNative.cs`、`PowerSchemeTestScope.cs`、`ManualTaskScheduler.cs`、`PowerWriteGuard.cs` | 新建 | 替身、作用域、可控调度器（排队语义测试）、真实写入封锁 |
| `Tests/SmartGuard.Configuration.Tests/PowerSchemeServiceTests.cs` | 新建 | 执行器语义 |
| `Tests/SmartGuard.Configuration.Tests/PowerSchemeServiceLiveTests.cs` | 新建 | 真实 API 只读与（用户执行的）写入测试 |
| `Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs` | 修改 | `TryLoadOnceAsync` 新行为 |
| `Tests/SmartGuard.Engine.Tests/FakePowrProfNative.cs`、`PowerSchemeTestScope.cs`、`PowerWriteGuard.cs` | 新建 | 替身、作用域（含旧 powercfg 路径封锁）、真实写入封锁（测试程序集互不引用，各放一份） |
| `Tests/SmartGuard.Engine.Tests/GuardianPowerLifecycleIntegrationTests.cs`、`GuardInterruptionLoggingTests.cs`、`GuardStatusPublishingTests.cs`、`PlanPublicationConsistencyTests.cs`、`PolicyEngineTests.cs` | 修改 | Task 7 删除旧失败分类后改用 `ApiFailed` |
| `Tests/SmartGuard.Engine.Tests/PowerPlanOperationTests.cs` | 重写 | 探测、切换验证、分类与文案 |
| `Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs` | 修改 | Runner 行为迁移到替身原生层 |
| `Tests/SmartGuard.Engine.Tests/NativeProbeLoggingTests.cs` | 修改 | 电源 API 日志格式 |
| `Tests/SmartGuard.Engine.Tests/GuardianFirstRunInitializerTests.cs` | 新建 | 只读能力探测、“不支持/探测失败”区分、写入范围与完成标记语义 |
| `Tests/SmartGuard.Engine.Tests/PowerCfgParserTests.cs` | 删除 | powercfg 文本解析随实现删除 |
| `Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs` | 新建 | Engine 无 powercfg、原生函数唯一导入处、契约登记 |

---

### Task 0: 不改变行为的骨架

**Files:**
- Create: `src/SmartGuard.Configuration/PowrProfNative.cs`、`src/SmartGuard.Configuration/PowerSchemeService.cs`
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs`、`src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs:5-7`

- [ ] **Step 1: 记录基线**

```powershell
New-Item -ItemType Directory -Force .work\power-scheme-api-phase1 | Out-Null
foreach ($p in 'Configuration','Engine','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task0-baseline-$p.trx" --results-directory .work/power-scheme-api-phase1 }
```

Expected: 三个项目全部通过，记录各自通过数。

- [ ] **Step 2: 新建 `PowrProfNative.cs`（骨架：所有调用返回 1 = ERROR_INVALID_FUNCTION）**

```csharp
namespace SmartGuard.Configuration;

internal enum PowerSource { Ac, Dc }

/// <summary>Replaceable native surface of the power scheme API. Return value is the Win32 code (0 = success).</summary>
internal interface IPowrProfNative
{
  uint GetActiveScheme(out Guid scheme);
  uint SetActiveScheme(Guid scheme);
  uint ReadFriendlyName(Guid scheme, out string name);
  uint EnumerateSchemes(out Guid[] schemes);
  uint ReadValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, out uint value);
  uint WriteValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, uint value);
}

/// <summary>Scaffold only; the real P/Invoke implementation arrives with its live tests.</summary>
internal sealed class PowrProfNative : IPowrProfNative
{
  internal static readonly PowrProfNative Instance = new();
  private const uint NotImplemented = 1;

  public uint GetActiveScheme(out Guid scheme) { scheme = Guid.Empty; return NotImplemented; }
  public uint SetActiveScheme(Guid scheme) => NotImplemented;
  public uint ReadFriendlyName(Guid scheme, out string name) { name = string.Empty; return NotImplemented; }
  public uint EnumerateSchemes(out Guid[] schemes) { schemes = []; return NotImplemented; }
  public uint ReadValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, out uint value) { value = 0; return NotImplemented; }
  public uint WriteValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, uint value) => NotImplemented;
}
```

- [ ] **Step 3: 新建 `PowerSchemeService.cs`（骨架：所有操作直接返回 `api-error`，不调用原生层）**

```csharp
namespace SmartGuard.Configuration;

internal enum PowerSchemeSetting { VideoNormalLevel, AdaptiveBrightness }

internal readonly record struct PowerSchemeStatus(string FailureKind, int? Win32Error, long ElapsedMilliseconds)
{
  public bool Succeeded => FailureKind == "none";
}

internal readonly record struct PowerSchemeResult<T>(PowerSchemeStatus Status, T? Value)
{
  public bool Succeeded => Status.Succeeded;
}

/// <summary>Scaffold only; execution semantics arrive with their tests.</summary>
internal sealed class PowerSchemeExecutor
{
  internal PowerSchemeExecutor(IPowrProfNative native, TimeSpan? deadlineOverride = null, TaskScheduler? scheduler = null)
  {
    Native = native;
    DeadlineOverride = deadlineOverride;
    Scheduler = scheduler ?? TaskScheduler.Default;
  }

  internal IPowrProfNative Native { get; }
  internal TimeSpan? DeadlineOverride { get; }
  internal TaskScheduler Scheduler { get; }
  internal bool IsCallInFlight => false;
  internal long? InFlightSince => null;
  internal long? LastSucceededAt => null;

  internal Task<PowerSchemeResult<T>> RunAsync<T>(Func<IPowrProfNative, (uint Rc, T Value)> operation, TimeSpan deadline,
    Func<bool>? admit, CancellationToken cancellationToken)
    => Task.FromResult(new PowerSchemeResult<T>(new("api-error", null, 0), default));
}

/// <summary>Scaffold only; execution semantics arrive with their tests.</summary>
internal static class PowerSchemeService
{
  internal static readonly Guid VideoSubgroupGuid = new("7516b95f-f776-4464-8c53-06167f40cc99");
  internal static readonly Guid VideoNormalLevelGuid = new("aded5e82-b909-4619-9949-f5d71dac0bcb");
  internal static readonly Guid AdaptiveBrightnessGuid = new("fbd9aa66-9553-4097-ba44-ed6e9d65eab8");
  internal static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(2);
  internal static readonly TimeSpan EnumerateDeadline = TimeSpan.FromSeconds(5);

  internal static volatile bool RealWritesAllowed = true;
  internal static void UseExecutorForTests(PowerSchemeExecutor? executor) { }

  internal static bool IsCallInFlight => false;
  internal static long? InFlightSince => null;
  internal static long? LastSucceededAt => null;

  internal static Task<PowerSchemeResult<Guid>> GetActiveAsync(CancellationToken cancellationToken = default) => Stub<Guid>();
  internal static Task<PowerSchemeResult<bool>> SetActiveAsync(Guid scheme, Func<bool>? admit = null,
    CancellationToken cancellationToken = default) => Stub<bool>();
  internal static Task<PowerSchemeResult<string>> ReadNameAsync(Guid scheme, CancellationToken cancellationToken = default) => Stub<string>();
  internal static Task<PowerSchemeResult<IReadOnlyList<Guid>>> EnumerateSchemesAsync(CancellationToken cancellationToken = default) => Stub<IReadOnlyList<Guid>>();
  internal static Task<PowerSchemeResult<uint>> ReadValueIndexAsync(Guid scheme, PowerSchemeSetting setting, PowerSource source,
    CancellationToken cancellationToken = default) => Stub<uint>();
  internal static Task<PowerSchemeResult<bool>> WriteValueIndexAsync(Guid scheme, PowerSchemeSetting setting, PowerSource source,
    uint value, Func<bool>? admit = null, CancellationToken cancellationToken = default) => Stub<bool>();

  private static Task<PowerSchemeResult<T>> Stub<T>() => Task.FromResult(new PowerSchemeResult<T>(new("api-error", null, 0), default));
}
```

- [ ] **Step 4: Engine 结果类型增加可选字段与新分类（旧分类暂留）**

`PowerPlanOperationResult.cs` 整体替换为：

```csharp
namespace SmartGuard.Engine.Infrastructure;

internal enum PowerPlanFailureKind
{
  None,
  ProcessStartFailed,
  TimedOut,
  CommandFailed,
  ParseFailed,
  VerificationMismatch,
  ApiFailed,
  CallPending,
  NotAdmitted,
}

internal readonly record struct PowerPlanProbeResult(
  bool Succeeded,
  PowerSchemeInfo? Plan,
  PowerPlanFailureKind FailureKind,
  string Diagnostic,
  int? Win32Error = null)
{
  public Guid? ObservedGuid => Plan?.Guid;
}

internal readonly record struct PowerPlanApplyResult(
  bool Succeeded,
  Guid TargetGuid,
  Guid? ObservedGuid,
  PowerPlanFailureKind FailureKind,
  string Diagnostic,
  int? Win32Error = null);
```

`WmiProbeProcess.cs` 第 5–7 行：

```csharp
internal sealed record WmiProbeObservation(string Operation, string FailureKind,
  long ElapsedMilliseconds, long BudgetMilliseconds, bool TerminationConfirmed,
  int? ExitCode, long SuppressedCount, bool Recovered, int? Win32Error = null);
```

- [ ] **Step 5: 证明无行为变化**

```powershell
foreach ($p in 'Configuration','Engine','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task0-$p.trx" --results-directory .work/power-scheme-api-phase1 }
```

Expected: 三个项目全部通过，通过数与 Step 1 一致。

- [ ] **Step 6: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowrProfNative.cs src/SmartGuard.Configuration/PowerSchemeService.cs src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs
git commit -m "refactor(config): scaffold power scheme service without behaviour change" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 1: 单飞执行器语义

**Files:**
- Create: `Tests/SmartGuard.Configuration.Tests/FakePowrProfNative.cs`、`Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs`、`Tests/SmartGuard.Configuration.Tests/ManualTaskScheduler.cs`、`Tests/SmartGuard.Configuration.Tests/PowerWriteGuard.cs`、`Tests/SmartGuard.Configuration.Tests/PowerSchemeServiceTests.cs`
- Modify: `src/SmartGuard.Configuration/PowerSchemeService.cs`

**执行器语义（本任务的验收对象）：**

| 时刻 | 结果 | 是否触达原生层 | 门 |
|------|------|----------------|----|
| 门被占用时发起 | `call-pending` | 否 | 不变 |
| 排队中调用方取消 | 调用方收到 `OperationCanceledException` | 否（工作项看到“已放弃”后跳过） | 工作项跳过后释放 |
| 排队中超过截止 | `timed-out` | 否 | 同上 |
| 排队中写操作准入被撤销 | `not-admitted` | 否 | 工作项释放 |
| 已进入原生层后超时／取消 | `timed-out`／`OperationCanceledException` | 是（无法撤回） | 原生函数返回后释放 |
| 正常完成 | 结果 | 是 | **结果对调用方可见前**已释放 |

“进入原生层”的判定点是工作项把票据从“排队”原子地改为“已进入”（CAS）；调用方超时或取消时把票据从“排队”改为“已放弃”。两者只有一方成功。门只由工作项的 `finally` 释放（含跳过路径），调用方从不释放，避免重复释放。

- [ ] **Step 1: 测试替身、作用域、可控调度器与写入封锁**

`Tests/SmartGuard.Configuration.Tests/FakePowrProfNative.cs`：

```csharp
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

internal sealed class FakePowrProfNative : IPowrProfNative
{
  private readonly object _sync = new();
  private readonly List<string> _calls = [];

  public Func<(uint Rc, Guid Scheme)> GetActive { get; set; } = () => (0, PowerPlanCatalogProvider.BalancedPlanGuid);
  public Func<Guid, uint> SetActive { get; set; } = _ => 0;
  public Func<Guid, (uint Rc, string Name)> ReadName { get; set; } = scheme => (0, scheme.ToString("D"));
  public Func<(uint Rc, Guid[] Schemes)> Enumerate { get; set; } = () => (0, []);
  public Func<Guid, Guid, PowerSource, (uint Rc, uint Value)> ReadValue { get; set; } = (_, _, _) => (0, 50);
  public Func<Guid, Guid, PowerSource, uint, uint> WriteValue { get; set; } = (_, _, _, _) => 0;

  public IReadOnlyList<string> Calls { get { lock (_sync) return _calls.ToArray(); } }

  private void Record(string call) { lock (_sync) _calls.Add(call); }

  public uint GetActiveScheme(out Guid scheme) { Record("get-active"); var r = GetActive(); scheme = r.Scheme; return r.Rc; }
  public uint SetActiveScheme(Guid scheme) { Record($"set-active {scheme:D}"); return SetActive(scheme); }
  public uint ReadFriendlyName(Guid scheme, out string name) { Record($"read-name {scheme:D}"); var r = ReadName(scheme); name = r.Name; return r.Rc; }
  public uint EnumerateSchemes(out Guid[] schemes) { Record("enumerate"); var r = Enumerate(); schemes = r.Schemes; return r.Rc; }

  public uint ReadValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, out uint value)
  {
    Record($"read-value {scheme:D} {subgroup:D} {setting:D} {source}");
    var r = ReadValue(scheme, setting, source);
    value = r.Value;
    return r.Rc;
  }

  public uint WriteValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, uint value)
  {
    Record($"write-value {scheme:D} {subgroup:D} {setting:D} {source} {value}");
    return WriteValue(scheme, setting, source, value);
  }
}
```

`Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs`：

```csharp
using System.Diagnostics;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

internal static class PowerSchemeTestScope
{
  // The executor override is AsyncLocal: it reaches only this test's async flow, so parallel
  // tests never share this fake, its gate or its health facts.
  internal static async Task WithNative(IPowrProfNative native, Func<Task> run, TimeSpan? deadline = null,
    TaskScheduler? scheduler = null)
  {
    PowerSchemeService.UseExecutorForTests(new PowerSchemeExecutor(native, deadline, scheduler));
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    try { await run(); }
    finally
    {
      // A blocked fake must return before the scope ends: the gate belongs to the in-flight call.
      try { await WaitUntilIdleAsync(); }
      finally
      {
        // Reset in this frame (not in a nested async method) so the change applies to this flow.
        PowerSchemeService.UseExecutorForTests(null);
        PowerPlanCatalogProvider.ClearSessionCacheForTests();
      }
    }
  }

  internal static async Task WaitUntilIdleAsync()
  {
    var watch = Stopwatch.StartNew();
    while (PowerSchemeService.IsCallInFlight)
    {
      if (watch.Elapsed > TimeSpan.FromSeconds(10))
        throw new TimeoutException("A fake native call is still in flight; the test left the gate held.");
      await Task.Delay(10);
    }
  }
}
```

`Tests/SmartGuard.Configuration.Tests/ManualTaskScheduler.cs`（让“排队中”成为确定状态，不依赖线程池时序）：

```csharp
using System.Collections.Concurrent;

namespace SmartGuard.Configuration.Tests;

internal sealed class ManualTaskScheduler : TaskScheduler
{
  private readonly ConcurrentQueue<Task> _queue = new();

  public int RunPending()
  {
    var count = 0;
    while (_queue.TryDequeue(out var task)) { TryExecuteTask(task); count++; }
    return count;
  }

  protected override void QueueTask(Task task) => _queue.Enqueue(task);
  protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
  protected override IEnumerable<Task> GetScheduledTasks() => _queue.ToArray();
}
```

`Tests/SmartGuard.Configuration.Tests/PowerWriteGuard.cs`（仓库已有先例：`SettingsUiTestModeBootstrap` 使用 `[ModuleInitializer]`）：

```csharp
using System.Runtime.CompilerServices;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

internal static class PowerWriteGuard
{
  internal const string Variable = "SMARTGUARD_POWER_WRITE_TESTS";
  internal static bool WritesRequested => Environment.GetEnvironmentVariable(Variable) == "1";

  // Tests read real power settings only: a real SetActive/WriteValueIndex throws unless the user opted in.
  [ModuleInitializer]
  internal static void Install() => PowerSchemeService.RealWritesAllowed = WritesRequested;
}
```

- [ ] **Step 2: 写失败测试 `PowerSchemeServiceTests.cs`**

```csharp
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("PowerPlanCatalogProvider")]
public class PowerSchemeServiceTests
{
  private static readonly Guid High = PowerPlanCatalogProvider.HighPerformancePlanGuid;

  [Fact]
  public async Task Successful_call_returns_its_value()
  {
    var native = new FakePowrProfNative { GetActive = () => (0, High) };
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      var result = await PowerSchemeService.GetActiveAsync();
      result.Succeeded.Should().BeTrue();
      result.Value.Should().Be(High);
      PowerSchemeService.LastSucceededAt.Should().NotBeNull();
    });
    native.Calls.Should().Equal("get-active");
  }

  [Fact]
  public async Task Nonzero_return_code_is_an_api_error_with_its_win32_code()
  {
    var native = new FakePowrProfNative { ReadName = _ => (2, string.Empty) };
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      var result = await PowerSchemeService.ReadNameAsync(Guid.NewGuid());
      result.Status.Should().Be(new PowerSchemeStatus("api-error", 2, result.Status.ElapsedMilliseconds));
    });
  }

  [Fact]
  public async Task Exception_from_the_native_layer_is_an_api_error_without_a_code()
  {
    var native = new FakePowrProfNative { GetActive = () => throw new DllNotFoundException("powrprof") };
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      var result = await PowerSchemeService.GetActiveAsync();
      result.Status.FailureKind.Should().Be("api-error");
      result.Status.Win32Error.Should().BeNull();
      PowerSchemeService.IsCallInFlight.Should().BeFalse("a faulted call released the gate");
    });
  }

  [Fact]
  public async Task Call_past_its_deadline_times_out_and_blocks_new_calls_until_it_returns()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var first = true;
    var native = new FakePowrProfNative
    {
      GetActive = () =>
      {
        if (first) { first = false; entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); }
        return (0, High);
      },
    };
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      try
      {
        var timedOut = await PowerSchemeService.GetActiveAsync();
        timedOut.Status.FailureKind.Should().Be("timed-out");
        entered.IsSet.Should().BeTrue();
        PowerSchemeService.IsCallInFlight.Should().BeTrue();
        PowerSchemeService.InFlightSince.Should().NotBeNull();

        var pending = await PowerSchemeService.ReadNameAsync(High);
        pending.Status.FailureKind.Should().Be("call-pending");
        native.Calls.Should().Equal(["get-active"], "a refused call never reaches the native layer");
      }
      finally { release.Set(); }

      await PowerSchemeTestScope.WaitUntilIdleAsync();
      PowerSchemeService.IsCallInFlight.Should().BeFalse();
      (await PowerSchemeService.GetActiveAsync()).Succeeded.Should().BeTrue("the returned call reopened the gate");
    }, deadline: TimeSpan.FromMilliseconds(100));
  }

  [Fact]
  public async Task Caller_cancellation_after_entry_stops_waiting_but_keeps_the_call_in_flight()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var native = new FakePowrProfNative
    {
      Enumerate = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return (0, []); },
    };
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      try
      {
        using var cancellation = new CancellationTokenSource();
        var call = PowerSchemeService.EnumerateSchemesAsync(cancellation.Token);
        entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        PowerSchemeService.IsCallInFlight.Should().BeTrue("a native call that already started keeps the gate");
        (await PowerSchemeService.GetActiveAsync()).Status.FailureKind.Should().Be("call-pending");
      }
      finally { release.Set(); }
    });
  }

  [Fact]
  public async Task Cancelled_while_queued_never_reaches_the_native_layer()
  {
    var scheduler = new ManualTaskScheduler();
    var native = new FakePowrProfNative();
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      using var cancellation = new CancellationTokenSource();
      var call = PowerSchemeService.SetActiveAsync(High, cancellationToken: cancellation.Token);
      cancellation.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
      PowerSchemeService.IsCallInFlight.Should().BeTrue("the queued work item still owns the gate");

      scheduler.RunPending().Should().Be(1);
      native.Calls.Should().BeEmpty("a caller that already observed cancellation never starts a write");
      PowerSchemeService.IsCallInFlight.Should().BeFalse("the skipped work item released the gate");
    }, scheduler: scheduler);
  }

  [Fact]
  public async Task Deadline_passed_while_queued_never_reaches_the_native_layer()
  {
    var scheduler = new ManualTaskScheduler();
    var native = new FakePowrProfNative();
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      var result = await PowerSchemeService.WriteValueIndexAsync(High, PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac, 40);
      result.Status.FailureKind.Should().Be("timed-out");

      scheduler.RunPending().Should().Be(1);
      native.Calls.Should().BeEmpty();
      PowerSchemeService.IsCallInFlight.Should().BeFalse();
    }, deadline: TimeSpan.FromMilliseconds(50), scheduler: scheduler);
  }

  [Fact]
  public async Task Admission_is_rechecked_when_the_work_item_starts()
  {
    var scheduler = new ManualTaskScheduler();
    var native = new FakePowrProfNative();
    var permitted = true;
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      var call = PowerSchemeService.SetActiveAsync(High, admit: () => permitted);
      permitted = false; // lifecycle revoked after the caller's own check, before the native call
      scheduler.RunPending().Should().Be(1);

      (await call).Status.FailureKind.Should().Be("not-admitted");
      native.Calls.Should().BeEmpty();
      PowerSchemeService.IsCallInFlight.Should().BeFalse();
    }, scheduler: scheduler);
  }

  [Fact]
  public async Task Completed_call_releases_the_gate_before_its_result_is_visible()
  {
    var native = new FakePowrProfNative();
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      for (var i = 0; i < 5000; i++)
      {
        var result = await PowerSchemeService.GetActiveAsync();
        result.Status.FailureKind.Should().Be("none", $"sequential call {i} must not be refused by its own predecessor");
        PowerSchemeService.IsCallInFlight.Should().BeFalse($"the gate is free once call {i} is visible");
      }
    });
  }

  [Fact]
  public void Real_writes_are_refused_before_the_native_layer_when_disabled()
  {
    var previous = PowerSchemeService.RealWritesAllowed;
    PowerSchemeService.RealWritesAllowed = false;
    try
    {
      Assert.Throws<InvalidOperationException>(() => { _ = PowerSchemeService.SetActiveAsync(High); });
      Assert.Throws<InvalidOperationException>(() =>
      {
        _ = PowerSchemeService.WriteValueIndexAsync(High, PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac, 40);
      });
    }
    finally { PowerSchemeService.RealWritesAllowed = previous; }
  }

  [Fact]
  public async Task Value_index_operations_target_only_the_fixed_video_settings()
  {
    var native = new FakePowrProfNative();
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      (await PowerSchemeService.WriteValueIndexAsync(High, PowerSchemeSetting.VideoNormalLevel, PowerSource.Dc, 40)).Succeeded.Should().BeTrue();
      (await PowerSchemeService.ReadValueIndexAsync(High, PowerSchemeSetting.AdaptiveBrightness, PowerSource.Ac)).Value.Should().Be(50u);
    });
    native.Calls.Should().Equal(
      $"write-value {High:D} {PowerSchemeService.VideoSubgroupGuid:D} {PowerSchemeService.VideoNormalLevelGuid:D} Dc 40",
      $"read-value {High:D} {PowerSchemeService.VideoSubgroupGuid:D} {PowerSchemeService.AdaptiveBrightnessGuid:D} Ac");
  }

  [Fact]
  public void Deadlines_are_bounded()
  {
    PowerSchemeService.DefaultDeadline.Should().Be(TimeSpan.FromSeconds(2));
    PowerSchemeService.EnumerateDeadline.Should().Be(TimeSpan.FromSeconds(5));
  }
}
```

- [ ] **Step 3: 运行确认 RED**

```powershell
foreach ($p in 'Configuration','Engine','Architecture') { dotnet build "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore -v q; "$p build exit=$LASTEXITCODE" }
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~PowerSchemeServiceTests --logger "trx;LogFileName=task1-red.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: 三个项目编译退出码 0；12 个用例中除 `Deadlines_are_bounded` 与 `Exception_from_the_native_layer...`（骨架恰好返回无码 `api-error` 且门空闲）外全部失败（骨架从不调度工作项、从不调用原生层、始终 `api-error`、不封锁真实写入）。

- [ ] **Step 4: 实现执行器**

`PowerSchemeService.cs` 中，将 `PowerSchemeExecutor` 与 `PowerSchemeService` 两个类（含其上方的骨架注释）整体替换为：

```csharp
/// <summary>
/// Single-flight executor (PWR-INV-SCHEME-API). The work item owns the gate and releases it in its own
/// finally, after the native function returned and before the result becomes visible to the caller, so a
/// completed call never refuses its caller's next call. A call past its deadline keeps the gate until the
/// native function really returns; meanwhile new calls are refused as call-pending. A work item that has
/// not entered the native layer skips it when its caller gave up (deadline or cancellation) or when a
/// write is no longer admitted. The caller never releases the gate.
/// </summary>
internal sealed class PowerSchemeExecutor
{
  private const int Queued = 0, Entered = 1, Abandoned = 2;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private long _inFlightSince;
  private long _lastSucceededAt;

  internal PowerSchemeExecutor(IPowrProfNative native, TimeSpan? deadlineOverride = null, TaskScheduler? scheduler = null)
  {
    Native = native;
    DeadlineOverride = deadlineOverride;
    Scheduler = scheduler ?? TaskScheduler.Default;
  }

  internal IPowrProfNative Native { get; }
  internal TimeSpan? DeadlineOverride { get; }
  internal TaskScheduler Scheduler { get; }
  internal bool IsCallInFlight => Volatile.Read(ref _inFlightSince) != 0;
  internal long? InFlightSince => Volatile.Read(ref _inFlightSince) is var at && at != 0 ? at : (long?)null;
  internal long? LastSucceededAt => Volatile.Read(ref _lastSucceededAt) is var at && at != 0 ? at : (long?)null;

  private enum Outcome { Completed, Faulted, Skipped, NotAdmitted }

  private sealed class Ticket
  {
    public int State;
  }

  internal async Task<PowerSchemeResult<T>> RunAsync<T>(Func<IPowrProfNative, (uint Rc, T Value)> operation,
    TimeSpan deadline, Func<bool>? admit, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    if (!_gate.Wait(0)) return Failed<T>("call-pending", null, started);
    Volatile.Write(ref _inFlightSince, started);
    var ticket = new Ticket();
    Task<(Outcome Outcome, uint Rc, T? Value)> call;
    try
    {
      // No token on StartNew: a task cancelled before it starts would skip the finally that owns the release.
      call = Task.Factory.StartNew(() =>
      {
        try
        {
          // Linearization point: entering wins against a caller that has not yet given up, and vice versa.
          if (Interlocked.CompareExchange(ref ticket.State, Entered, Queued) != Queued
              || cancellationToken.IsCancellationRequested)
            return (Outcome.Skipped, 0u, default(T));
          if (admit?.Invoke() == false) return (Outcome.NotAdmitted, 0u, default(T));
          var (rc, value) = operation(Native);
          return (Outcome.Completed, rc, value);
        }
        catch (Exception)
        {
          // Observed here so a late fault after the caller left never becomes an unobserved task exception.
          return (Outcome.Faulted, 0u, default(T));
        }
        finally
        {
          Release();
        }
      }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, Scheduler);
    }
    catch
    {
      Release();
      throw;
    }

    try
    {
      var (outcome, rc, value) = await call.WaitAsync(DeadlineOverride ?? deadline, cancellationToken).ConfigureAwait(false);
      switch (outcome)
      {
        case Outcome.Skipped:
          cancellationToken.ThrowIfCancellationRequested();
          return Failed<T>("timed-out", null, started);
        case Outcome.NotAdmitted:
          return Failed<T>("not-admitted", null, started);
        case Outcome.Faulted:
          return Failed<T>("api-error", null, started);
      }
      if (rc != 0) return Failed<T>("api-error", unchecked((int)rc), started);
      Volatile.Write(ref _lastSucceededAt, System.Diagnostics.Stopwatch.GetTimestamp());
      return new(new("none", null, Elapsed(started)), value);
    }
    catch (TimeoutException)
    {
      _ = Interlocked.CompareExchange(ref ticket.State, Abandoned, Queued);
      return Failed<T>("timed-out", null, started);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      _ = Interlocked.CompareExchange(ref ticket.State, Abandoned, Queued);
      throw;
    }
  }

  private void Release()
  {
    Volatile.Write(ref _inFlightSince, 0);
    _gate.Release();
  }

  private static PowerSchemeResult<T> Failed<T>(string kind, int? win32Error, long started)
    => new(new(kind, win32Error, Elapsed(started)), default);

  private static long Elapsed(long started) => (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}

/// <summary>
/// Sole entry for power scheme operations (PWR-INV-SCHEME-API). The real native layer has exactly one
/// executor per process; tests substitute their own executor for their async flow only. No fallback to
/// external processes.
/// </summary>
internal static class PowerSchemeService
{
  internal static readonly Guid VideoSubgroupGuid = new("7516b95f-f776-4464-8c53-06167f40cc99");
  internal static readonly Guid VideoNormalLevelGuid = new("aded5e82-b909-4619-9949-f5d71dac0bcb");
  internal static readonly Guid AdaptiveBrightnessGuid = new("fbd9aa66-9553-4097-ba44-ed6e9d65eab8");
  internal static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(2);
  internal static readonly TimeSpan EnumerateDeadline = TimeSpan.FromSeconds(5);

  private static readonly PowerSchemeExecutor RealExecutor = new(PowrProfNative.Instance);
  private static readonly AsyncLocal<PowerSchemeExecutor?> TestExecutor = new();

  /// <summary>Test assemblies clear this in a module initializer; real writes then throw before the native layer.</summary>
  internal static volatile bool RealWritesAllowed = true;

  internal static void UseExecutorForTests(PowerSchemeExecutor? executor) => TestExecutor.Value = executor;

  private static PowerSchemeExecutor Current => TestExecutor.Value ?? RealExecutor;

  private static PowerSchemeExecutor WriteExecutor()
  {
    var executor = Current;
    if (ReferenceEquals(executor, RealExecutor) && !RealWritesAllowed)
      throw new InvalidOperationException("Real power scheme writes are disabled in this process.");
    return executor;
  }

  internal static bool IsCallInFlight => Current.IsCallInFlight;
  internal static long? InFlightSince => Current.InFlightSince;
  internal static long? LastSucceededAt => Current.LastSucceededAt;

  internal static Task<PowerSchemeResult<Guid>> GetActiveAsync(CancellationToken cancellationToken = default)
    => Current.RunAsync(native => (native.GetActiveScheme(out var scheme), scheme), DefaultDeadline, null, cancellationToken);

  internal static Task<PowerSchemeResult<bool>> SetActiveAsync(Guid scheme, Func<bool>? admit = null,
    CancellationToken cancellationToken = default)
    => WriteExecutor().RunAsync(native => (native.SetActiveScheme(scheme), true), DefaultDeadline, admit, cancellationToken);

  internal static Task<PowerSchemeResult<string>> ReadNameAsync(Guid scheme, CancellationToken cancellationToken = default)
    => Current.RunAsync(native => (native.ReadFriendlyName(scheme, out var name), name), DefaultDeadline, null, cancellationToken);

  internal static Task<PowerSchemeResult<IReadOnlyList<Guid>>> EnumerateSchemesAsync(CancellationToken cancellationToken = default)
    => Current.RunAsync<IReadOnlyList<Guid>>(native => (native.EnumerateSchemes(out var schemes), schemes),
      EnumerateDeadline, null, cancellationToken);

  internal static Task<PowerSchemeResult<uint>> ReadValueIndexAsync(Guid scheme, PowerSchemeSetting setting, PowerSource source,
    CancellationToken cancellationToken = default)
    => Current.RunAsync(native => (native.ReadValueIndex(scheme, VideoSubgroupGuid, SettingGuid(setting), source, out var value), value),
      DefaultDeadline, null, cancellationToken);

  internal static Task<PowerSchemeResult<bool>> WriteValueIndexAsync(Guid scheme, PowerSchemeSetting setting, PowerSource source,
    uint value, Func<bool>? admit = null, CancellationToken cancellationToken = default)
    => WriteExecutor().RunAsync(native => (native.WriteValueIndex(scheme, VideoSubgroupGuid, SettingGuid(setting), source, value), true),
      DefaultDeadline, admit, cancellationToken);

  private static Guid SettingGuid(PowerSchemeSetting setting) => setting switch
  {
    PowerSchemeSetting.VideoNormalLevel => VideoNormalLevelGuid,
    PowerSchemeSetting.AdaptiveBrightness => AdaptiveBrightnessGuid,
    _ => throw new ArgumentOutOfRangeException(nameof(setting)),
  };
}
```

该执行器已在评审修订时以隔离控制台程序验证，所用代码从本步骤原样摘出（证据：`.work/power-scheme-api-phase1/review-executor-probe.txt`，源码在同名目录）。原方案的执行器在 20 万次串行调用中误报 852 次 `call-pending`，且排队中取消后原生替身仍然执行；修订版 20 万次 0 误报，排队取消、排队超时、准入复核、在途超时持门、在途取消持门、原生异常、错误码七项全部通过。这只证明执行器本身成立，Task 1 的 RED/GREEN 仍须按步骤取证。

- [ ] **Step 5: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~PowerSchemeServiceTests --logger "trx;LogFileName=task1-green.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: 12 passed。

- [ ] **Step 6: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowerSchemeService.cs Tests/SmartGuard.Configuration.Tests/FakePowrProfNative.cs Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs Tests/SmartGuard.Configuration.Tests/ManualTaskScheduler.cs Tests/SmartGuard.Configuration.Tests/PowerWriteGuard.cs Tests/SmartGuard.Configuration.Tests/PowerSchemeServiceTests.cs
git commit -m "feat(config): single-flight power scheme executor with deadline and self-recovery" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: 真实原生实现与只读集成测试

**Files:**
- Modify: `src/SmartGuard.Configuration/PowrProfNative.cs`
- Create: `Tests/SmartGuard.Configuration.Tests/PowerSchemeServiceLiveTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("PowerPlanCatalogProvider")]
public class PowerSchemeServiceLiveTests
{
  [Fact]
  public async Task Active_scheme_matches_powercfg()
  {
    var api = await PowerSchemeService.GetActiveAsync();
    api.Succeeded.Should().BeTrue();
    var output = await RunPowerCfgOracleAsync("/getactivescheme");
    var match = Regex.Match(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
    match.Success.Should().BeTrue();
    api.Value.Should().Be(Guid.Parse(match.Groups[1].Value));
  }

  [Fact]
  public async Task Builtin_balanced_scheme_has_a_name_and_a_missing_scheme_reports_not_found()
  {
    var balanced = await PowerSchemeService.ReadNameAsync(PowerPlanCatalogProvider.BalancedPlanGuid);
    balanced.Succeeded.Should().BeTrue();
    balanced.Value.Should().NotBeNullOrWhiteSpace();
    var missing = await PowerSchemeService.ReadNameAsync(Guid.NewGuid());
    missing.Status.FailureKind.Should().Be("api-error");
    missing.Status.Win32Error.Should().Be(2);
  }

  [Fact]
  public async Task Enumerated_schemes_include_the_active_scheme()
  {
    var active = await PowerSchemeService.GetActiveAsync();
    var schemes = await PowerSchemeService.EnumerateSchemesAsync();
    schemes.Succeeded.Should().BeTrue();
    schemes.Value.Should().Contain(active.Value);
  }

  [Fact]
  public async Task Brightness_setting_read_is_supported_or_reports_not_found()
  {
    var read = await PowerSchemeService.ReadValueIndexAsync(PowerPlanCatalogProvider.BalancedPlanGuid,
      PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac);
    (read.Succeeded || read.Status.Win32Error == 2).Should().BeTrue($"unexpected status {read.Status}");
  }

  // Creates and deletes a temporary scheme; never touches the active scheme. User-executed.
  [PowerWriteFact]
  public async Task Writes_land_on_a_temporary_duplicate_scheme_only()
  {
    var activeBefore = (await PowerSchemeService.GetActiveAsync()).Value;
    var source = PowerPlanCatalogProvider.BalancedPlanGuid;
    var destination = IntPtr.Zero;
    PowerDuplicateScheme(IntPtr.Zero, ref source, ref destination).Should().Be(0u);
    var temporary = Marshal.PtrToStructure<Guid>(destination);
    LocalFree(destination);
    try
    {
      var write = await PowerSchemeService.WriteValueIndexAsync(temporary, PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac, 37);
      write.Succeeded.Should().BeTrue();
      (await PowerSchemeService.ReadValueIndexAsync(temporary, PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac)).Value.Should().Be(37u);
    }
    finally
    {
      PowerDeleteScheme(IntPtr.Zero, ref temporary).Should().Be(0u);
    }
    (await PowerSchemeService.GetActiveAsync()).Value.Should().Be(activeBefore);
  }

  // powercfg is used here only as an independent oracle; production code never starts it.
  // One budget covers reading the output and waiting for exit; a stuck oracle is killed, never awaited forever.
  private static async Task<string> RunPowerCfgOracleAsync(string arguments)
  {
    using var process = Process.Start(new ProcessStartInfo("powercfg.exe", arguments)
    { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    try
    {
      var output = await process.StandardOutput.ReadToEndAsync(budget.Token);
      await process.WaitForExitAsync(budget.Token);
      return output;
    }
    finally
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(2000);
      }
    }
  }

  [DllImport("powrprof.dll")] private static extern uint PowerDuplicateScheme(IntPtr root, ref Guid source, ref IntPtr destination);
  [DllImport("powrprof.dll")] private static extern uint PowerDeleteScheme(IntPtr root, ref Guid scheme);
  [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}

internal sealed class PowerWriteFactAttribute : FactAttribute
{
  public PowerWriteFactAttribute()
  {
    // The same variable also lifts PowerWriteGuard, which otherwise refuses every real write in this assembly.
    if (!PowerWriteGuard.WritesRequested)
      Skip = $"Creates and deletes a temporary power scheme; set {PowerWriteGuard.Variable}=1 to run (user-executed).";
  }
}
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~PowerSchemeServiceLiveTests --logger "trx;LogFileName=task2-red.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: 4 failed（骨架原生层返回 1），1 skipped（写入测试）。

- [ ] **Step 3: 实现 `PowrProfNative`**

将 `PowrProfNative` 类整体替换为（`PowerSource` 与 `IPowrProfNative` 不变）：

```csharp
/// <summary>The only place that imports power scheme functions from PowrProf.dll (PWR-INV-SCHEME-API).</summary>
internal sealed class PowrProfNative : IPowrProfNative
{
  internal static readonly PowrProfNative Instance = new();
  private const uint ErrorNoMoreItems = 259;
  private const uint AccessScheme = 16;
  private const uint MaxSchemes = 256;

  public uint GetActiveScheme(out Guid scheme)
  {
    scheme = Guid.Empty;
    var rc = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
    if (rc != 0) return rc;
    try
    {
      scheme = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(pointer);
      return 0;
    }
    finally
    {
      LocalFree(pointer);
    }
  }

  public uint SetActiveScheme(Guid scheme) => PowerSetActiveScheme(IntPtr.Zero, ref scheme);

  public uint ReadFriendlyName(Guid scheme, out string name)
  {
    name = string.Empty;
    uint size = 0;
    var rc = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
    if (rc != 0) return rc;
    var buffer = new byte[size];
    rc = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
    if (rc != 0) return rc;
    name = System.Text.Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(size, (uint)buffer.Length)).TrimEnd('\0');
    return 0;
  }

  public uint EnumerateSchemes(out Guid[] schemes)
  {
    var found = new List<Guid>();
    schemes = [];
    for (uint index = 0; index < MaxSchemes; index++)
    {
      var buffer = new byte[16];
      var size = (uint)buffer.Length;
      var rc = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref size);
      if (rc == ErrorNoMoreItems) break;
      if (rc != 0) return rc;
      found.Add(new Guid(buffer));
    }
    schemes = found.ToArray();
    return 0;
  }

  public uint ReadValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, out uint value)
    => source == PowerSource.Ac
      ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value)
      : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);

  public uint WriteValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, uint value)
    => source == PowerSource.Ac
      ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value)
      : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value);

  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid,
    IntPtr powerSettingGuid, byte[]? buffer, ref uint bufferSize);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid,
    uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
    ref Guid powerSettingGuid, out uint acValueIndex);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
    ref Guid powerSettingGuid, out uint dcValueIndex);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
    ref Guid powerSettingGuid, uint acValueIndex);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
    ref Guid powerSettingGuid, uint dcValueIndex);
  [System.Runtime.InteropServices.DllImport("kernel32.dll")]
  private static extern IntPtr LocalFree(IntPtr memory);
}
```

- [ ] **Step 4: 运行确认 GREEN 并记录成本基线**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerSchemeServiceLiveTests|FullyQualifiedName~PowerSchemeServiceTests" --logger "trx;LogFileName=task2-green.trx" --results-directory .work/power-scheme-api-phase1
pwsh -NoProfile -File .work/power-scheme-api-phase1/power-api-spike.ps1 *> .work/power-scheme-api-phase1/task2-baseline.txt
```

Expected: 16 passed、1 skipped；`task2-baseline.txt` 记录 `GetActive` p50/p95/max 与列举耗时（评估时的只读 spike 脚本已保存于该目录）。

- [ ] **Step 5: 用户执行写入测试（会创建并删除一个临时电源方案）**

由用户在本机终端执行：

```powershell
$env:SMARTGUARD_POWER_WRITE_TESTS = '1'; dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~Writes_land_on_a_temporary_duplicate_scheme_only --logger "trx;LogFileName=task2-write.trx" --results-directory .work/power-scheme-api-phase1; Remove-Item Env:SMARTGUARD_POWER_WRITE_TESTS
```

Expected: 1 passed。未执行则在 Task 9 记录中标 `NOT RUN`。

- [ ] **Step 6: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowrProfNative.cs Tests/SmartGuard.Configuration.Tests/PowerSchemeServiceLiveTests.cs
git commit -m "feat(config): bind power scheme operations to PowrProf.dll" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Engine 探测、切换验证、分类与文案

**Files:**
- Create: `Tests/SmartGuard.Engine.Tests/FakePowrProfNative.cs`、`Tests/SmartGuard.Engine.Tests/PowerSchemeTestScope.cs`、`Tests/SmartGuard.Engine.Tests/PowerWriteGuard.cs`
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs`
- Rewrite: `Tests/SmartGuard.Engine.Tests/PowerPlanOperationTests.cs`

- [ ] **Step 1: Engine 测试替身、作用域与写入封锁**

`Tests/SmartGuard.Engine.Tests/FakePowrProfNative.cs`：与 Task 1 Step 1 的 Configuration 版本**内容相同**，仅命名空间改为 `SmartGuard.Engine.Tests`（两个测试程序集互不引用）：

```csharp
using SmartGuard.Configuration;

namespace SmartGuard.Engine.Tests;

internal sealed class FakePowrProfNative : IPowrProfNative
{
  private readonly object _sync = new();
  private readonly List<string> _calls = [];

  public Func<(uint Rc, Guid Scheme)> GetActive { get; set; } = () => (0, PowerPlanCatalogProvider.BalancedPlanGuid);
  public Func<Guid, uint> SetActive { get; set; } = _ => 0;
  public Func<Guid, (uint Rc, string Name)> ReadName { get; set; } = scheme => (0, scheme.ToString("D"));
  public Func<(uint Rc, Guid[] Schemes)> Enumerate { get; set; } = () => (0, []);
  public Func<Guid, Guid, PowerSource, (uint Rc, uint Value)> ReadValue { get; set; } = (_, _, _) => (0, 50);
  public Func<Guid, Guid, PowerSource, uint, uint> WriteValue { get; set; } = (_, _, _, _) => 0;

  public IReadOnlyList<string> Calls { get { lock (_sync) return _calls.ToArray(); } }

  private void Record(string call) { lock (_sync) _calls.Add(call); }

  public uint GetActiveScheme(out Guid scheme) { Record("get-active"); var r = GetActive(); scheme = r.Scheme; return r.Rc; }
  public uint SetActiveScheme(Guid scheme) { Record($"set-active {scheme:D}"); return SetActive(scheme); }
  public uint ReadFriendlyName(Guid scheme, out string name) { Record($"read-name {scheme:D}"); var r = ReadName(scheme); name = r.Name; return r.Rc; }
  public uint EnumerateSchemes(out Guid[] schemes) { Record("enumerate"); var r = Enumerate(); schemes = r.Schemes; return r.Rc; }

  public uint ReadValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, out uint value)
  {
    Record($"read-value {scheme:D} {subgroup:D} {setting:D} {source}");
    var r = ReadValue(scheme, setting, source);
    value = r.Value;
    return r.Rc;
  }

  public uint WriteValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, uint value)
  {
    Record($"write-value {scheme:D} {subgroup:D} {setting:D} {source} {value}");
    return WriteValue(scheme, setting, source, value);
  }
}
```

`Tests/SmartGuard.Engine.Tests/PowerSchemeTestScope.cs`：

```csharp
using System.Diagnostics;
using SmartGuard.Configuration;

namespace SmartGuard.Engine.Tests;

internal static class PowerSchemeTestScope
{
  // Blocks the legacy powercfg runner for the whole scope: a RED that still falls into the old
  // implementation fails instead of starting powercfg.exe and changing this machine's settings.
  private static readonly PowerCfgCommandResult Blocked =
    new(false, false, null, "", "powercfg is blocked inside PowerSchemeTestScope") { FailureKind = "start-failed" };

  internal static async Task WithNative(IPowrProfNative native, Func<Task> run, TimeSpan? deadline = null)
  {
    Enter(native, deadline);
    try { await run(); }
    finally
    {
      try { await WaitUntilIdleAsync(); }
      finally { Exit(); }
    }
  }

  internal static void WithNativeSync(IPowrProfNative native, Action run)
  {
    Enter(native, null);
    try { run(); }
    finally
    {
      try { WaitUntilIdleAsync().GetAwaiter().GetResult(); }
      finally { Exit(); }
    }
  }

  internal static async Task WaitUntilIdleAsync()
  {
    var watch = Stopwatch.StartNew();
    while (PowerSchemeService.IsCallInFlight)
    {
      if (watch.Elapsed > TimeSpan.FromSeconds(10))
        throw new TimeoutException("A fake native call is still in flight; the test left the gate held.");
      await Task.Delay(10);
    }
  }

  // Enter/Exit are synchronous so the AsyncLocal executor change applies to the caller's flow.
  private static void Enter(IPowrProfNative native, TimeSpan? deadline)
  {
    PowerSchemeService.UseExecutorForTests(new PowerSchemeExecutor(native, deadline));
    PowerCfgProcessRunner.RunOverrideForTests = (_, _) => Blocked;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(Blocked);
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
  }

  private static void Exit()
  {
    PowerSchemeService.UseExecutorForTests(null);
    PowerCfgProcessRunner.RunOverrideForTests = null;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = null;
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
  }
}
```

`Tests/SmartGuard.Engine.Tests/PowerWriteGuard.cs`：与 Configuration 版本内容相同，仅命名空间改为 `SmartGuard.Engine.Tests`。从本任务起，Engine.Tests 中任何未注入替身、经真实执行器的写入都会在调用方抛出，不会改动本机电源设置。

- [ ] **Step 2: 重写 `PowerPlanOperationTests.cs`（失败测试）**

```csharp
using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

[Collection("LoggingFallbackTests")]
public class PowerPlanOperationTests
{
  private static readonly Guid High = PowerPlanCatalogProvider.HighPerformancePlanGuid;
  private static readonly Guid Balanced = PowerPlanCatalogProvider.BalancedPlanGuid;

  // Theory parameters are strings: a public test method cannot expose the internal enum (CS0051).
  [Theory]
  [InlineData("api-error", 5, "ApiFailed")]
  [InlineData("timed-out", null, "TimedOut")]
  [InlineData("call-pending", null, "CallPending")]
  [InlineData("not-admitted", null, "NotAdmitted")]
  public void Scheme_failures_map_to_truthful_plan_failure_kinds(string kind, int? code, string expected)
    => PowerCfgExecutor.MapFailureKind(new PowerSchemeStatus(kind, code, 0)).Should().Be(Enum.Parse<PowerPlanFailureKind>(expected));

  [Theory]
  [InlineData("ApiFailed", 5, "电源服务调用失败（错误码 5）")]
  [InlineData("ApiFailed", null, "电源服务调用失败")]
  [InlineData("CallPending", null, "上次电源服务调用仍未返回，本轮未发起")]
  [InlineData("TimedOut", null, "电源服务调用超时")]
  [InlineData("VerificationMismatch", null, "切换后读回的计划不一致")]
  [InlineData("NotAdmitted", null, "电源状态已变化，本次未执行")]
  public void Failure_descriptions_are_chinese_labels(string kind, int? code, string expected)
    => PowerCfgExecutor.DescribeFailure(Enum.Parse<PowerPlanFailureKind>(kind), code).Should().Be(expected);

  [Fact]
  public async Task Probe_reads_active_scheme_and_name_through_the_scheme_service()
  {
    var native = new FakePowrProfNative { GetActive = () => (0, High), ReadName = _ => (0, "高性能") };
    var statuses = new List<PowerSchemeStatus>();
    PowerPlanProbeResult probe = default;
    await PowerSchemeTestScope.WithNative(native, async () =>
      probe = await PowerCfgExecutor.ProbeCurrentPlanAsync(CancellationToken.None, statuses.Add));
    probe.Succeeded.Should().BeTrue();
    probe.Plan.Should().Be(new PowerSchemeInfo(High, "高性能"));
    statuses.Should().ContainSingle().Which.Succeeded.Should().BeTrue();
  }

  [Fact]
  public async Task Probe_failure_carries_the_win32_code_and_skips_the_name()
  {
    var native = new FakePowrProfNative { GetActive = () => (5, Guid.Empty) };
    PowerPlanProbeResult probe = default;
    // Named argument: a single-argument call would bind the legacy powercfg overload until Task 7.
    await PowerSchemeTestScope.WithNative(native, async () =>
      probe = await PowerCfgExecutor.ProbeCurrentPlanAsync(CancellationToken.None, observe: null));
    probe.Succeeded.Should().BeFalse();
    probe.FailureKind.Should().Be(PowerPlanFailureKind.ApiFailed);
    probe.Win32Error.Should().Be(5);
    native.Calls.Should().Equal("get-active");
  }

  [Fact]
  public async Task Missing_name_does_not_fail_the_probe()
  {
    var native = new FakePowrProfNative { GetActive = () => (0, High), ReadName = _ => (2, string.Empty) };
    PowerPlanProbeResult probe = default;
    await PowerSchemeTestScope.WithNative(native, async () =>
      probe = await PowerCfgExecutor.ProbeCurrentPlanAsync(CancellationToken.None, observe: null));
    probe.Succeeded.Should().BeTrue();
    probe.Plan.Should().Be(new PowerSchemeInfo(High, null));
  }

  [Fact]
  public async Task Apply_is_successful_only_when_followup_probe_matches_target()
  {
    var recorder = new SchemeRecorder(Ok(true), Active(Balanced), Active(Balanced), Active(Balanced));
    var result = await PowerCfgExecutor.ApplyAndVerifyAsync(High, recorder.SetActiveAsync, recorder.GetActiveAsync,
      (_, _) => Task.CompletedTask, CancellationToken.None);
    result.Succeeded.Should().BeFalse();
    result.ObservedGuid.Should().Be(Balanced);
    result.FailureKind.Should().Be(PowerPlanFailureKind.VerificationMismatch);
    recorder.Calls.Should().Equal($"set {High:D}", "get", "get", "get");
  }

  [Fact]
  public async Task Apply_verification_accepts_bounded_delayed_convergence()
  {
    var delays = new List<TimeSpan>();
    var recorder = new SchemeRecorder(Ok(true), Active(Balanced), Active(High));
    var result = await PowerCfgExecutor.ApplyAndVerifyAsync(High, recorder.SetActiveAsync, recorder.GetActiveAsync,
      (delay, _) => { delays.Add(delay); return Task.CompletedTask; }, CancellationToken.None);
    result.Succeeded.Should().BeTrue();
    delays.Should().Equal(TimeSpan.FromMilliseconds(250));
    recorder.Calls.Should().Equal($"set {High:D}", "get", "get");
  }

  [Fact]
  public async Task Rejected_set_active_reports_its_win32_code_without_probing()
  {
    var recorder = new SchemeRecorder(Failed<bool>("api-error", 5));
    var result = await PowerCfgExecutor.ApplyAndVerifyAsync(High, recorder.SetActiveAsync, recorder.GetActiveAsync,
      (_, _) => Task.CompletedTask, CancellationToken.None);
    result.FailureKind.Should().Be(PowerPlanFailureKind.ApiFailed);
    result.Win32Error.Should().Be(5);
    recorder.Calls.Should().Equal($"set {High:D}");
  }

  [Fact]
  public async Task Set_active_refused_by_admission_is_not_admitted_without_probing()
  {
    var recorder = new SchemeRecorder(Failed<bool>("not-admitted", null));
    var result = await PowerCfgExecutor.ApplyAndVerifyAsync(High, recorder.SetActiveAsync, recorder.GetActiveAsync,
      (_, _) => Task.CompletedTask, CancellationToken.None);
    result.Succeeded.Should().BeFalse();
    result.FailureKind.Should().Be(PowerPlanFailureKind.NotAdmitted);
    recorder.Calls.Should().Equal($"set {High:D}");
  }

  private static PowerSchemeResult<bool> Ok(bool value) => new(new("none", null, 0), value);
  private static PowerSchemeResult<Guid> Active(Guid scheme) => new(new("none", null, 0), scheme);
  private static PowerSchemeResult<T> Failed<T>(string kind, int? code) => new(new(kind, code, 0), default);

  private sealed class SchemeRecorder(params object[] results)
  {
    private readonly Queue<object> _results = new(results);
    public List<string> Calls { get; } = [];

    public Task<PowerSchemeResult<bool>> SetActiveAsync(Guid scheme, CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Calls.Add($"set {scheme:D}");
      return Task.FromResult((PowerSchemeResult<bool>)_results.Dequeue());
    }

    public Task<PowerSchemeResult<Guid>> GetActiveAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Calls.Add("get");
      return Task.FromResult((PowerSchemeResult<Guid>)_results.Dequeue());
    }
  }
}
```

- [ ] **Step 3: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter FullyQualifiedName~PowerPlanOperationTests *> .work/power-scheme-api-phase1/task3-red.txt; $LASTEXITCODE
```

Expected: 非 0。说明：`ProbeCurrentPlanAsync(CancellationToken, Action<PowerSchemeStatus>)`、`MapFailureKind(PowerSchemeStatus)`、`DescribeFailure`、新 `ApplyAndVerifyAsync` 重载不存在而编译失败。**为满足 §5.1，先执行 Step 4 的骨架子步骤再取 RED。**

- [ ] **Step 4: 先加骨架再取行为 RED**

在 `PowerCfgExecutor` 类末尾加入（不改变现有方法）：

```csharp
  internal static Task<PowerPlanProbeResult> ProbeCurrentPlanAsync(CancellationToken cancellationToken,
    Action<PowerSchemeStatus>? observe)
    => Task.FromResult(new PowerPlanProbeResult(false, null, PowerPlanFailureKind.None, string.Empty));

  internal static Task<PowerPlanApplyResult> ApplyAndVerifyAsync(Guid targetGuid,
    Func<Guid, CancellationToken, Task<PowerSchemeResult<bool>>> setActive,
    Func<CancellationToken, Task<PowerSchemeResult<Guid>>> getActive,
    Func<TimeSpan, CancellationToken, Task> delayAsync, CancellationToken cancellationToken)
    => Task.FromResult(new PowerPlanApplyResult(false, targetGuid, null, PowerPlanFailureKind.None, string.Empty));

  internal static PowerPlanFailureKind MapFailureKind(PowerSchemeStatus status) => PowerPlanFailureKind.None;

  internal static string DescribeFailure(PowerPlanFailureKind kind, int? win32Error) => string.Empty;
```

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter FullyQualifiedName~PowerPlanOperationTests --logger "trx;LogFileName=task3-red.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: 三个测试项目编译退出码 0（先执行通用约束中的 `dotnet build`）；全部用例因返回值不符而失败。`Probe_failure...` 与 `Missing_name...` 必须经命名参数 `observe:` 绑定到新重载，否则它们会落入旧 powercfg 重载（此时已被作用域封锁，表现为失败但原因错误），取 RED 时核对失败消息不是 `ProcessStartFailed`。

- [ ] **Step 5: 实现**

将 Step 4 的四个骨架替换为：

```csharp
  // Guardian probe through PowerSchemeService (PWR-INV-SCHEME-API). A missing name is presentation only.
  internal static async Task<PowerPlanProbeResult> ProbeCurrentPlanAsync(CancellationToken cancellationToken,
    Action<PowerSchemeStatus>? observe = null)
  {
    var active = await PowerSchemeService.GetActiveAsync(cancellationToken).ConfigureAwait(false);
    observe?.Invoke(active.Status);
    if (!active.Succeeded)
      return new PowerPlanProbeResult(false, null, MapFailureKind(active.Status), active.Status.FailureKind, active.Status.Win32Error);
    var name = await PowerSchemeService.ReadNameAsync(active.Value, cancellationToken).ConfigureAwait(false);
    return new PowerPlanProbeResult(true, new PowerSchemeInfo(active.Value, name.Succeeded ? name.Value : null),
      PowerPlanFailureKind.None, string.Empty);
  }

  internal static async Task<PowerPlanApplyResult> ApplyAndVerifyAsync(
    Guid targetGuid,
    Func<Guid, CancellationToken, Task<PowerSchemeResult<bool>>> setActive,
    Func<CancellationToken, Task<PowerSchemeResult<Guid>>> getActive,
    Func<TimeSpan, CancellationToken, Task> delayAsync,
    CancellationToken cancellationToken)
  {
    var set = await setActive(targetGuid, cancellationToken).ConfigureAwait(false);
    if (!set.Succeeded)
      return new PowerPlanApplyResult(false, targetGuid, null, MapFailureKind(set.Status), set.Status.FailureKind, set.Status.Win32Error);

    Guid? observedGuid = null;
    var delays = new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750) };
    for (var attempt = 0; attempt < 3; attempt++)
    {
      if (attempt > 0)
        await delayAsync(delays[attempt - 1], cancellationToken).ConfigureAwait(false);
      var probe = await getActive(cancellationToken).ConfigureAwait(false);
      if (!probe.Succeeded)
        return new PowerPlanApplyResult(false, targetGuid, null, MapFailureKind(probe.Status), probe.Status.FailureKind, probe.Status.Win32Error);
      observedGuid = probe.Value;
      if (observedGuid == targetGuid)
        return new PowerPlanApplyResult(true, targetGuid, observedGuid, PowerPlanFailureKind.None, string.Empty);
    }

    return new PowerPlanApplyResult(false, targetGuid, observedGuid, PowerPlanFailureKind.VerificationMismatch,
      $"Active scheme did not converge to {targetGuid:D}.");
  }

  internal static PowerPlanFailureKind MapFailureKind(PowerSchemeStatus status) => status.FailureKind switch
  {
    "call-pending" => PowerPlanFailureKind.CallPending,
    "timed-out" => PowerPlanFailureKind.TimedOut,
    "not-admitted" => PowerPlanFailureKind.NotAdmitted,
    _ => PowerPlanFailureKind.ApiFailed,
  };

  internal static string DescribeFailure(PowerPlanFailureKind kind, int? win32Error) => kind switch
  {
    PowerPlanFailureKind.ApiFailed => win32Error is int code ? $"电源服务调用失败（错误码 {code}）" : "电源服务调用失败",
    PowerPlanFailureKind.CallPending => "上次电源服务调用仍未返回，本轮未发起",
    PowerPlanFailureKind.TimedOut => "电源服务调用超时",
    PowerPlanFailureKind.VerificationMismatch => "切换后读回的计划不一致",
    PowerPlanFailureKind.NotAdmitted => "电源状态已变化，本次未执行",
    _ => kind.ToString(),
  };
```

- [ ] **Step 6: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerPlanOperationTests|FullyQualifiedName~PowerCfgParserTests" --logger "trx;LogFileName=task3-green.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: 全部通过（`PowerCfgParserTests` 仍针对暂存的旧解析，Task 7 删除）。

- [ ] **Step 7: 提交**

```powershell
git add -- src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs Tests/SmartGuard.Engine.Tests/FakePowrProfNative.cs Tests/SmartGuard.Engine.Tests/PowerSchemeTestScope.cs Tests/SmartGuard.Engine.Tests/PowerWriteGuard.cs Tests/SmartGuard.Engine.Tests/PowerPlanOperationTests.cs
git commit -m "feat(engine): probe and verify power plans through the scheme service" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Runner 计划读取、切换、亮度同步与日志

**Files:**
- Modify: `src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs`（整体替换）
- Modify: `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs:242,489-501,526-529`
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs`（`SyncPlanBrightnessAsync`）
- Modify: `src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs`
- Modify: `Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs`、`Tests/SmartGuard.Engine.Tests/NativeProbeLoggingTests.cs`

- [ ] **Step 1: 写失败测试**

`GuardianProbeHardeningTests.cs`：

(a) `WithRunner` 增加参数 `bool useNativeApply = false`，构造中的 `applyPlan:` 改为：

```csharp
        applyPlan: useNativeApply ? null : apply ?? ((_, _) => throw new InvalidOperationException("Fixture forbids physical plan writes")));
```

`useNativeApply: true` 只允许出现在 `PowerSchemeTestScope.WithNative` 内部：作用域在 RED 期间封锁旧 powercfg 入口（同步与异步），GREEN 后写入落到替身；作用域之外即使误用，真实写入也会被 `PowerWriteGuard` 拒绝。

(b) 在类顶部加入：

```csharp
  private static readonly Guid High = PowerPlanCatalogProvider.HighPerformancePlanGuid;
  private static readonly Guid Balanced = PowerPlanCatalogProvider.BalancedPlanGuid;
```

(c) 用以下用例**替换**同名或对应旧用例：`Diagnostic_callback_failure_does_not_prevent_probe_failure_state_publication`、`Current_plan_probe_also_uses_cancelable_process_execution`、`Unconfirmed_process_cleanup_blocks_further_process_creation_in_that_guardian`、`Lifecycle_revocation_during_ac_sync_prevents_dc_write`、`Malformed_success_exit_does_not_announce_plan_probe_recovery`、`Persistent_plan_read_failure_does_not_flood_the_legacy_warning_path`；并新增 `Plan_apply_switches_through_the_scheme_service_and_verifies`：

```csharp
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
  public async Task Plan_probe_recovery_is_announced_once_after_a_failure()
  {
    var round = 0;
    var native = new FakePowrProfNative { GetActive = () => ++round == 1 ? (5, Guid.Empty) : (0, High) };
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
```

（`Oversized_initial_brightness_wait_cannot_hold_a_round_for_days` 不变；目录相关四个用例留到 Task 5。）

`NativeProbeLoggingTests.cs` 新增：

```csharp
  [Theory]
  [InlineData("plan-read", "timed-out")]
  [InlineData("plan-apply", "call-pending")]
  [InlineData("plan-brightness-dc", "timed-out")]
  public void Scheme_api_wait_failures_do_not_mention_child_processes(string operation, string kind)
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new(operation, kind, 2000, 2000, true, null, 0, false));
    record.Message.Should().Contain("电源服务调用仍在进行，期间不发起新调用，返回后自动恢复").And.NotContain("子进程");
  }

  [Fact]
  public void Scheme_api_error_reports_its_win32_code()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("plan-brightness-ac", "api-error", 1, 2000, true, null, 0, false, 5));
    record.Properties!["win32Error"].GetInt32().Should().Be(5);
    record.Message.Should().Contain("电源服务返回错误码 5").And.NotContain("子进程");
  }
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~GuardianProbeHardeningTests|FullyQualifiedName~NativeProbeLoggingTests" --logger "trx;LogFileName=task4-red.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: 三个测试项目编译退出码 0；替换与新增的 Runner 用例失败（Runner 仍走旧 powercfg 实现，该实现在作用域内被封锁为启动失败，替身原生层未被调用；本机不发生任何 powercfg 进程或亮度/方案写入）；两个日志用例失败（文案含“子进程”、无 `win32Error`）；目录用例与其他用例通过。取 RED 前后各执行一次 `powercfg /getactivescheme` 与 `powercfg /q SCHEME_CURRENT SUB_VIDEO VIDEONORMALLEVEL` 并存入 `task4-red-machine-state.txt`，证明本机方案与亮度值未变。

- [ ] **Step 3: 实现**

(a) `GuardianIterationRunner.NativeProbes.cs` 整体替换为：

```csharp
using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal sealed partial class GuardianIterationRunner
{
  // Only fixed operation names below enter this bounded, main-round-owned map.
  // It stores aggregate failure evidence, never configuration or process facts.
  private readonly Dictionary<string, (string Kind, long Count, long At)> _processFailures = new();
  private PowerPlanFailureKind? _lastPlanProbeFailure;
  private long _lastPlanProbeWarningAt, _suppressedPlanProbeWarnings;

  private void LogPlanProbeFailure(GuardConfig config, PowerPlanProbeResult probe)
  {
    var time = timeProvider ?? TimeProvider.System;
    var now = time.GetTimestamp();
    if (_lastPlanProbeFailure == probe.FailureKind
        && time.GetElapsedTime(_lastPlanProbeWarningAt, now) < TimeSpan.FromMinutes(1))
    {
      _suppressedPlanProbeWarnings++;
      return;
    }
    var properties = new Dictionary<string, JsonElement>
    {
      ["failureKind"] = JsonSerializer.SerializeToElement(probe.FailureKind.ToString()),
      ["suppressedCount"] = JsonSerializer.SerializeToElement(_suppressedPlanProbeWarnings),
    };
    if (probe.Win32Error is int code) properties["win32Error"] = JsonSerializer.SerializeToElement(code);
    var result = writeLog(config, LogLevel.Warn,
      $"电源计划探测失败，停止本轮切换：{PowerCfgExecutor.DescribeFailure(probe.FailureKind, probe.Win32Error)}；合并省略 {_suppressedPlanProbeWarnings} 次同类异常。",
      EventNameRegistry.PowerPlanProbeFailed, properties, null);
    if (result.IsAccepted() || result == GuardianLogWriteResult.SuppressedByLevel)
    {
      _lastPlanProbeFailure = probe.FailureKind;
      _lastPlanProbeWarningAt = now;
      _suppressedPlanProbeWarnings = 0;
    }
  }

  private int ReadBrightness(CancellationToken token)
  {
    token.ThrowIfCancellationRequested();
    var value = readBrightness is null ? brightness.GetBrightness(token) : readBrightness();
    token.ThrowIfCancellationRequested();
    return value;
  }

  private (int Percent, bool IsOnAc) ReadBattery(bool force, CancellationToken token)
  {
    token.ThrowIfCancellationRequested();
    var value = readBatteryInfo is null ? BatteryInfoProvider.GetBatteryInfo(force, token, nativeProbe) : readBatteryInfo(force);
    token.ThrowIfCancellationRequested();
    return value;
  }

  private async Task<PowerPlanProbeResult> ReadCurrentPlanAsync(CancellationToken token)
  {
    token.ThrowIfCancellationRequested();
    var value = readCurrentPlan is not null ? readCurrentPlan()
      : await PowerCfgExecutor.ProbeCurrentPlanAsync(token, status => ReportSchemeStatus("plan-read", status));
    token.ThrowIfCancellationRequested();
    return value;
  }

  // admit is re-evaluated by the executor right before the native call: lifecycle revocation is a
  // predicate, not a cancellation, so the caller's own earlier check is not enough.
  private async Task<PowerSchemeResult<bool>> SetActiveObservedAsync(Guid target, Func<bool> admit, CancellationToken token)
  {
    var result = await PowerSchemeService.SetActiveAsync(target, admit, token);
    ReportSchemeStatus("plan-apply", result.Status);
    return result;
  }

  private async Task<PowerSchemeResult<Guid>> GetActiveObservedAsync(CancellationToken token)
  {
    var result = await PowerSchemeService.GetActiveAsync(token);
    ReportSchemeStatus("plan-read", result.Status);
    return result;
  }

  // A scheme API call leaves no child process behind, so TerminationConfirmed is always true here.
  private void ReportSchemeStatus(string operation, PowerSchemeStatus status)
  {
    // A lifecycle refusal is neither a failure nor evidence of recovery.
    if (observeNativeProbe is null || status.FailureKind == "not-admitted") return;
    var budgetMs = (long)(operation == "plan-catalog"
      ? PowerSchemeService.EnumerateDeadline : PowerSchemeService.DefaultDeadline).TotalMilliseconds;
    var time = timeProvider ?? TimeProvider.System;
    var now = time.GetTimestamp();
    var hadFailure = _processFailures.TryGetValue(operation, out var previous);
    if (status.Succeeded)
    {
      if (!hadFailure) return;
      _processFailures.Remove(operation);
      SafeObserveNativeProbe(new(operation, "none", status.ElapsedMilliseconds, budgetMs, true, null, previous.Count, true));
      return;
    }
    var kind = status.FailureKind;
    var count = hadFailure ? previous.Count + 1 : 1;
    var emit = !hadFailure || previous.Kind != kind || time.GetElapsedTime(previous.At, now) >= TimeSpan.FromMinutes(1);
    _processFailures[operation] = (kind, emit ? 0 : count, emit ? now : previous.At);
    if (emit) SafeObserveNativeProbe(new(operation, kind, status.ElapsedMilliseconds, budgetMs,
      true, null, count - 1, false, status.Win32Error));
  }

  // Catalog still runs through the process runner until Task 5 moves it to the scheme service.
  private void ReportProcessResult(string operation, PowerCfgCommandResult result, long budgetMs)
  {
    if (observeNativeProbe is null) return;
    var time = timeProvider ?? TimeProvider.System;
    var now = time.GetTimestamp();
    var hadFailure = _processFailures.TryGetValue(operation, out var previous);
    if (result.Succeeded)
    {
      if (!hadFailure) return;
      _processFailures.Remove(operation);
      SafeObserveNativeProbe(new(operation, "none", result.ElapsedMilliseconds, budgetMs,
        result.TerminationConfirmed, result.ExitCode, previous.Count, true));
      return;
    }
    var kind = result.FailureKind != "none" ? result.FailureKind
      : result.TimedOut ? "timed-out" : !result.Started ? "start-failed" : "command-failed";
    var count = hadFailure ? previous.Count + 1 : 1;
    var emit = !hadFailure || previous.Kind != kind || time.GetElapsedTime(previous.At, now) >= TimeSpan.FromMinutes(1);
    _processFailures[operation] = (kind, emit ? 0 : count, emit ? now : previous.At);
    if (emit) SafeObserveNativeProbe(new(operation, kind, result.ElapsedMilliseconds, budgetMs,
      result.TerminationConfirmed, result.ExitCode, count - 1, false));
  }

  private void SafeObserveNativeProbe(WmiProbeObservation observation)
  {
    try { observeNativeProbe?.Invoke(observation); }
    catch
    {
      // Diagnostics are optional; the probe result remains authoritative even when
      // its consumer cannot accept an observation.
    }
  }
```

紧接着保留现有 `LoadPlanCatalogAsync` 方法**原文不变**（Task 5 替换），最后以 `}` 结束类。

(b) `GuardianIterationRunner.cs`：

第 242 行：

```csharp
          $"电源计划切换未验证成功：{PowerCfgExecutor.DescribeFailure(applied.FailureKind, applied.Win32Error)}",
```

以下替换以唯一代码片段为锚点（行号仅供定位，以片段为准）。

第 489–502 行，从 `else await PowerCfgExecutor.SyncPlanBrightnessAsync(` 到 `if (!apply.Succeeded)` 之前（含 `var apply = await applyOperation(target, cancellationToken);`）：

```csharp
      else await PowerCfgExecutor.SyncPlanBrightnessAsync(target, brightnessBefore, cancellationToken,
        (operation, status) => ReportSchemeStatus(operation, status), canMutate);
    }

    cancellationToken.ThrowIfCancellationRequested();
    if (!canMutate()) return (null, brightnessBefore, brightnessBefore);

    Func<Guid, CancellationToken, Task<PowerPlanApplyResult>> applyOperation =
      applyPlan ?? ((targetGuid, token) => PowerCfgExecutor.ApplyAndVerifyAsync(
        targetGuid,
        (guid, setToken) => SetActiveObservedAsync(guid, canMutate, setToken),
        GetActiveObservedAsync,
        Task.Delay,
        token));
    var apply = await applyOperation(target, cancellationToken);
    // Refused at the executor because the lifecycle revoked mutation after the check above:
    // same outcome as that check, not a switch failure.
    if (apply.FailureKind == PowerPlanFailureKind.NotAdmitted) return (null, brightnessBefore, brightnessBefore);
```

第 526–530 行（`return (new PowerPlanApplyResult(` 起，到 `brightnessBefore, after);` 止，含元组结尾）整段替换为：

```csharp
    return (new PowerPlanApplyResult(
      verified, target, finalProbe.ObservedGuid,
      verified ? PowerPlanFailureKind.None : finalProbe.Succeeded ? PowerPlanFailureKind.VerificationMismatch : finalProbe.FailureKind,
      verified ? string.Empty : finalProbe.Succeeded ? "Active scheme changed before final publication." : finalProbe.Diagnostic,
      verified ? null : finalProbe.Win32Error),
      brightnessBefore, after);
```

替换后检查该方法只剩一个 `brightnessBefore, after);`。

(c) `PowerCfgExecutor.SyncPlanBrightnessAsync` 整体替换为：

```csharp
  // Optional synchronization before switching: the target scheme takes effect with SetActive.
  internal static async Task SyncPlanBrightnessAsync(Guid planGuid, int brightness,
    CancellationToken cancellationToken, Action<string, PowerSchemeStatus>? observe = null,
    Func<bool>? canMutate = null)
  {
    if (brightness is < 0 or > 100) return;
    foreach (var source in new[] { PowerSource.Ac, PowerSource.Dc })
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (canMutate?.Invoke() == false) return;
      // The executor re-checks canMutate right before the native write.
      var result = await PowerSchemeService.WriteValueIndexAsync(planGuid, PowerSchemeSetting.VideoNormalLevel,
        source, (uint)brightness, canMutate, cancellationToken).ConfigureAwait(false);
      observe?.Invoke(source == PowerSource.Ac ? "plan-brightness-ac" : "plan-brightness-dc", result.Status);
      // Optional synchronization stops at the first failure or refusal.
      if (!result.Succeeded) return;
    }
  }
```

(d) `GuardianLoop.NativeProbes.cs` 的 `CreateNativeProbeEvent` 中，从 `var cleanup = ...` 一行到方法结尾替换为：

```csharp
    var schemeApi = observation.Operation is "plan-read" or "plan-apply" or "plan-brightness-ac" or "plan-brightness-dc";
    var cleanup = schemeApi
      ? observation.FailureKind is "timed-out" or "call-pending"
        ? "电源服务调用仍在进行，期间不发起新调用，返回后自动恢复"
        : observation.Win32Error is int code ? $"电源服务返回错误码 {code}" : "电源服务调用失败"
      : observation.TerminationConfirmed ? "子进程已退出或未启动" : "清理未确认，探测启动资格已关闭";
    var message = observation.Recovered ? $"系统探测已恢复：{label}。此前合并省略 {observation.SuppressedCount} 次同类异常。"
      : $"系统探测{(observation.FailureKind == "cancelled" ? "已取消" : "异常")}：{label}；{observation.FailureKind}；耗时 {observation.ElapsedMilliseconds}ms，预算 {observation.BudgetMilliseconds}ms；{degradation}；{cleanup}。合并省略 {observation.SuppressedCount} 次同类请求。";
    var properties = new Dictionary<string, JsonElement>
    {
      ["operation"] = JsonSerializer.SerializeToElement(observation.Operation),
      ["failureKind"] = JsonSerializer.SerializeToElement(observation.FailureKind),
      ["elapsedMs"] = JsonSerializer.SerializeToElement(observation.ElapsedMilliseconds),
      ["budgetMs"] = JsonSerializer.SerializeToElement(observation.BudgetMilliseconds),
      ["terminationConfirmed"] = JsonSerializer.SerializeToElement(observation.TerminationConfirmed),
      ["exitCode"] = JsonSerializer.SerializeToElement(observation.ExitCode),
      ["suppressedCount"] = JsonSerializer.SerializeToElement(observation.SuppressedCount),
      ["recovered"] = JsonSerializer.SerializeToElement(observation.Recovered),
      ["recoveryScope"] = JsonSerializer.SerializeToElement(observation.Operation == "plan-apply" ? "command-execution" : "probe"),
      ["actionResult"] = JsonSerializer.SerializeToElement(observation.Recovered ? "probe-recovered" : "unconfirmed"),
      ["degradation"] = JsonSerializer.SerializeToElement(degradation),
    };
    if (observation.Win32Error is int win32Error)
      properties["win32Error"] = JsonSerializer.SerializeToElement(win32Error);
    return EngineLogEvents.Create(observation.Recovered ? LogLevel.Info : LogLevel.Warn,
      observation.Recovered ? EventNameRegistry.NativeProbeRecovered : EventNameRegistry.NativeProbeFailed,
      message, OperationIdContext.Current, TraceIdContext.Current, properties: properties);
  }
```

（`label`、`degradation` 两个 switch 保持原文不变。）

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --logger "trx;LogFileName=task4-green.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: Engine.Tests 全部通过。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs Tests/SmartGuard.Engine.Tests/NativeProbeLoggingTests.cs
git commit -m "fix(engine): read, switch and sync power plans through the scheme service" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Guardian 计划目录改走电源 API

**Files:**
- Modify: `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs`（`TryLoadOnceAsync`）
- Modify: `src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs`（`LoadPlanCatalogAsync`，删除 `ReportProcessResult`）
- Modify: `src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs`（`schemeApi` 加入 `plan-catalog`）
- Modify: `Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs`、`Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs`、`Tests/SmartGuard.Engine.Tests/NativeProbeLoggingTests.cs`

说明：为保持自定义计划名称的现有显示能力，Guardian 启动时仍列举一次全部方案（实测约 0.6–0.8 秒），结果缓存于 `state.PlanCatalog`；迁移设计第 2.2 节随 Task 8 修正。

**成本与失败语义：**

- **总预算：** 列举与全部名称读取共享一个总预算 `PowerPlanCatalogProvider.GuardianLoadBudget = 5 秒`（以链接 `CancellationTokenSource` 实现）。单次调用的截止（列举 5 秒、名称 2 秒）仍各自生效，但整条链路不会超过总预算，最坏耗时不随方案数量增长。预算耗尽时停止补名，返回已读到的部分目录，报告 `timed-out`，不写会话缓存。守护照常按 GUID 进行，名称只影响展示。这与现有行为一致：每个 Guardian 只把结果存入 `state.PlanCatalog` 一次。
- **执行器排队语义：** 预算耗尽后，尚未进入原生层的名称读取被跳过。已在原生层中的那次调用继续持门，因此同一轮随后的 `plan-read` 会如实得到 `call-pending`（守护状态 `probe-failed`），门在原生函数返回后自动恢复。
- **隐藏方案：** 只忽略已确认的“不存在”（`api-error` 且 Win32 码 2）。其他结果（访问拒绝等错误码、`timed-out`、`call-pending`）都算失败：报告首个失败，不宣布恢复。
- **列出方案的名称读取失败：** 一律算失败。

- [ ] **Step 1: 写失败测试**

`PowerPlanCatalogProviderTests.cs` 新增（类已在 `PowerPlanCatalogProvider` 集合中）：

```csharp
    [Fact]
    public async Task TryLoadOnceAsync_reads_listed_and_hidden_known_names_through_the_scheme_service()
    {
        var custom = Guid.Parse("56c8d9b8-d34d-4d19-96a4-c177cf5f4882");
        var names = new Dictionary<Guid, string>
        {
            [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
            [custom] = "自定义模式",
            [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能",
            [PowerPlanCatalogProvider.PowerSaverPlanGuid] = "节能",
        };
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid, custom]),
            ReadName = guid => names.TryGetValue(guid, out var name) ? (0, name) : (2, string.Empty),
        };
        var statuses = new List<PowerSchemeStatus>();
        IReadOnlyDictionary<Guid, string> catalog = new Dictionary<Guid, string>();
        await PowerSchemeTestScope.WithNative(native, async () =>
            catalog = await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, statuses.Add));
        catalog.Should().BeEquivalentTo(names);
        statuses.Should().OnlyContain(status => status.Succeeded);
        native.Calls.Should().Contain("enumerate");
    }

    [Fact]
    public async Task TryLoadOnceAsync_reports_enumeration_failure_and_returns_empty()
    {
        var native = new FakePowrProfNative { Enumerate = () => (5, []) };
        var statuses = new List<PowerSchemeStatus>();
        IReadOnlyDictionary<Guid, string> catalog = new Dictionary<Guid, string> { [Guid.Empty] = "x" };
        await PowerSchemeTestScope.WithNative(native, async () =>
            catalog = await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, statuses.Add));
        catalog.Should().BeEmpty();
        statuses.Should().ContainSingle().Which.Should().Be(new PowerSchemeStatus("api-error", 5, statuses[0].ElapsedMilliseconds));
    }

    [Fact]
    public async Task Missing_hidden_known_scheme_is_not_reported_as_a_failure()
    {
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid]),
            ReadName = guid => guid == PowerPlanCatalogProvider.PowerSaverPlanGuid ? (2, string.Empty) : (0, "n"),
        };
        var statuses = new List<PowerSchemeStatus>();
        IReadOnlyDictionary<Guid, string> catalog = new Dictionary<Guid, string>();
        await PowerSchemeTestScope.WithNative(native, async () =>
            catalog = await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, statuses.Add));
        catalog.Should().NotContainKey(PowerPlanCatalogProvider.PowerSaverPlanGuid);
        statuses.Should().OnlyContain(status => status.Succeeded);
    }

    [Fact]
    public async Task Hidden_known_scheme_that_cannot_be_read_is_reported_as_a_failure()
    {
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid]),
            ReadName = guid => guid == PowerPlanCatalogProvider.PowerSaverPlanGuid ? (5, string.Empty) : (0, "n"),
        };
        var statuses = new List<PowerSchemeStatus>();
        await PowerSchemeTestScope.WithNative(native, async () =>
            await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, statuses.Add));
        statuses.Should().Contain(status => status.FailureKind == "api-error" && status.Win32Error == 5,
            "only a confirmed absence (Win32 2) is normal for a hidden scheme");
    }

    [Fact]
    public async Task Name_reads_share_one_total_budget_and_return_a_partial_catalog()
    {
        var listed = Enumerable.Range(0, 8).Select(i => new Guid($"00000000-0000-0000-0000-{i:D12}")).ToArray();
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, listed),
            // Each read is far below its own 2 s deadline; only their sum exceeds the total budget.
            ReadName = guid => { Thread.Sleep(100); return (0, guid.ToString("N")); },
        };
        var statuses = new List<PowerSchemeStatus>();
        IReadOnlyDictionary<Guid, string> catalog = new Dictionary<Guid, string>();
        var elapsed = TimeSpan.Zero;
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            catalog = await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, statuses.Add,
                budget: TimeSpan.FromMilliseconds(350));
            elapsed = watch.Elapsed;
            await PowerSchemeTestScope.WaitUntilIdleAsync();
            // A partial catalog is never cached: a second load enumerates again.
            await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, budget: TimeSpan.FromMilliseconds(350));
        });
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "the chain is bounded by the total budget, not by the scheme count");
        // Only the upper bound is asserted: how many 100 ms reads finish within the budget depends on the test pool.
        catalog.Count.Should().BeLessThan(listed.Length);
        statuses.Should().Contain(status => status.FailureKind == "timed-out");
        native.Calls.Count(call => call == "enumerate").Should().Be(2);
    }
```

（文件顶部需有 `using FluentAssertions;`、`using SmartGuard.Configuration;`。）

`GuardianProbeHardeningTests.cs` 中**替换**目录用例：`Cold_catalog_uses_the_cancelable_path_without_synchronous_enrichment`、`Cancellation_during_catalog_stops_the_round_before_state_publication`、`Catalog_failure_keeps_real_cleanup_uncertainty_in_the_logged_transition`，并**删除** `Catalog_unconfirmed_cleanup_prevents_the_remaining_hidden_queries`（不再有补全链与清理问题，隐藏方案行为由上面的 Configuration 用例覆盖）与 `ResetOverrides` 方法：

```csharp
  [Fact]
  public async Task Cold_catalog_is_loaded_once_through_the_scheme_service()
  {
    var native = new FakePowrProfNative { Enumerate = () => (0, [Balanced, High]) };
    await PowerSchemeTestScope.WithNative(native, () => WithRunner(async (runner, state, _) =>
    {
      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      state.PlanCatalog.Should().ContainKey(Balanced).And.ContainKey(High);
    }));
    native.Calls.Count(call => call == "enumerate").Should().Be(1, "the guardian loads names once and caches them");
  }

  [Fact]
  public async Task Cancellation_during_catalog_stops_the_round_before_state_publication()
  {
    using var release = new ManualResetEventSlim();
    var native = new FakePowrProfNative { Enumerate = () => { release.Wait(TimeSpan.FromSeconds(10)); return (0, [Balanced]); } };
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
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
      finally { release.Set(); }
    });
  }

  [Fact]
  public async Task Catalog_api_failure_is_reported_with_its_win32_code()
  {
    var observations = new List<WmiProbeObservation>();
    var native = new FakePowrProfNative { Enumerate = () => (5, []) };
    await PowerSchemeTestScope.WithNative(native, () => WithRunner(
      (runner, state, _) => runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None),
      observe: observations.Add));
    observations.Should().ContainSingle(x => x.Operation == "plan-catalog")
      .Which.Should().Match<WmiProbeObservation>(x => x.FailureKind == "api-error" && x.Win32Error == 5);
  }

  [Fact]
  public async Task Catalog_call_left_in_flight_makes_the_same_round_report_plan_read_pending()
  {
    using var release = new ManualResetEventSlim();
    var native = new FakePowrProfNative
    {
      Enumerate = () => (0, [Balanced]),
      ReadName = _ => { release.Wait(TimeSpan.FromSeconds(10)); return (0, "n"); },
    };
    var observations = new List<WmiProbeObservation>();
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      try
      {
        await WithRunner(async (runner, state, path) =>
        {
          await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
          StatusJsonReader.TryRead(path)!.guardReason.Should().Be("probe-failed",
            "the name read still owns the gate, so the plan read is truthfully refused");
        }, useNativePlanReader: true, observe: observations.Add);
        observations.Should().Contain(x => x.Operation == "plan-catalog" && x.FailureKind == "timed-out");
        observations.Should().Contain(x => x.Operation == "plan-read" && x.FailureKind == "call-pending");
        native.Calls.Should().NotContain("get-active", "a refused call never reaches the native layer");
      }
      finally { release.Set(); }
    }, deadline: TimeSpan.FromMilliseconds(100));
  }
```

`NativeProbeLoggingTests.Scheme_api_wait_failures_do_not_mention_child_processes` 增加一行 `[InlineData("plan-catalog", "timed-out")]`。

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~PowerPlanCatalogProviderTests --logger "trx;LogFileName=task5-red-config.trx" --results-directory .work/power-scheme-api-phase1
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~catalog|FullyQualifiedName~Catalog|FullyQualifiedName~NativeProbeLoggingTests" --logger "trx;LogFileName=task5-red-engine.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: 编译失败点——`TryLoadOnceAsync` 的 `observe` 参数类型仍为 `Action<PowerCfgCommandResult>`。**为满足 §5.1**，先在 `PowerPlanCatalogProvider` 中加入重载骨架再取 RED：

```csharp
  internal static readonly TimeSpan GuardianLoadBudget = TimeSpan.FromSeconds(5);

  internal static Task<IReadOnlyDictionary<Guid, string>> TryLoadOnceAsync(CancellationToken cancellationToken,
    Action<PowerSchemeStatus>? observe = null, TimeSpan? budget = null)
    => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
```

（骨架直接返回空目录，不走旧 powercfg 路径；新旧重载的第二参数类型不同，`TryLoadOnceAsync(token, status => …)` 依 lambda 参数类型绑定，按名称传 `budget:` 只绑定新重载。）

重跑上面两条命令。Expected: 三个测试项目编译退出码 0；新增 Configuration 用例失败（骨架返回空目录、不报告任何状态），其中 `Missing_hidden_known_scheme_is_not_reported_as_a_failure` 是保护性用例，对空目录可能通过，如实记录；原有 `PowerPlanCatalogProviderTests` 用例通过；Engine 目录用例（含 `Catalog_call_left_in_flight...`）失败；`plan-catalog` 日志用例失败。

- [ ] **Step 3: 实现**

`PowerPlanCatalogProvider.cs`：删除 Step 2 的骨架重载（保留 `GuardianLoadBudget`），并把原 `TryLoadOnceAsync` 替换为：

```csharp
  // Guardian-only: one load through PowerSchemeService (PWR-INV-SCHEME-API). Enumeration and every
  // name read share one total budget, so the chain is bounded regardless of the scheme count. Names are
  // presentation only; only a confirmed absence (Win32 2) of a hidden known scheme is normal.
  internal static async Task<IReadOnlyDictionary<Guid, string>> TryLoadOnceAsync(CancellationToken cancellationToken,
    Action<PowerSchemeStatus>? observe = null, TimeSpan? budget = null)
  {
    if (_sessionCache is not null) return _sessionCache;
    if (LoadImplementationForTests is { } loader) return loader();

    var catalog = new Dictionary<Guid, string>();
    using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    total.CancelAfter(budget ?? GuardianLoadBudget);
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    var complete = false;
    try
    {
      var schemes = await PowerSchemeService.EnumerateSchemesAsync(total.Token).ConfigureAwait(false);
      observe?.Invoke(schemes.Status);
      if (!schemes.Succeeded) return catalog;

      var listed = schemes.Value!;
      complete = true;
      foreach (var guid in listed.Concat(KnownSchemeGuids.Where(known => !listed.Contains(known))))
      {
        var name = await PowerSchemeService.ReadNameAsync(guid, total.Token).ConfigureAwait(false);
        if (name.Succeeded)
        {
          if (!string.IsNullOrWhiteSpace(name.Value)) catalog[guid] = name.Value!;
          continue;
        }
        var confirmedAbsent = !listed.Contains(guid) && name.Status is { FailureKind: "api-error", Win32Error: 2 };
        if (confirmedAbsent) continue;
        complete = false;
        observe?.Invoke(name.Status);
      }
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      // Total budget exhausted: keep the names read so far; the guardian continues by GUID.
      complete = false;
      observe?.Invoke(new PowerSchemeStatus("timed-out", null,
        (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds));
    }

    if (complete && catalog.Count > 0 && !HasMissingKnownSchemes(catalog)) _sessionCache = catalog;
    return catalog;
  }
```

`TryLoadAsync`、`LoadCoreAsync` 等 Settings 路径保持不变（第二阶段处理）。

`GuardianIterationRunner.NativeProbes.cs`：删除 `ReportProcessResult` 方法，并把 `LoadPlanCatalogAsync` 整体替换为：

```csharp
  private async Task<IReadOnlyDictionary<Guid, string>> LoadPlanCatalogAsync(CancellationToken token)
  {
    PowerSchemeStatus? firstFailure = null;
    var catalog = await PowerPlanCatalogProvider.TryLoadOnceAsync(token, status =>
    {
      if (!status.Succeeded) firstFailure ??= status;
    });
    token.ThrowIfCancellationRequested();
    // One aggregate outcome per load keeps per-operation recovery semantics.
    ReportSchemeStatus("plan-catalog", firstFailure ?? new PowerSchemeStatus("none", null, 0));
    return catalog;
  }
```

`GuardianLoop.NativeProbes.cs`：

```csharp
    var schemeApi = observation.Operation is "plan-read" or "plan-apply" or "plan-brightness-ac" or "plan-brightness-dc" or "plan-catalog";
```

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --logger "trx;LogFileName=task5-green-config.trx" --results-directory .work/power-scheme-api-phase1
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --logger "trx;LogFileName=task5-green-engine.trx" --results-directory .work/power-scheme-api-phase1
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --filter FullyQualifiedName~PowerPlanCatalogSingleSourceTests --logger "trx;LogFileName=task5-green-arch.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: 全部通过；`Guardian_iteration_must_load_catalog_via_PowerPlanCatalogProvider` 仍通过（源码仍含 `PowerPlanCatalogProvider.TryLoadOnceAsync(`）。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs Tests/SmartGuard.Engine.Tests/NativeProbeLoggingTests.cs
git commit -m "fix(engine): load guardian plan names through the scheme service" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 首次初始化：只读能力探测与确定的失败语义

**Files:**
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs`（`IsBrightnessSupported` → `ProbeBrightnessCapability`、`SyncPlanBrightness`、`DisableAdaptiveBrightness`）
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs`（新增 `BrightnessCapability`）
- Modify: `src/SmartGuard.Engine/Worker/GuardianFirstRunInitializer.cs`（`InitializeIfNeeded`）
- Create: `Tests/SmartGuard.Engine.Tests/GuardianFirstRunInitializerTests.cs`

**范围说明（已有缺陷，只记录不扩范围）：** 生产代码中 `InitializeIfNeeded` 当前没有调用方。`GuardianLoop` 构造了初始化器，但启动流程里的调用在 `7be38e78`（2026-08-03，`feat(logging): route component events through structured pipeline`）中被删除，生产启动时传入的 `powerCfgBrightnessSupported` 恒为 `true`（`GuardianLoop` 公共构造函数）。本任务仍要迁移初始化器，因为 Task 7 的门禁要求 Engine 不再含 powercfg 调用；同时修正它的失败语义，确保将来恢复入口时行为正确。**本任务不恢复启动入口**，是否恢复由用户另行决定（见文末修订记录）。直接调用初始化器的测试只证明初始化器本身的行为，不能证明首次启动有效；Task 9 的首次启动验收标为 `BLOCKED`。

**失败语义：**

| 能力探测结果 | 模式 | 写入 | 完成标记 | 日志 |
|--------------|------|------|----------|------|
| 读取成功 | 支持亮度设置项 | 关闭自适应亮度，并把亮度写到三个方案 | 全部写入成功才写 | `Info` “已对齐” |
| `api-error` 且 Win32 码 2 | WMI-only | 无 | 写 | `Info` “不含亮度设置项” |
| 其他失败（错误码、超时、在途） | **保持原值** | 无 | **不写**，下次启动重试 | `Warn` “探测失败”，附中文失败标签 |
| 支持，但任一写入失败 | 支持 | 在第一处失败停止 | **不写** | `Warn` “写入失败”，不出现“已对齐” |

- [ ] **Step 1: 写失败测试**

```csharp
using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

[Collection("LoggingFallbackTests")]
public sealed class GuardianFirstRunInitializerTests
{
  private static IEnumerable<string> Writes(FakePowrProfNative native)
    => native.Calls.Where(call => call.StartsWith("write-value", StringComparison.Ordinal));

  [Fact]
  public void Capability_probe_is_read_only_and_alignment_writes_only_brightness_settings()
  {
    var native = new FakePowrProfNative();
    var supported = false;
    var run = WithInitializer(native, (initializer, cfg) => initializer.InitializeIfNeeded(cfg, ref supported).Should().BeTrue());
    supported.Should().BeTrue();
    run.MarkerWritten.Should().BeTrue();
    run.Logs.Should().Contain("INIT: 三个电源计划的亮度设置项已对齐为 40%");
    native.Calls.Should().Contain(
      $"read-value {run.Config.BalancedPlanGuid:D} {PowerSchemeService.VideoSubgroupGuid:D} {PowerSchemeService.VideoNormalLevelGuid:D} Ac");
    Writes(native).Should().HaveCount(12);
    Writes(native).Should().OnlyContain(call =>
      (call.Contains(PowerSchemeService.AdaptiveBrightnessGuid.ToString("D")) && call.EndsWith(" 0"))
      || (call.Contains(PowerSchemeService.VideoNormalLevelGuid.ToString("D")) && call.EndsWith(" 40")),
      "the capability probe no longer writes a trial value");
  }

  [Fact]
  public void Confirmed_absent_brightness_setting_selects_wmi_only_mode_without_writes()
  {
    var native = new FakePowrProfNative { ReadValue = (_, _, _) => (2, 0) };
    var supported = true;
    var run = WithInitializer(native, (initializer, cfg) => initializer.InitializeIfNeeded(cfg, ref supported).Should().BeTrue());
    supported.Should().BeFalse();
    run.MarkerWritten.Should().BeTrue();
    Writes(native).Should().BeEmpty();
  }

  [Fact]
  public void Brightness_probe_failure_keeps_the_mode_and_retries_on_the_next_start()
  {
    var native = new FakePowrProfNative { ReadValue = (_, _, _) => (5, 0) };
    var supported = true;
    var run = WithInitializer(native, (initializer, cfg) => initializer.InitializeIfNeeded(cfg, ref supported).Should().BeFalse());
    supported.Should().BeTrue("an unknown capability is not evidence that the setting is missing");
    run.MarkerWritten.Should().BeFalse("the next start must retry the probe");
    run.Logs.Should().Contain(log => log.Contains("探测失败") && log.Contains("电源服务调用失败（错误码 5）"));
    Writes(native).Should().BeEmpty();
  }

  [Fact]
  public void Failed_alignment_write_is_not_reported_as_aligned_and_writes_no_marker()
  {
    var native = new FakePowrProfNative
    {
      WriteValue = (_, setting, _, _) => setting == PowerSchemeService.VideoNormalLevelGuid ? 5u : 0u,
    };
    var supported = false;
    var run = WithInitializer(native, (initializer, cfg) => initializer.InitializeIfNeeded(cfg, ref supported).Should().BeFalse());
    run.MarkerWritten.Should().BeFalse();
    run.Logs.Should().NotContain(log => log.Contains("已对齐"));
    run.Logs.Should().Contain(log => log.Contains("写入失败") && log.Contains("错误码 5"));
    Writes(native).Count(call => call.Contains(PowerSchemeService.VideoNormalLevelGuid.ToString("D")))
      .Should().Be(1, "alignment stops at the first failed write");
  }

  private sealed record InitializerRun(GuardConfig Config, bool MarkerWritten, IReadOnlyList<string> Logs);

  private delegate void InitializerAction(GuardianFirstRunInitializer initializer, GuardConfig config);

  private static InitializerRun WithInitializer(FakePowrProfNative native, InitializerAction run)
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardFirstRun_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var config = GuardConfig.CreateDefault(root);
      var marker = Path.Combine(root, "init");
      var logs = new List<string>();
      var brightness = new BrightnessService(new WmiProbeProcess((_, _, _, _) =>
        Task.FromResult(new PowerCfgCommandResult(true, false, 0, "SGWMI1|brightness|40", ""))));
      var initializer = new GuardianFirstRunInitializer(marker, brightness, (_, _, message) => logs.Add(message));
      // The scope also blocks the legacy powercfg runner, so the RED never writes this machine's settings.
      PowerSchemeTestScope.WithNativeSync(native, () => run(initializer, config));
      return new(config, File.Exists(marker), logs);
    }
    finally { Directory.Delete(root, true); }
  }
}
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter FullyQualifiedName~GuardianFirstRunInitializerTests --logger "trx;LogFileName=task6-red.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: 三个测试项目编译退出码 0。旧实现仍走 powercfg，但 `PowerSchemeTestScope` 已封锁旧入口，所有 powercfg 调用都表现为启动失败，本机不写入（旧实现原本会向平衡方案写入 AC 亮度 50 作为探测）。结果：

- `Capability_probe...`、`Brightness_probe_failure...`、`Failed_alignment_write...` 失败：替身未收到读写调用；旧实现把任何失败都判为“不支持”，并写入了完成标记。
- `Confirmed_absent...` 通过：这是保护性用例，旧实现被封锁后同样判为不支持、写标记、不写入。

- [ ] **Step 3: 实现**

`PowerPlanOperationResult.cs` 末尾加入：

```csharp
internal enum BrightnessCapability
{
  Supported,
  Unsupported,
  ProbeFailed,
}
```

`PowerCfgExecutor.cs` 中 `IsBrightnessSupported`、`SyncPlanBrightness`、`DisableAdaptiveBrightness` 三个方法替换为（访问级别改为 `internal`，调用方仅 Engine 内部；`IsBrightnessSupported` 更名为 `ProbeBrightnessCapability`，其唯一调用方是初始化器）：

```csharp
  private static readonly PowerSource[] BothSources = [PowerSource.Ac, PowerSource.Dc];

  // Read-only capability probe: never writes a trial value. Only a confirmed absence (Win32 2) means
  // "unsupported"; any other failure is unknown and must not switch the guardian to WMI-only mode.
  internal static (BrightnessCapability Capability, PowerSchemeStatus Status) ProbeBrightnessCapability(Guid probePlanGuid)
  {
    var read = PowerSchemeService.ReadValueIndexAsync(probePlanGuid, PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac)
      .GetAwaiter().GetResult();
    var capability = read.Succeeded ? BrightnessCapability.Supported
      : read.Status is { FailureKind: "api-error", Win32Error: 2 } ? BrightnessCapability.Unsupported
      : BrightnessCapability.ProbeFailed;
    return (capability, read.Status);
  }

  // Both return the first failed write, or a success status when every write succeeded.
  internal static PowerSchemeStatus SyncPlanBrightness(Guid planGuid, int brightness)
    => brightness is < 0 or > 100
      ? new PowerSchemeStatus("invalid-parameter", null, 0)
      : WriteBothSources(planGuid, PowerSchemeSetting.VideoNormalLevel, (uint)brightness);

  internal static PowerSchemeStatus DisableAdaptiveBrightness(Guid planGuid)
    => WriteBothSources(planGuid, PowerSchemeSetting.AdaptiveBrightness, 0);

  private static PowerSchemeStatus WriteBothSources(Guid planGuid, PowerSchemeSetting setting, uint value)
  {
    foreach (var source in BothSources)
    {
      var write = PowerSchemeService.WriteValueIndexAsync(planGuid, setting, source, value).GetAwaiter().GetResult();
      if (!write.Succeeded) return write.Status;
    }
    return new PowerSchemeStatus("none", null, 0);
  }

  internal static string DescribeSchemeFailure(PowerSchemeStatus status)
    => DescribeFailure(MapFailureKind(status), status.Win32Error);
```

`GuardianFirstRunInitializer.InitializeIfNeeded` 整体替换为（首尾的标记检查、CABC 提示、写标记与完成日志保持原文）：

```csharp
  internal bool InitializeIfNeeded(GuardConfig config, ref bool powerCfgBrightnessSupported)
  {
    if (File.Exists(_initMarkerPath)) return false;

    _writeLog(config, LogLevel.Info, "INIT: 开始首次初始化...");
    var (capability, probe) = PowerCfgExecutor.ProbeBrightnessCapability(config.BalancedPlanGuid);
    if (capability == BrightnessCapability.ProbeFailed)
    {
      // Unknown is not "unsupported": keep the current mode, write no marker, retry on the next start.
      _writeLog(config, LogLevel.Warn,
        $"INIT: 亮度设置项探测失败（{PowerCfgExecutor.DescribeSchemeFailure(probe)}），保持当前模式，下次启动重试");
      return false;
    }

    powerCfgBrightnessSupported = capability == BrightnessCapability.Supported;
    if (powerCfgBrightnessSupported)
    {
      var plans = new[] { config.ActivePlanGuid, config.BalancedPlanGuid, config.PowerSaverPlanGuid };
      PowerSchemeStatus? failure = null;
      foreach (var g in plans)
      {
        var disabled = PowerCfgExecutor.DisableAdaptiveBrightness(g);
        if (!disabled.Succeeded) { failure = disabled; break; }
      }

      var b = failure is null ? _brightness.GetBrightness() : -1;
      if (failure is null && b >= 0)
      {
        foreach (var g in plans)
        {
          var synced = PowerCfgExecutor.SyncPlanBrightness(g, b);
          if (!synced.Succeeded) { failure = synced; break; }
        }
      }

      if (failure is { } failed)
      {
        _writeLog(config, LogLevel.Warn,
          $"INIT: 电源计划亮度设置项写入失败（{PowerCfgExecutor.DescribeSchemeFailure(failed)}），未写完成标记，下次启动重试");
        return false;
      }
      if (b >= 0) _writeLog(config, LogLevel.Info, $"INIT: 三个电源计划的亮度设置项已对齐为 {b}%");
    }
    else
    {
      _writeLog(config, LogLevel.Info, "INIT: 本机电源计划不含亮度设置项，已切换为 WMI-only 模式（切计划后写回亮度）");
    }

    _writeLog(config, LogLevel.Info, "INIT: 请手动关闭 CABC 与节电模式降亮度");
    File.WriteAllText(_initMarkerPath, DateTime.Now.ToString("s"));
    _writeLog(config, LogLevel.Info, "INIT: 首次初始化完成");
    return true;
  }
```

失败后重试的成本：每次启动最多一次只读探测，外加不超过 12 次写入，每次写入截止 2 秒。写入全部经执行器，不会无界。

**Win32 码 2 的前提：** 只有本机已证实“亮度设置项不存在时 `PowerReadACValueIndex` 返回 2”，才把码 2 判为不支持。Task 2 的只读集成测试（`Brightness_setting_read_is_supported_or_reports_not_found`）在本机只能覆盖“支持”分支。如果在缺少该设置项的机器上观察到其他码，按探测失败处理，偏向保守：保持原模式并重试，不误入 WMI-only。

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --logger "trx;LogFileName=task6-green.trx" --results-directory .work/power-scheme-api-phase1
```

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs src/SmartGuard.Engine/Worker/GuardianFirstRunInitializer.cs Tests/SmartGuard.Engine.Tests/GuardianFirstRunInitializerTests.cs
git commit -m "fix(engine): probe brightness capability read-only through the scheme service" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: 删除 Engine 中的 powercfg 路径并加门禁

**Files:**
- Create: `Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs`
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs`、`src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs`
- Modify（删除旧失败分类后的引用，评审时已全量 grep 确认只有这 5 处，均为构造测试数据，无断言依赖旧名称）：`Tests/SmartGuard.Engine.Tests/GuardianPowerLifecycleIntegrationTests.cs:333`、`GuardInterruptionLoggingTests.cs:163`、`GuardStatusPublishingTests.cs:405`、`PlanPublicationConsistencyTests.cs:49`、`PolicyEngineTests.cs:565`
- Delete: `Tests/SmartGuard.Engine.Tests/PowerCfgParserTests.cs`

- [ ] **Step 1: 写门禁测试**

```csharp
using System.Text;
using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class PowerSchemeApiArchitectureTests
{
  [Fact]
  public void Engine_never_starts_powercfg()
  {
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src/SmartGuard.Engine"))
    {
      var source = File.ReadAllText(file, Encoding.UTF8);
      source.Should().NotContainEquivalentOf("\"powercfg", $"{file} must use PowerSchemeService (PWR-INV-SCHEME-API)");
      source.Should().NotContain("RunPowerCfg", $"{file} must not reach the powercfg runner");
      source.Should().NotContain("\"/setactive").And.NotContain("\"/getactivescheme");
    }
  }

  [Fact]
  public void Power_scheme_functions_are_imported_only_by_PowrProfNative()
  {
    string[] functions =
    [
      "PowerGetActiveScheme", "PowerSetActiveScheme", "PowerReadFriendlyName", "PowerEnumerate",
      "PowerReadACValueIndex", "PowerReadDCValueIndex", "PowerWriteACValueIndex", "PowerWriteDCValueIndex",
    ];
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      if (Path.GetFileName(file).Equals("PowrProfNative.cs", StringComparison.OrdinalIgnoreCase)) continue;
      var source = File.ReadAllText(file, Encoding.UTF8);
      foreach (var function in functions)
        source.Should().NotContain(function + "(", $"{file} imports a power scheme function outside PowrProfNative");
    }
  }
}
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --filter FullyQualifiedName~PowerSchemeApiArchitectureTests --logger "trx;LogFileName=task7-red.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: `Engine_never_starts_powercfg` 失败（`PowerCfgExecutor.cs` 仍含 `"/setactive`、`RunPowerCfg`）；第二个用例通过（保护性门禁）。

- [ ] **Step 3: 删除**

`PowerCfgExecutor.cs` 整体替换为只含 Task 3、4、6 的方法：

```csharp
using SmartGuard.Configuration;

namespace SmartGuard.Engine.Infrastructure;

/// <summary>Guardian power-plan operations on top of PowerSchemeService (PWR-INV-SCHEME-API).</summary>
public static class PowerCfgExecutor
{
  private static readonly PowerSource[] BothSources = [PowerSource.Ac, PowerSource.Dc];
```

随后依次放入以下成员，内容与 Task 3 Step 5、Task 4 Step 3(c)、Task 6 Step 3 完全一致：

- `ProbeCurrentPlanAsync`
- `ApplyAndVerifyAsync`（PowerScheme 委托版本）
- `SyncPlanBrightnessAsync`
- `ProbeBrightnessCapability`
- `SyncPlanBrightness`
- `DisableAdaptiveBrightness`
- `WriteBothSources`
- `MapFailureKind(PowerSchemeStatus)`
- `DescribeFailure`
- `DescribeSchemeFailure`

以 `}` 结束。Task 6 已声明过一份 `BothSources`，只保留类头这一份。

被删除的成员：

- 解析：`GuidRegex`、`ParseActiveSchemeGuid`、`ParseCurrentPlanInfo`、`ParseProbeResult`、`ParsePowerSchemeList`、`ContainsPowerCfgError`、两个 `GeneratedRegex`
- 旧 powercfg 读取：`ProbeCurrentPlan`、单参数 `ProbeCurrentPlanAsync(CancellationToken)`、`GetCurrentPlanInfo`、`GetCurrentPlanGuid`
- 旧 powercfg 切换：`SetActivePlanResult`、`SetActivePlan`、旧 `ApplyAndVerifyAsync`（`runAsync` 版本）、`RunPowerCfgAsync`
- 辅助：`FailedProbe`、`FailedApply`、`MapFailureKind(PowerCfgCommandResult)`
- `partial` 修饰符

删除单参数重载后，新 `ProbeCurrentPlanAsync` 的 `observe` 有默认值，单参数调用会绑定到它。

`PowerPlanOperationResult.cs` 枚举改为：

```csharp
internal enum PowerPlanFailureKind
{
  None,
  TimedOut,
  VerificationMismatch,
  ApiFailed,
  CallPending,
  NotAdmitted,
}
```

5 处测试引用中的 `PowerPlanFailureKind.CommandFailed` 与 `PowerPlanFailureKind.ParseFailed` 一律改为 `PowerPlanFailureKind.ApiFailed`。它们只用来构造“探测失败”的测试输入，语义不变。改完后执行：

```powershell
Get-ChildItem src,Tests -Recurse -Include *.cs | Select-String -Pattern 'PowerPlanFailureKind\.(CommandFailed|ParseFailed|ProcessStartFailed)'
foreach ($p in 'Configuration','Engine','Architecture') { dotnet build "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore -v q; "$p build exit=$LASTEXITCODE" }
```

Expected: 第一条无输出；三个项目编译退出码 0。

删除 `Tests/SmartGuard.Engine.Tests/PowerCfgParserTests.cs`：

```powershell
git rm -- Tests/SmartGuard.Engine.Tests/PowerCfgParserTests.cs
```

- [ ] **Step 4: 运行确认 GREEN**

```powershell
foreach ($p in 'Configuration','Engine','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task7-green-$p.trx" --results-directory .work/power-scheme-api-phase1 }
```

Expected: 三个项目全部通过。

- [ ] **Step 5: 提交**

```powershell
git add -- Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs Tests/SmartGuard.Engine.Tests/GuardianPowerLifecycleIntegrationTests.cs Tests/SmartGuard.Engine.Tests/GuardInterruptionLoggingTests.cs Tests/SmartGuard.Engine.Tests/GuardStatusPublishingTests.cs Tests/SmartGuard.Engine.Tests/PlanPublicationConsistencyTests.cs Tests/SmartGuard.Engine.Tests/PolicyEngineTests.cs
git commit -m "refactor(engine): remove powercfg paths from the engine and gate them" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: 契约与设计文档

**Files:**
- Modify: `docs/ARCHITECTURE-CONTRACT.md`、`docs/implementation-plans/2026-11-19-power-scheme-api-migration.md`
- Modify: `Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs`

- [ ] **Step 1: 写门禁测试**

在 `PowerSchemeApiArchitectureTests` 中加入：

```csharp
  [Fact]
  public void Contract_registers_the_scheme_api_invariant_and_owner()
  {
    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    contract.Should().Contain("PWR-INV-SCHEME-API").And.Contain("`PowerSchemeService`");
    contract.Should().NotContain("`powercfg /setactive`", "lifecycle gating now names the API call");
  }
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --filter FullyQualifiedName~Contract_registers_the_scheme_api_invariant_and_owner --logger "trx;LogFileName=task8-red.trx" --results-directory .work/power-scheme-api-phase1
```

Expected: 1 failed。

- [ ] **Step 3: 更新契约**

(a) 在 `| 守护状态稳定代码 |` 行之前插入一行：

```text
| 电源方案操作 | `PowerSchemeService`（Configuration：读/切换当前方案、方案名称、方案列表、`VIDEONORMALLEVEL`/`ADAPTBRIGHT` 读写；真实原生层每进程一个单飞执行器 `PowerSchemeExecutor`，截止 2 秒、列举 5 秒，在途返回前拒绝新调用为 `call-pending`，未进入原生层的工作项在调用方放弃或写准入撤销时跳过；健康事实 `IsCallInFlight`/`InFlightSince`/`LastSucceededAt`） | Engine 守护轮次、首次初始化、Guardian 计划名称；Settings 计划目录与 Tray 一键高性能为第二阶段前的登记例外 | `PowerSchemeService` | Engine 启动 `powercfg.exe`；在 `PowrProfNative` 之外导入电源方案函数；API 失败回退外部进程 |
```

(b) 在 `| \`PowerPlanCatalogProvider\` |` 行之后插入：

```text
| `PowerSchemeService` | `SmartGuard.Configuration` | 电源方案操作唯一入口（PWR-INV-SCHEME-API）；`PowrProfNative` 为电源方案函数唯一导入处（睡眠/唤醒通知注册不在此列） |
```

并把 `PowerPlanCatalogProvider` 行的说明末尾 `Engine/兼容调用才使用同步 \`TryLoad\`` 改为 `Engine 经 \`TryLoadOnceAsync\` 走 \`PowerSchemeService\`；Settings 与同步 \`TryLoad\` 在第二阶段前仍经 powercfg`。

(c) `PWR-LIFE-SUSPEND` 行中 `` `powercfg /setactive` `` 改为 `` `PowerSetActiveScheme` ``。

(d) 在 `| PWR-LIFE-TIME |` 行之后插入：

```text
| PWR-INV-SCHEME-API | 阻断 | 电源方案操作经 `PowerSchemeService`，真实原生层每进程一个单飞执行器。门属于工作项：原生函数返回后、结果对调用方可见前释放，调用方从不释放。超过截止或被取消的调用若已进入原生层，在原生函数返回前持有门，期间新调用为 `call-pending` 且不触达原生层；若尚未进入原生层，工作项跳过原生调用。写操作在进入原生层前复核生命周期准入，撤销时为 `not-admitted`，不算失败也不算恢复。恢复以在途调用返回为准，不以时间推断；不回退外部进程。Guardian 目录加载（列举与读名）共享 5 秒总预算。失败分类 `ApiFailed`（含 Win32 码）/`CallPending`/`TimedOut`/`VerificationMismatch`/`NotAdmitted`，告警使用中文标签。测试进程默认禁止真实写入。门禁：`PowerSchemeApiArchitectureTests`。 |
```

(e) 第 5 行 `` `plan-apply`恢复仅表示`setactive`命令执行链恢复 `` 改为 `` `plan-apply`恢复仅表示 `PowerSetActiveScheme` 调用恢复 ``。

迁移设计文档修正：

- 第 2.1 节执行器第 34–37 行：把“信号量由在途调用的完成延续释放”改为“信号量由工作项在原生函数返回后、结果对调用方可见前释放；尚未进入原生层的工作项在调用方超时/取消或写准入撤销时跳过原生调用并释放”，并补一行写操作的准入复核（`not-admitted`）。
- 第 2.2 节“Guardian 计划名称”一行改为：`启动时经 PowerPlanCatalogProvider.TryLoadOnceAsync 列举一次全部方案并读名称（保留自定义计划名称显示），补读隐藏的内置方案；列举与读名共享 5 秒总预算（取代旧的 2 秒目录预算），耗尽时返回部分目录并报告 timed-out；隐藏方案只忽略 Win32 2；结果缓存于 Guardian 状态`。
- 第 2.2 节“首次初始化”相关行补充：探测只读；码 2 为不支持，其他失败为探测失败（保持模式、不写标记）；任一写入失败不写标记。并注明：生产启动入口自 `7be38e78` 起缺失，第一阶段不恢复。
- 第 3 节失败分类补 `NotAdmitted`（生命周期撤销，不告警）。
- 第 4 节禁止项“在 `PowrProfNative` 之外 P/Invoke `powrprof.dll`”改为“在 `PowrProfNative` 之外导入**电源方案**函数（`PowerRegisterSuspendResumeNotification` 等睡眠/唤醒注册不在此列）”。
- 第 6 节“结果映射”中“`GetActive` 分配的内存经 `LocalFree` 释放（替身记录释放次数）”改为“`LocalFree` 位于真实原生层，由代码审查与真实 API 集成测试覆盖（替身层无法观察非托管释放）”。

- [ ] **Step 4: 运行确认 GREEN 与文档校验**

```powershell
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --logger "trx;LogFileName=task8-green.trx" --results-directory .work/power-scheme-api-phase1
foreach ($p in 'docs/ARCHITECTURE-CONTRACT.md','docs/implementation-plans/2026-11-19-power-scheme-api-migration.md') { $b=[IO.File]::ReadAllBytes($p); $null=[Text.UTF8Encoding]::new($false,$true).GetString($b); "$p bom=$($b[0] -eq 0xEF) ok" }
git diff --check -- docs
```

- [ ] **Step 5: 提交**

```powershell
git add -- docs/ARCHITECTURE-CONTRACT.md docs/implementation-plans/2026-11-19-power-scheme-api-migration.md Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs
git commit -m "docs(contract): register PowerSchemeService and PWR-INV-SCHEME-API" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: 全量验证、安装与验收

**Files:**
- Modify: `docs/implementation-plans/2026-11-19-power-scheme-api-migration.md`（追加“第一阶段实施与验证记录”）

- [ ] **Step 1: 全量测试**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1; $LASTEXITCODE
Get-ChildItem .work\test-results -Filter *.summary.json | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -Encoding utf8
```

Expected: 退出码 0；Pester、.NET、Tray Integration、Installer Integration 各层通过/失败/跳过数记录在案（`Writes_land_on_a_temporary_duplicate_scheme_only` 计为跳过，原因写明）。全量运行前后各执行一次 `powercfg /getactivescheme` 与 `powercfg /q SCHEME_CURRENT SUB_VIDEO VIDEONORMALLEVEL`，存入 `task9-machine-state.txt`，用于证明测试没有改动本机方案和亮度设置项。若前后不一致，先查明是哪一层测试改动了本机设置并记录，不直接判定为通过或失败。失败按 systematic-debugging 定位，不改测试迁就实现。

- [ ] **Step 2: 生成安装包**

```powershell
dotnet run --project src\SmartGuard.Packaging -- stage --root . --configuration Release
dotnet run --project src\SmartGuard.Packaging -- installer --root . --configuration Release
Get-ChildItem dist -Filter SmartGuard-Setup-*-x64.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { $_.FullName; (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
```

- [ ] **Step 3: 安装（用户确认 UAC）**

安装前备份 `D:\Apps\SmartGuard\bin` 与配置到 `.work/power-scheme-api-phase1/original-install`，记录配置 SHA256；用户运行安装包并确认 UAC；核对 Engine 与 Configuration DLL 与候选一致、配置哈希未变、Engine/Tray 在交互会话运行。

- [ ] **Step 4: 真实路径验收（需用户配合）**

- WPS 文字切到前台：`PowerPlanSwitched` 且“已确认生效”。
- 后台 180 秒观察期与返回检测事件正常。
- 等价性：应用内切换、系统设置手动切换、电源模式滑块介入三种情形，各记录一次 `PowerSchemeService` 读数（取自日志 `actualPlanGuid`）与 `powercfg /getactivescheme` 的对照。
- 运行至少 10 分钟：无 `ProcessStartFailed`；若有 `NativeProbeFailed`，须为 `api-error`/`timed-out`/`call-pending` 且带中文文案；Engine 进程下无 `powercfg.exe` 子进程（`Get-CimInstance Win32_Process -Filter "Name='powercfg.exe'"` 为空）。
- 至少一次睡眠/唤醒后核对恢复与耗时；未执行标 `NOT RUN`。
- Task 2 Step 5 写入测试的执行结果；未执行标 `NOT RUN`。
- Guardian 启动时的 `plan-catalog`：记录耗时，应在 5 秒总预算内；同时确认日志中没有 `plan-catalog` 告警，或者告警分类真实。
- 首次初始化真实路径：标 `BLOCKED`。原因是生产启动入口自 `7be38e78` 起缺失，第一阶段不恢复（见 Task 6 范围说明）。只记录 `GuardianFirstRunInitializerTests` 的自动化结果，不据此宣称首次启动验收通过。

- [ ] **Step 5: 记录并提交**

在迁移设计末尾追加“第一阶段实施与验证记录”：各任务 RED/GREEN 证据、Task 0 基线对照、`GetActive`/列举耗时基线、全量 summary 文件名与各层计数、安装包 SHA256 与核对结果、验收结果与 `NOT RUN` 项。

```powershell
git add -- docs/implementation-plans/2026-11-19-power-scheme-api-migration.md
git commit -m "docs(engine): record power scheme api phase one verification" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## 自查记录

- **设计覆盖：** 2.1 原生层→Task 0/2；执行器与健康事实→Task 1；对外操作→Task 1/2；2.2 计划读取/切换/亮度同步→Task 3/4，首次初始化→Task 6，Guardian 名称→Task 5；第 3 节分类与文案→Task 3/4/5；第 4 节契约与门禁→Task 7/8；第 6 节测试与验收→Task 1–9。Settings/Tray（第二阶段）与 WMI 闩锁收缩（第三阶段）不在本计划内。
- **偏离设计（已在 Task 8 修正设计文档）：** Guardian 仍列举一次全部方案以保留自定义计划名称；原生函数门禁收窄为电源方案函数（Engine 已用 `PowerRegisterSuspendResumeNotification`）；`LocalFree` 由真实原生层与集成测试覆盖而非替身计数。
- **RED 合规：**
  - 新类型、新重载与新参数都先以骨架引入（Task 0、Task 3 Step 4、Task 5 Step 2）。每个 RED 先编译三个测试项目，再取因行为缺口而失败的证据。
  - Theory 参数不暴露 internal 枚举，避免 CS0051。
  - 新旧重载并存期间，测试以命名参数显式绑定新重载。
  - 编译能否通过是评审时按 C# 规则推断的，没有逐任务实际编译。实施时以每个 RED 步骤的 `dotnet build` 为准。
- **系统修改边界：**
  - 迁移 RED 落入旧实现时，Engine 作用域把旧 powercfg 同步、异步入口一并封锁（Task 3 起）。
  - 真实写入由两个测试程序集的 `PowerWriteGuard` 拒绝（Task 1、Task 3 起），只有用户显式设置环境变量才放开。
  - Task 4 与 Task 9 用前后两次机器状态快照佐证本机未被改动。
- **并行隔离：**
  - 替身执行器经 `AsyncLocal` 只作用于当前测试的异步流，门与健康事实不跨测试共享。
  - 评审时逐个核对了 Engine.Tests 中构造 Runner/Loop 的 14 个测试类。除正在迁移的 `GuardianProbeHardeningTests` 夹具外，Runner 均注入了 `readCurrentPlan` 与 `applyPlan`；传入 `powerCfgBrightnessSupported: true` 的两处都注入了 `syncPlanBrightness`，其余都传 `false`。`GuardianLoop` 测试都传 `false`，并且只经反射调用 `EnsureConfigExists`、`WriteLog`、`WriteConsumerBatch`、`TryPublishLogHealth`、`DrainGuardObservations`，不运行守护轮次，因此不会触发真实的计划切换。`StructuredEngineLoggingTests` 与 `GuardRuleLoggingTests` 会经真实执行器只读加载目录，它们与替身测试不再共享门或替身。
  - 剩余的进程级共享量只有 `PowerCfgProcessRunner` 覆盖，暴露面与现有 `GuardianProbeHardeningTests` 相同。Task 5 之后 Engine 目录不再经 powercfg，该暴露消失。
- **执行器正确性：** 修订版执行器的代码已在隔离控制台程序中原样编译运行（证据见 Task 1 Step 4），同时复现了原方案的两个缺陷。
- **类型一致：** 以下名称在各任务中一致：
  - 原生层：`IPowrProfNative`、`PowrProfNative.Instance`、`PowerSource{Ac,Dc}`
  - 结果类型：`PowerSchemeSetting{VideoNormalLevel,AdaptiveBrightness}`、`PowerSchemeStatus(FailureKind,Win32Error,ElapsedMilliseconds)`、`PowerSchemeResult<T>(Status,Value)`
  - 执行器：`PowerSchemeExecutor(native, deadlineOverride, scheduler)`、`.RunAsync(operation, deadline, admit, cancellationToken)`
  - `PowerSchemeService`：`GetActiveAsync`、`SetActiveAsync(scheme, admit, ct)`、`ReadNameAsync`、`EnumerateSchemesAsync`、`ReadValueIndexAsync`、`WriteValueIndexAsync(scheme, setting, source, value, admit, ct)`、`IsCallInFlight`、`InFlightSince`、`LastSucceededAt`、`UseExecutorForTests`、`RealWritesAllowed`、`DefaultDeadline`、`EnumerateDeadline`、`VideoSubgroupGuid`、`VideoNormalLevelGuid`、`AdaptiveBrightnessGuid`
  - 目录：`PowerPlanCatalogProvider.{TryLoadOnceAsync(ct, observe, budget),GuardianLoadBudget}`
  - `PowerCfgExecutor`：`ProbeCurrentPlanAsync`、`ApplyAndVerifyAsync`、`SyncPlanBrightnessAsync`、`ProbeBrightnessCapability`、`SyncPlanBrightness`、`DisableAdaptiveBrightness`、`MapFailureKind`、`DescribeFailure`、`DescribeSchemeFailure`
  - 其他：`BrightnessCapability`、`WmiProbeObservation.Win32Error`、`PowerPlanFailureKind.{ApiFailed,CallPending,NotAdmitted}`
- **未覆盖项：** 执行器的准入复核在 Task 1 以可控调度器确定性验证；Runner 对 `NotAdmitted` 的处理只由 Task 3 的映射用例与代码审查覆盖，原因是无法确定性地让撤销恰好落在调用方检查之后、工作项启动之前。

---

## 外部审查修订记录（2026-11-19）

外部审查结论是“不建议按原文直接实施”。各项均已对照代码核实，处理如下。

| # | 审查发现 | 核实结果 | 修订 |
|---|----------|----------|------|
| 1 | 取消后，尚未启动的写操作仍会执行 | 成立。在隔离程序中复现：排队取消后原生替身仍然执行。另外，生命周期撤销是 `IsPolicyMutationAllowed` 谓词，不会取消迭代令牌，因此只复核令牌不够 | 票据 CAS（排队/已进入/已放弃）；工作项进入原生层前复核令牌与写准入（`admit`，失败为 `not-admitted`）；Task 1 新增排队取消、排队超时、准入复核三个确定性用例（`ManualTaskScheduler`） |
| 2 | 门释放与返回结果之间存在竞态 | 成立。原方案执行器 20 万次串行调用误报 852 次 `call-pending` | 门改由工作项 `finally` 在结果可见前释放，调用方从不释放；新增 5000 次串行调用用例，同时断言结果与门状态 |
| 3 | Task 4 的 RED 可能修改真实电源设置 | 成立。`useNativeApply` 与亮度同步用例在 RED 时会走旧 powercfg 实现，执行真实写入 | Engine 作用域封锁旧同步、异步入口；两个测试程序集用 `PowerWriteGuard` 拒绝真实写入；前后机器状态快照 |
| 4 | 多个任务无法达到声明的 RED/GREEN | 成立，且比审查列出的更多：旧分类在测试中共有 5 处引用（审查列出 1 处）；Task 5 的 RED 过滤器漏掉了新增的目录用例 | Theory 参数改为字符串；命名参数绑定新重载；列出 5 处引用及替换方式；替换以 526–530 行的唯一片段为锚；修正过滤器；每个 RED 先编译 |
| 5 | 目录加载没有总预算 | 成立。原方案还删除了现有的 2 秒总预算；隐藏方案的全部失败都被忽略 | 列举与读名共享 5 秒总预算，耗尽时返回部分目录、报告 `timed-out`、不缓存；隐藏方案只忽略 Win32 码 2；新增预算与访问拒绝用例，以及“在途调用使同轮 `plan-read` 为 `call-pending`”用例 |
| 6 | 首次初始化未形成验收闭环 | 成立。`InitializeIfNeeded` 的生产调用在 `7be38e78` 中被删除，至今没有调用方 | 能力探测改为三态；探测失败时保持模式、不写标记；写入失败不写标记、不报“已对齐”；Task 9 首次启动验收标 `BLOCKED`。**不在本计划内恢复入口** |
| 7 | 真实只读测试可能无限等待 | 成立 | 用同一个 10 秒预算覆盖异步读取与等待退出，超时后结束进程树 |

**用户决定（2026-11-19）：** 暂不恢复 `GuardianLoop` 启动时对 `InitializeIfNeeded` 的调用，只记录。该调用在 `7be38e78` 中被删除，属于已有缺陷。恢复会改变行为：首次启动会向三个方案写入亮度设置项，并写完成标记。将来恢复须另立任务，并有自己的 RED。本计划按修订版批准实施。
