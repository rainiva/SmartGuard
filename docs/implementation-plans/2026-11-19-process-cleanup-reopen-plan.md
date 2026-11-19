# 子进程清理闩锁可证实重开 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **已被替代，不得执行（2026-11-19）。** 本计划按第 2 版设计编写，覆盖已退役的 Guardian powercfg 闩锁（Task 5 等）。其日志改动还会回退第一阶段成果：
> - `WmiProbeObservation` 第 9 参数 `Win32Error` 被替换为字符串证据；
> - `CreateNativeProbeEvent` 被整段替换，删除电源 API 分支与 `win32Error` 属性。
>
> 第三阶段以[设计第 3 版](2026-11-19-process-cleanup-reopen.md)为准，待第二阶段完成后基于当时代码另出实施计划。本文件仅作历史参考。

**状态（历史）：** 第 2 版（已按外部审查修订）。设计见 [2026-11-19-process-cleanup-reopen.md](2026-11-19-process-cleanup-reopen.md)，审查处理见其第 11 节。

**Goal:** 子进程清理未确认后，powercfg 与 WMI 探测的启动资格只在同一子进程被证实退出、且两路管道读到 EOF 后自动重开，不再因一次误判永久停工。

**Architecture:** 读取任务改为以 EOF 为证据并使用独立取消令牌；运行器在 250ms 收尾等待后判定清理，仅向显式 `retainUnconfirmed: true` 的登记持有方移交未证实子进程（`UnconfirmedChild`）。`GuardianIterationRunner` 与 `WmiProbeProcess` 的布尔闩锁改为持有该对象，启动前以常数成本确认一次。拒绝使用 `cleanup-pending` / `ProcessCleanupPending`；日志用 `cleanupEvidence`（本次执行）与 `pendingCleanupEvidence`（持有对象）区分两个事实。

**Tech Stack:** .NET 8、C#、xUnit 2.5.3、FluentAssertions 8、PowerShell（测试夹具子进程）。

**证据目录：** `.work/process-cleanup-reopen/`。

**通用约束：** 源文件 UTF-8 无 BOM；精确补丁编辑；每个任务只暂存本任务文件；Conventional Commits，结尾 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`；分支 `fix/logging-observability`。**RED 规则：** 依 `PROJECT-RULES.md` §5.1，RED 必须因目标行为缺口失败；为此 Task 0 先引入不改变行为的脚手架，此后所有 RED 都能编译运行。

---

## 文件结构

| 文件 | 动作 | 责任 |
|------|------|------|
| `src/SmartGuard.Configuration/UnconfirmedChild.cs` | 新建 | 持有终止未证实的子进程；常数成本证明 |
| `src/SmartGuard.Configuration/PowerCfgCommandResult.cs` | 修改 | 新增 `PendingCleanup` |
| `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs` | 修改 | `PipeReadOutcome`、EOF 读取、收尾后判定、可选移交、测试钩子 |
| `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs` | 修改 | Guardian 单次目录加载传递保留选择 |
| `src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs` | 修改 | `ProcessCleanupPending` |
| `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs` | 修改 | 拒绝映射；Guardian 调用保留；亮度同步分类 |
| `src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs` | 修改 | 观测字段；闩锁持有 `UnconfirmedChild` |
| `src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs` | 修改 | 日志文案与属性 |
| `src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs` | 修改 | 闩锁、统一门控、恢复、目录分类 |
| `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs` | 修改 | 亮度同步经同一门控 |
| `docs/ARCHITECTURE-CONTRACT.md` | 修改 | `PWR-INV-CLEANUP-REOPEN` |
| `Tests/SmartGuard.Configuration.Tests/UnconfirmedChildTests.cs` | 新建 | 证明规则 |
| `Tests/SmartGuard.Configuration.Tests/PowerCfgProcessRunnerTests.cs` | 修改 | 真实进程竞态、移交、成本 |
| `Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs` | 修改 | 拒绝映射、重开、亮度/目录分类 |
| `Tests/SmartGuard.Engine.Tests/NativeProbeLoggingTests.cs` | 修改 | 日志格式 |
| `Tests/SmartGuard.Engine.Tests/WmiProbeProcessTests.cs` | 修改 | WMI 重开与单飞 |
| `Tests/SmartGuard.Architecture.Tests/GuardianProbeIsolationArchitectureTests.cs` | 修改 | 架构门禁 |

---

### Task 0: 不改变行为的脚手架

**Files:**
- Create: `src/SmartGuard.Configuration/UnconfirmedChild.cs`
- Modify: `src/SmartGuard.Configuration/PowerCfgCommandResult.cs`、`src/SmartGuard.Configuration/PowerCfgProcessRunner.cs`、`src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs`、`src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs`

本任务只引入类型、可选参数、未使用的枚举成员与测试钩子，不改变任何运行语义；以现有全部测试保持通过为验收。

- [ ] **Step 1: 新建骨架 `UnconfirmedChild.cs`**

```csharp
using System.Diagnostics;

namespace SmartGuard.Configuration;

/// <summary>Scaffold only; proof semantics arrive with their tests.</summary>
public sealed class UnconfirmedChild
{
  private UnconfirmedChild(string evidence) => Evidence = evidence;

  public string Evidence { get; private set; }

  internal static UnconfirmedChild FromProcess(Process process, Task<PipeReadOutcome> output,
    Task<PipeReadOutcome> error, CancellationTokenSource? readers, string evidence) => new(evidence);

  internal static UnconfirmedChild FromExecution(Task<PowerCfgCommandResult> execution) => new("executor-pending");

  internal static UnconfirmedChild Unprovable(string evidence = "executor-pending") => new(evidence);

  internal static UnconfirmedChild ForTests(Func<bool> proof, string evidence) => new(evidence);

  public bool TryConfirm() => false;
}
```

- [ ] **Step 2: 其余脚手架**

`PowerCfgCommandResult.cs` 在 `ElapsedMilliseconds` 之后加入：

```csharp
  /// <summary>Non-null only when <see cref="TerminationConfirmed"/> is false and the caller asked to retain the child.</summary>
  public UnconfirmedChild? PendingCleanup { get; init; }
```

`PowerCfgProcessRunner.cs`：

(a) 在 `namespace SmartGuard.Configuration;` 之后、类之前加入：

```csharp
internal readonly record struct PipeReadOutcome(string Text, bool ReachedEof);
```

(b) 类内 `RunAsyncOverrideForTests` 之后加入：

```csharp
  // Invoked with the child PID after the termination request and before the settle window;
  // fixes race-test ordering. Tests filter by PID because other test classes run in parallel.
  internal static Action<int>? AfterTerminationRequestForTests;
```

(c) `RunPowerCfgAsync` 与 `RunProcessAsync` 增加末位可选参数（暂不使用）：

```csharp
  internal static Task<PowerCfgCommandResult> RunPowerCfgAsync(
    string arguments,
    CancellationToken cancellationToken = default,
    TimeSpan? timeout = null,
    bool retainUnconfirmed = false)
  {
    if (RunAsyncOverrideForTests is not null)
      return RunAsyncOverrideForTests(arguments, timeout ?? DefaultTimeout, cancellationToken);

    if (RunOverrideForTests is not null)
      return Task.FromResult(RunOverrideForTests(arguments, timeout ?? DefaultTimeout));

    return RunProcessAsync(
      "powercfg.exe",
      arguments,
      timeout ?? DefaultTimeout,
      cancellationToken,
      retainUnconfirmed);
  }
```

```csharp
  internal static Task<PowerCfgCommandResult> RunProcessAsync(
    string fileName,
    string arguments,
    TimeSpan timeout,
    CancellationToken cancellationToken = default,
    bool retainUnconfirmed = false)
    => RunProcessCoreAsync(fileName, arguments, timeout, cancellationToken);
```

(d) `RunProcessCoreAsync` 中 `var killSucceeded = TryKill(proc);` 之后加入一行：

```csharp
    AfterTerminationRequestForTests?.Invoke(proc.Id);
```

`PowerPlanOperationResult.cs` 枚举末尾追加（不改变已有成员值）：

```csharp
  VerificationMismatch,
  ProcessCleanupPending,
}
```

`WmiProbeProcess.cs`：

(a) 记录追加两个可选参数：

```csharp
internal sealed record WmiProbeObservation(string Operation, string FailureKind,
  long ElapsedMilliseconds, long BudgetMilliseconds, bool TerminationConfirmed,
  int? ExitCode, long SuppressedCount, bool Recovered,
  string? CleanupEvidence = null, string? PendingCleanupEvidence = null);
```

(b) 方法组无法绑定带可选参数的方法，构造函数中默认执行器改为 lambda（语义不变）：

```csharp
    _execute = execute ?? ((file, args, budget, token) => PowerCfgProcessRunner.RunProcessAsync(file, args, budget, token));
```

- [ ] **Step 3: 证明无行为变化**

```powershell
New-Item -ItemType Directory -Force .work\process-cleanup-reopen | Out-Null
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --logger "trx;LogFileName=task0-config.trx" --results-directory .work/process-cleanup-reopen
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --logger "trx;LogFileName=task0-engine.trx" --results-directory .work/process-cleanup-reopen
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --logger "trx;LogFileName=task0-arch.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 三个项目全部通过，与改动前通过数一致（改动前先跑一次同样命令保存 `task0-baseline-*.trx` 作比对）。

- [ ] **Step 4: 提交**

```powershell
git add -- src/SmartGuard.Configuration/UnconfirmedChild.cs src/SmartGuard.Configuration/PowerCfgCommandResult.cs src/SmartGuard.Configuration/PowerCfgProcessRunner.cs src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs
git commit -m "refactor(config): scaffold unconfirmed child cleanup types without behaviour change" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 1: `UnconfirmedChild` 证明规则

**Files:**
- Modify: `src/SmartGuard.Configuration/UnconfirmedChild.cs`
- Test: `Tests/SmartGuard.Configuration.Tests/UnconfirmedChildTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Diagnostics;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class UnconfirmedChildTests
{
    private static readonly Task<PipeReadOutcome> Eof = Task.FromResult(new PipeReadOutcome("", true));
    private static readonly Task<PipeReadOutcome> StoppedBeforeEof = Task.FromResult(new PipeReadOutcome("", false));
    private static readonly Task<PipeReadOutcome> Cancelled = Task.FromCanceled<PipeReadOutcome>(new CancellationToken(true));

    [Fact]
    public void Unprovable_child_never_confirms_and_keeps_its_evidence()
    {
        var child = UnconfirmedChild.Unprovable("pipes-open");
        child.TryConfirm().Should().BeFalse();
        child.TryConfirm().Should().BeFalse();
        child.Evidence.Should().Be("pipes-open");
    }

    [Fact]
    public void Test_proof_is_consulted_until_it_succeeds()
    {
        var answers = new Queue<bool>([false, true]);
        var child = UnconfirmedChild.ForTests(() => answers.Dequeue(), "pipes-open");
        child.TryConfirm().Should().BeFalse();
        child.TryConfirm().Should().BeTrue();
        child.Evidence.Should().Be("confirmed");
        child.TryConfirm().Should().BeTrue("a proven child stays proven without asking again");
    }

    [Fact]
    public void Execution_certificate_confirms_only_after_a_confirmed_result()
    {
        var execution = new TaskCompletionSource<PowerCfgCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = UnconfirmedChild.FromExecution(execution.Task);
        child.TryConfirm().Should().BeFalse();
        child.Evidence.Should().Be("executor-pending");
        execution.SetResult(new PowerCfgCommandResult(true, false, 0, "", ""));
        child.TryConfirm().Should().BeTrue();
        child.Evidence.Should().Be("confirmed");
    }

    [Fact]
    public void Execution_that_left_its_own_unconfirmed_child_delegates_the_proof()
    {
        var execution = new TaskCompletionSource<PowerCfgCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = UnconfirmedChild.FromExecution(execution.Task);
        execution.SetResult(new PowerCfgCommandResult(true, true, null, "", "")
        {
            FailureKind = "cleanup-unconfirmed",
            TerminationConfirmed = false,
            PendingCleanup = UnconfirmedChild.Unprovable("not-exited"),
        });
        child.TryConfirm().Should().BeFalse();
        child.Evidence.Should().Be("not-exited");
    }

    [Fact]
    public void Faulted_execution_is_not_proof_of_termination()
    {
        var execution = new TaskCompletionSource<PowerCfgCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = UnconfirmedChild.FromExecution(execution.Task);
        execution.SetException(new IOException("executor failed"));
        child.TryConfirm().Should().BeFalse();
    }

    [Fact]
    public void Cancelled_reader_is_never_proof_even_after_the_child_exited()
    {
        var process = StartExitedPing();
        var child = UnconfirmedChild.FromProcess(process, Cancelled, Eof, null, "pipes-open");
        child.TryConfirm().Should().BeFalse();
        child.Evidence.Should().Be("reader-failed");
    }

    [Fact]
    public void Reader_that_stopped_before_eof_is_not_proof()
    {
        var process = StartExitedPing();
        var child = UnconfirmedChild.FromProcess(process, Eof, StoppedBeforeEof, null, "pipes-open");
        child.TryConfirm().Should().BeFalse();
        child.Evidence.Should().Be("pipes-open");
    }

    [Fact]
    public void Exited_child_with_both_pipes_at_eof_is_proven()
    {
        var process = StartExitedPing();
        var child = UnconfirmedChild.FromProcess(process, Eof, Eof, new CancellationTokenSource(), "pipes-open");
        child.TryConfirm().Should().BeTrue();
        child.Evidence.Should().Be("confirmed");
        child.TryConfirm().Should().BeTrue();
    }

    [Fact]
    public void Running_child_receives_one_direct_termination_request_and_is_not_proven_yet()
    {
        using var observer = StartPing(30);
        var process = Process.GetProcessById(observer.Id);
        var child = UnconfirmedChild.FromProcess(process, Eof, Eof, null, "not-exited");
        child.TryConfirm().Should().BeFalse("confirmation never waits for the exit it requested");
        child.Evidence.Should().Be("not-exited");
        observer.WaitForExit(5000).Should().BeTrue("TryConfirm requested termination of the held child");
        child.TryConfirm().Should().BeTrue("the exit is observed by a later check");
    }

    private static Process StartPing(int count) => Process.Start(new ProcessStartInfo("ping.exe", $"-n {count} 127.0.0.1")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
    })!;

    private static Process StartExitedPing()
    {
        var process = StartPing(1);
        process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000).Should().BeTrue();
        return process;
    }
}
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~UnconfirmedChildTests --logger "trx;LogFileName=task1-red.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 编译通过；`Test_proof...`、`Execution_certificate...`、`Execution_that_left...`（evidence 断言）、`Cancelled_reader...`、`Exited_child...`、`Running_child...` 失败（骨架 `TryConfirm` 恒为 false、不更新 evidence、不终止进程）；`Unprovable...`、`Faulted...`、`Reader_that_stopped...` 通过。

- [ ] **Step 3: 实现**

`UnconfirmedChild.cs` 整体替换为：

```csharp
using System.Diagnostics;

namespace SmartGuard.Configuration;

/// <summary>
/// Owns one child process whose termination has not been proven (PWR-INV-CLEANUP-REOPEN).
/// Proof = the same child exited AND both redirected pipes were read to EOF. A cancelled, faulted
/// or early-stopped reader is never proof. <see cref="TryConfirm"/> never waits and never enumerates
/// processes: it reads exit/task state and, while the child still runs, issues one direct
/// TerminateProcess. Not thread-safe: the owning latch serializes calls.
/// </summary>
public sealed class UnconfirmedChild
{
  private Process? _process;
  private readonly Task<PipeReadOutcome>? _output;
  private readonly Task<PipeReadOutcome>? _error;
  private CancellationTokenSource? _readers;
  private readonly Task<PowerCfgCommandResult>? _execution;
  private readonly Func<bool>? _proofForTests;
  private bool _confirmed;

  private UnconfirmedChild(string evidence, Process? process = null, Task<PipeReadOutcome>? output = null,
    Task<PipeReadOutcome>? error = null, CancellationTokenSource? readers = null,
    Task<PowerCfgCommandResult>? execution = null, Func<bool>? proofForTests = null)
  {
    Evidence = evidence;
    _process = process;
    _output = output;
    _error = error;
    _readers = readers;
    _execution = execution;
    _proofForTests = proofForTests;
  }

  /// <summary>kill-failed, not-exited, pipes-open, reader-failed, executor-pending or confirmed.</summary>
  public string Evidence { get; private set; }

  internal static UnconfirmedChild FromProcess(Process process, Task<PipeReadOutcome> output,
    Task<PipeReadOutcome> error, CancellationTokenSource? readers, string evidence)
  {
    ObserveFault(output);
    ObserveFault(error);
    return new(evidence, process, output, error, readers);
  }

  internal static UnconfirmedChild FromExecution(Task<PowerCfgCommandResult> execution)
  {
    ObserveFault(execution);
    return new("executor-pending", execution: execution);
  }

  internal static UnconfirmedChild Unprovable(string evidence = "executor-pending") => new(evidence);

  internal static UnconfirmedChild ForTests(Func<bool> proof, string evidence) => new(evidence, proofForTests: proof);

  /// <summary>Single source of the pipe half of the proof, shared with the runner's settle decision.</summary>
  internal static string PipeEvidence(Task<PipeReadOutcome> output, Task<PipeReadOutcome> error)
  {
    if (output.IsFaulted || output.IsCanceled || error.IsFaulted || error.IsCanceled) return "reader-failed";
    return output.IsCompletedSuccessfully && output.Result.ReachedEof
      && error.IsCompletedSuccessfully && error.Result.ReachedEof ? "confirmed" : "pipes-open";
  }

  public bool TryConfirm()
  {
    if (_confirmed) return true;
    try
    {
      _confirmed = _proofForTests is not null ? _proofForTests()
        : _execution is not null ? ConfirmExecution()
        : ConfirmProcess();
    }
    catch
    {
      // An exception is not proof of termination; the latch stays closed.
      _confirmed = false;
    }
    if (_confirmed) Evidence = "confirmed";
    return _confirmed;
  }

  private bool ConfirmProcess()
  {
    if (_process is null || _output is null || _error is null) return false;
    if (!_process.HasExited)
    {
      try
      {
        // Direct, non-tree termination: constant cost on the handle already held.
        _process.Kill();
        Evidence = "not-exited";
      }
      catch
      {
        Evidence = "kill-failed";
      }
      return false;
    }
    Evidence = PipeEvidence(_output, _error);
    if (Evidence != "confirmed") return false;
    _process.Dispose();
    _process = null;
    _readers?.Dispose();
    _readers = null;
    return true;
  }

  private bool ConfirmExecution()
  {
    if (!_execution!.IsCompletedSuccessfully) return false;
    var result = _execution.Result;
    if (result.TerminationConfirmed) return true;
    if (result.PendingCleanup is not { } inner) return false;
    var confirmed = inner.TryConfirm();
    Evidence = inner.Evidence;
    return confirmed;
  }

  private static void ObserveFault(Task task)
    => _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
      TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
```

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~UnconfirmedChildTests --logger "trx;LogFileName=task1-green.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 9 passed。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Configuration/UnconfirmedChild.cs Tests/SmartGuard.Configuration.Tests/UnconfirmedChildTests.cs
git commit -m "feat(config): prove unconfirmed child cleanup by exit and pipe EOF" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: 运行器以 EOF 判定清理并按需移交

**Files:**
- Modify: `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs`（`RunProcessAsync`、`RunProcessCoreAsync`、`ReadBoundedAsync`）
- Test: `Tests/SmartGuard.Configuration.Tests/PowerCfgProcessRunnerTests.cs`

- [ ] **Step 1: 写失败测试**

文件顶部改为：

```csharp
using System.Diagnostics;
using System.Text;
using FluentAssertions;
using SmartGuard.Configuration;
using Xunit.Abstractions;
```

类声明后加入构造函数：

```csharp
public class PowerCfgProcessRunnerTests
{
    private readonly ITestOutputHelper _output;

    public PowerCfgProcessRunnerTests(ITestOutputHelper output) => _output = output;
```

在现有 `RunProcess_deadline_covers_descendant_held_pipes_and_reports_uncertain_cleanup` 的 `watch.Elapsed...` 断言之后加入（保护性断言，RED 阶段即通过）：

```csharp
            result.PendingCleanup.Should().BeNull("a non-retaining caller keeps today's release behaviour");
```

类中加入以下用例与夹具：

```csharp
    [Fact]
    public async Task RunProcess_confirms_cleanup_when_held_pipes_reach_eof_inside_the_settle_window()
    {
        using var holder = new PipeHolder();
        using var cancellation = new CancellationTokenSource();
        // Released only after cancellation was classified and termination requested: at that
        // moment the descendant provably still holds the pipes (the 2026-11-19 shape).
        PowerCfgProcessRunner.AfterTerminationRequestForTests = pid =>
        {
            if (pid == holder.ParentPid) holder.Release();
        };
        try
        {
            var running = PowerCfgProcessRunner.RunProcessAsync("powershell.exe", holder.ParentArguments,
                TimeSpan.FromSeconds(30), cancellation.Token);
            await holder.WaitUntilHeldByDescendantAsync();
            cancellation.Cancel();
            var result = await running;
            result.FailureKind.Should().Be("cancelled");
            result.TerminationConfirmed.Should().BeTrue("both pipes reached EOF inside the settle window");
            result.PendingCleanup.Should().BeNull();
        }
        finally
        {
            PowerCfgProcessRunner.AfterTerminationRequestForTests = null;
        }
    }

    [Fact]
    public async Task RunProcess_hands_an_unproven_child_to_a_retaining_caller_until_its_pipes_reach_eof()
    {
        using var holder = new PipeHolder();
        var result = await RunHeldAsync(holder, retainUnconfirmed: true);
        result.FailureKind.Should().Be("cleanup-unconfirmed");
        result.TerminationConfirmed.Should().BeFalse();
        result.PendingCleanup.Should().NotBeNull();
        result.PendingCleanup!.TryConfirm().Should().BeFalse("the descendant still holds the pipes");
        result.PendingCleanup.Evidence.Should().Be("pipes-open");

        holder.Release();
        (await EventuallyAsync(result.PendingCleanup.TryConfirm, TimeSpan.FromSeconds(3)))
            .Should().BeTrue("the same pipes reached EOF after the holder exited");
        result.PendingCleanup.Evidence.Should().Be("confirmed");
    }

    [Fact]
    public async Task RunProcess_does_not_hand_over_children_to_a_non_retaining_caller()
    {
        using var holder = new PipeHolder();
        var result = await RunHeldAsync(holder, retainUnconfirmed: false);
        result.FailureKind.Should().Be("cleanup-unconfirmed");
        result.PendingCleanup.Should().BeNull();
    }

    [Fact]
    public async Task Confirming_a_held_child_is_constant_cost()
    {
        using var holder = new PipeHolder();
        var result = await RunHeldAsync(holder, retainUnconfirmed: true);
        result.PendingCleanup.Should().NotBeNull();
        var samples = new double[200];
        for (var i = 0; i < samples.Length; i++)
        {
            var watch = Stopwatch.StartNew();
            result.PendingCleanup!.TryConfirm().Should().BeFalse();
            samples[i] = watch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(samples);
        _output.WriteLine($"TryConfirm ms over {samples.Length} calls: p50={samples[99]:F3} p95={samples[189]:F3} max={samples[^1]:F3}");
        samples[^1].Should().BeLessThan(50, "confirmation must not wait or enumerate processes");
    }

    private static async Task<PowerCfgCommandResult> RunHeldAsync(PipeHolder holder, bool retainUnconfirmed)
    {
        using var cancellation = new CancellationTokenSource();
        var running = PowerCfgProcessRunner.RunProcessAsync("powershell.exe", holder.ParentArguments,
            TimeSpan.FromSeconds(30), cancellation.Token, retainUnconfirmed);
        await holder.WaitUntilHeldByDescendantAsync();
        cancellation.Cancel();
        return await running;
    }

    private static async Task<bool> EventuallyAsync(Func<bool> condition, TimeSpan limit)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < limit)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    // Parent powershell starts a descendant that inherits the redirected pipes, then exits normally.
    // The descendant announces readiness and holds the pipes until released.
    private sealed class PipeHolder : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"smartguard-pipe-holder-{Guid.NewGuid():N}");
        private readonly string _pids;
        private readonly string _ready;
        private readonly string _release;
        private int? _child;

        public PipeHolder()
        {
            Directory.CreateDirectory(_dir);
            _pids = Path.Combine(_dir, "pids");
            _ready = Path.Combine(_dir, "ready");
            _release = Path.Combine(_dir, "release");
            var child = Encode(
                $"[IO.File]::WriteAllText('{_ready}', 'ready'); " +
                $"while (-not (Test-Path -LiteralPath '{_release}')) {{ Start-Sleep -Milliseconds 10 }}");
            ParentArguments = "-NoProfile -EncodedCommand " + Encode(
                $"$p = Start-Process powershell.exe -ArgumentList '-NoProfile','-EncodedCommand','{child}' -NoNewWindow -PassThru; " +
                $"[IO.File]::WriteAllText('{_pids}', [string]$p.Id + ' ' + [string]$PID)");
        }

        public string ParentArguments { get; }

        public int? ParentPid { get; private set; }

        public async Task WaitUntilHeldByDescendantAsync()
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(20))
            {
                if (TryReadPids(out var child, out var parent))
                {
                    _child = child;
                    ParentPid = parent;
                    if (File.Exists(_ready) && HasExited(parent)) return;
                }
                await Task.Delay(20);
            }
            throw new TimeoutException("pipe holder fixture did not reach the held state");
        }

        public void Release()
        {
            try { File.WriteAllText(_release, "go"); }
            catch (IOException) { }
        }

        public void Dispose()
        {
            Release();
            if (_child is { } pid)
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (!process.WaitForExit(2000)) process.Kill(entireProcessTree: true);
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            try { Directory.Delete(_dir, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private bool TryReadPids(out int child, out int parent)
        {
            child = parent = 0;
            try
            {
                var parts = File.ReadAllText(_pids, Encoding.UTF8).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length == 2 && int.TryParse(parts[0], out child) && int.TryParse(parts[1], out parent);
            }
            catch (IOException) { return false; }
        }

        private static bool HasExited(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return process.HasExited;
            }
            catch (ArgumentException) { return true; }
        }
    }
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~PowerCfgProcessRunnerTests --logger "trx;LogFileName=task2-red.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 3 failed —— 竞态用例 `TerminationConfirmed` 为 false（旧代码预先快照判为孤儿）；移交用例与成本用例 `PendingCleanup` 为 null（`retainUnconfirmed` 尚未生效）。非保留用例与原有用例通过。

- [ ] **Step 3: 实现**

`RunProcessAsync` 改为传递参数：

```csharp
    => RunProcessCoreAsync(fileName, arguments, timeout, cancellationToken, retainUnconfirmed);
```

`RunProcessCoreAsync` 整体替换为：

```csharp
  private static async Task<PowerCfgCommandResult> RunProcessCoreAsync(
    string fileName,
    string arguments,
    TimeSpan timeout,
    CancellationToken cancellationToken,
    bool retainUnconfirmed)
  {
    var elapsed = Stopwatch.StartNew();
    PowerCfgCommandResult Result(bool started, string failure, bool confirmed = true, int? exitCode = null,
      string output = "", string error = "") => new(started, failure == "timed-out", exitCode, output, error)
      { FailureKind = failure, TerminationConfirmed = confirmed, ElapsedMilliseconds = elapsed.ElapsedMilliseconds };

    if (cancellationToken.IsCancellationRequested)
      return Result(false, "cancelled");

    using var timeoutCts = new CancellationTokenSource(timeout);
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
    var psi = new ProcessStartInfo(fileName, arguments)
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = ConsoleEncoding,
      StandardErrorEncoding = ConsoleEncoding,
      UseShellExecute = false,
      CreateNoWindow = true,
    };

    Process? proc;
    try
    {
      proc = Process.Start(psi);
    }
    catch (Exception)
    {
      return Result(false, "start-failed");
    }

    if (proc is null)
      return Result(false, "start-failed");

    // Readers are never cancelled by the business deadline: EOF observed after it is the cleanup
    // proof. Only this method's final decision (or a retaining owner) ends them.
    var readerCts = new CancellationTokenSource();
    var transferred = false;
    try
    {
      var failureSignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
      // Anonymous pipe implementations may perform synchronous work before their first await.
      // Capture before scheduling: a late worker must not access an already-disposed Process.
      // Reading the properties transfers stream ownership away from Process.Dispose.
      var standardOutput = proc.StandardOutput;
      var standardError = proc.StandardError;
      var readOutTask = Task.Run(() => ReadBoundedAsync(standardOutput, failureSignal, readerCts.Token));
      var readErrTask = Task.Run(() => ReadBoundedAsync(standardError, failureSignal, readerCts.Token));
      var exitTask = proc.WaitForExitAsync();
      var completion = Task.WhenAll(exitTask, readOutTask, readErrTask);
      string failure;

      try
      {
        // The same deadline covers EOF: a descendant can keep redirected pipes open after its parent exits.
        var winner = await Task.WhenAny(completion, failureSignal.Task).WaitAsync(linkedCts.Token).ConfigureAwait(false);
        if (winner == failureSignal.Task || failureSignal.Task.IsCompletedSuccessfully)
          failure = await failureSignal.Task.ConfigureAwait(false);
        else
        {
          await completion.ConfigureAwait(false);
          linkedCts.Token.ThrowIfCancellationRequested();
          var output = (await readOutTask.ConfigureAwait(false)).Text;
          var error = (await readErrTask.ConfigureAwait(false)).Text;
          return Result(true, "none", exitCode: proc.ExitCode, output: output, error: error);
        }
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        failure = "cancelled";
      }
      catch (OperationCanceledException)
      {
        failure = "timed-out";
      }
      catch (Exception)
      {
        failure = "io-failed";
      }

      var killSucceeded = TryKill(proc);
      AfterTerminationRequestForTests?.Invoke(proc.Id);
      // Settle window: readers keep running so that EOF can still be observed.
      await Task.WhenAny(completion, Task.Delay(250)).ConfigureAwait(false);
      // Observe every reader/exit failure even if an OS pipe cannot settle inside the cleanup budget.
      _ = completion.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
      // Judged after settling: an exited parent whose pipes reached EOF left no pipe-holding
      // descendant; open, cancelled or faulted readers are never proof.
      var evidence = !killSucceeded ? "kill-failed"
        : !proc.HasExited ? "not-exited"
        : UnconfirmedChild.PipeEvidence(readOutTask, readErrTask);
      if (evidence == "confirmed")
        return Result(true, failure);

      var unconfirmed = Result(true, failure, confirmed: false) with { FailureKind = "cleanup-unconfirmed" };
      if (!retainUnconfirmed)
      {
        readerCts.Cancel();
        return unconfirmed;
      }
      transferred = true;
      return unconfirmed with
      {
        PendingCleanup = UnconfirmedChild.FromProcess(proc, readOutTask, readErrTask, readerCts, evidence),
      };
    }
    finally
    {
      if (!transferred)
      {
        proc.Dispose();
        readerCts.Dispose();
      }
    }
  }
```

`ReadBoundedAsync` 整体替换为：

```csharp
  private static async Task<PipeReadOutcome> ReadBoundedAsync(StreamReader reader,
    TaskCompletionSource<string> failure, CancellationToken cancellationToken)
  {
    // The reader task exclusively owns disposal, including after the caller's cleanup
    // deadline. Disposing concurrently with a blocked synchronous pipe read can block
    // the guardian itself; late tasks retain ownership until their read unwinds.
    using var ownedReader = reader;
    const int limit = 65536;
    var output = new StringBuilder();
    var buffer = new char[2048];
    var overflowed = false;
    try
    {
      while (true)
      {
        var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        if (count == 0) return new(output.ToString(), ReachedEof: true);
        // Past the limit, keep draining without retaining so that EOF stays observable.
        if (overflowed) continue;
        if (count > limit - output.Length)
        {
          overflowed = true;
          failure.TrySetResult("output-limit");
          continue;
        }
        output.Append(buffer, 0, count);
      }
    }
    catch (OperationCanceledException) { throw; }
    catch
    {
      failure.TrySetResult("io-failed");
      throw;
    }
  }
```

（删除旧的 `orphanedPipes`、`settled` 变量与 `using var process = proc;`。）

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --logger "trx;LogFileName=task2-green.trx" --results-directory .work/process-cleanup-reopen
```

Expected: Configuration.Tests 全部通过，含 `RunProcess_limits_each_output_stream`（超限后丢弃读取至 EOF 仍 `TerminationConfirmed=true`）、`RunProcess_cancellation_is_bounded_and_confirmed`、句柄释放用例。`task2-green.trx` 的输出中含 `TryConfirm ms ... p50/p95/max`，抄入 Task 8 记录。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowerCfgProcessRunner.cs Tests/SmartGuard.Configuration.Tests/PowerCfgProcessRunnerTests.cs
git commit -m "fix(config): prove process cleanup by pipe EOF after settling and hand over to retaining owners" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 闩锁拒绝的独立失败类型

**Files:**
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs`（`ParseProbeResult`、`MapFailureKind`）
- Test: `Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
  [Fact]
  public async Task Cleanup_pending_refusal_is_not_reported_as_a_process_start_failure()
  {
    var refusal = new PowerCfgCommandResult(false, false, null, "", "") { FailureKind = "cleanup-pending" };
    PowerCfgExecutor.ParseProbeResult(refusal).FailureKind.Should().Be(PowerPlanFailureKind.ProcessCleanupPending);
    var apply = await PowerCfgExecutor.ApplyAndVerifyAsync(PowerPlanCatalogProvider.HighPerformancePlanGuid,
      (_, _) => Task.FromResult(refusal), (_, _) => Task.CompletedTask, CancellationToken.None);
    apply.FailureKind.Should().Be(PowerPlanFailureKind.ProcessCleanupPending);
  }
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter FullyQualifiedName~Cleanup_pending_refusal_is_not_reported_as_a_process_start_failure --logger "trx;LogFileName=task3-red.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 1 failed —— 实际为 `ProcessStartFailed`。

- [ ] **Step 3: 实现**

`ParseProbeResult` 开头：

```csharp
  internal static PowerPlanProbeResult ParseProbeResult(PowerCfgCommandResult command)
  {
    if (command.FailureKind == "cleanup-pending")
      return FailedProbe(PowerPlanFailureKind.ProcessCleanupPending, command.StandardError);
    if (!command.Started)
```

`MapFailureKind`：

```csharp
  private static PowerPlanFailureKind MapFailureKind(PowerCfgCommandResult command)
  {
    if (command.FailureKind == "cleanup-pending") return PowerPlanFailureKind.ProcessCleanupPending;
    if (!command.Started) return PowerPlanFailureKind.ProcessStartFailed;
    if (command.TimedOut) return PowerPlanFailureKind.TimedOut;
    return PowerPlanFailureKind.CommandFailed;
  }
```

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter FullyQualifiedName~GuardianProbeHardeningTests --logger "trx;LogFileName=task3-green.trx" --results-directory .work/process-cleanup-reopen
```

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs
git commit -m "fix(engine): report cleanup-pending refusals as their own failure kind" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 日志区分本次执行与持有对象

**Files:**
- Modify: `src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs`（`CreateNativeProbeEvent`）
- Test: `Tests/SmartGuard.Engine.Tests/NativeProbeLoggingTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
  [Fact]
  public void Unconfirmed_cleanup_names_the_failed_condition_and_promises_proven_recovery()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(
      new("plan-brightness-dc", "cleanup-unconfirmed", 1237, 2000, false, null, 0, false, "pipes-open", "pipes-open"));
    record.Properties!["cleanupEvidence"].GetString().Should().Be("pipes-open");
    record.Properties["pendingCleanupEvidence"].GetString().Should().Be("pipes-open");
    record.Message.Should().Contain("清理未确认（pipes-open）")
      .And.Contain("确认其退出后自动恢复")
      .And.NotContain("资格已关闭");
  }

  [Fact]
  public void Refusal_reports_this_execution_and_the_held_child_as_separate_facts()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(
      new("plan-read", "cleanup-pending", 0, 5000, true, null, 0, false, "not-started", "pipes-open"));
    record.Properties!["cleanupEvidence"].GetString().Should().Be("not-started");
    record.Properties["pendingCleanupEvidence"].GetString().Should().Be("pipes-open");
    record.Message.Should().Contain("上次子进程清理尚未确认（pipes-open），本次未启动")
      .And.NotContain("已退出").And.NotContain("confirmed");
  }

  [Fact]
  public void Open_latch_omits_the_held_child_property()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("plan-read", "timed-out", 5000, 5000, true, null, 0, false));
    record.Properties!["cleanupEvidence"].GetString().Should().Be("confirmed");
    record.Properties.ContainsKey("pendingCleanupEvidence").Should().BeFalse();
  }

  [Fact]
  public void Process_cleanup_recovery_reports_its_own_scope_and_closed_duration()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(
      new("process-cleanup", "none", 300000, 0, true, null, 0, true, "confirmed"));
    record.EventName.Should().Be(EventNameRegistry.NativeProbeRecovered);
    record.Properties!["recoveryScope"].GetString().Should().Be("process-cleanup");
    record.Message.Should().Contain("子进程清理确认").And.Contain("探测启动资格已恢复").And.Contain("300000ms");
  }
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter FullyQualifiedName~NativeProbeLoggingTests --logger "trx;LogFileName=task4-red.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 4 新用例失败（缺少 `cleanupEvidence` 属性、文案与范围不符）；原 3 个用例通过。

- [ ] **Step 3: 实现**

`CreateNativeProbeEvent` 整体替换为：

```csharp
  internal static LogEvent CreateNativeProbeEvent(WmiProbeObservation observation)
  {
    var label = observation.Operation switch
    {
      "brightness-read" => "亮度读取", "brightness-write" => "亮度写入", "brightness-restore" => "亮度恢复流程",
      "battery-read" => "电池补充探测", "plan-catalog" => "电源计划名称加载",
      "plan-read" => "当前电源计划读取", "plan-apply" => "电源计划切换命令执行",
      "plan-brightness-ac" => "插电亮度同步", "plan-brightness-dc" => "电池亮度同步",
      "process-cleanup" => "子进程清理确认",
      _ => "系统探测",
    };
    var degradation = observation.Operation switch
    {
      "brightness-read" => "亮度标记为不可用，继续守护",
      "brightness-write" => "亮度写入结果无法确认，不自动补发超时操作",
      "brightness-restore" => "停止本次亮度恢复，继续最终计划观测",
      "battery-read" => "保留原生供电状态，不依赖失败的WMI补充",
      "plan-catalog" => "使用已有计划标识和默认名称，继续守护",
      "plan-brightness-ac" or "plan-brightness-dc" => "停止本次可选亮度同步",
      "process-cleanup" => "暂停启动新的探测子进程",
      _ => "本次计划操作未确认，等待下一轮重新探测",
    };
    // cleanupEvidence describes this execution; pendingCleanupEvidence describes the child the latch holds.
    var evidence = observation.CleanupEvidence ?? (observation.TerminationConfirmed ? "confirmed" : "executor-pending");
    var cleanup = observation.FailureKind == "cleanup-pending"
      ? $"上次子进程清理尚未确认（{observation.PendingCleanupEvidence ?? "executor-pending"}），本次未启动"
      : observation.TerminationConfirmed ? "子进程已退出或未启动"
      : $"清理未确认（{evidence}），暂停启动新的探测子进程，确认其退出后自动恢复";
    var message = observation.Recovered
      ? observation.Operation == "process-cleanup"
        ? $"系统探测已恢复：{label}，探测启动资格已恢复；此前关闭 {observation.ElapsedMilliseconds}ms。"
        : $"系统探测已恢复：{label}。此前合并省略 {observation.SuppressedCount} 次同类异常。"
      : $"系统探测{(observation.FailureKind == "cancelled" ? "已取消" : "异常")}：{label}；{observation.FailureKind}；耗时 {observation.ElapsedMilliseconds}ms，预算 {observation.BudgetMilliseconds}ms；{degradation}；{cleanup}。合并省略 {observation.SuppressedCount} 次同类请求。";
    var recoveryScope = observation.Operation switch
    {
      "plan-apply" => "command-execution",
      "process-cleanup" => "process-cleanup",
      _ => "probe",
    };
    var properties = new Dictionary<string, JsonElement>
    {
      ["operation"] = JsonSerializer.SerializeToElement(observation.Operation),
      ["failureKind"] = JsonSerializer.SerializeToElement(observation.FailureKind),
      ["elapsedMs"] = JsonSerializer.SerializeToElement(observation.ElapsedMilliseconds),
      ["budgetMs"] = JsonSerializer.SerializeToElement(observation.BudgetMilliseconds),
      ["terminationConfirmed"] = JsonSerializer.SerializeToElement(observation.TerminationConfirmed),
      ["cleanupEvidence"] = JsonSerializer.SerializeToElement(evidence),
      ["exitCode"] = JsonSerializer.SerializeToElement(observation.ExitCode),
      ["suppressedCount"] = JsonSerializer.SerializeToElement(observation.SuppressedCount),
      ["recovered"] = JsonSerializer.SerializeToElement(observation.Recovered),
      ["recoveryScope"] = JsonSerializer.SerializeToElement(recoveryScope),
      ["actionResult"] = JsonSerializer.SerializeToElement(observation.Recovered ? "probe-recovered" : "unconfirmed"),
      ["degradation"] = JsonSerializer.SerializeToElement(degradation),
    };
    if (observation.PendingCleanupEvidence is { } pending)
      properties["pendingCleanupEvidence"] = JsonSerializer.SerializeToElement(pending);
    return EngineLogEvents.Create(observation.Recovered ? LogLevel.Info : LogLevel.Warn,
      observation.Recovered ? EventNameRegistry.NativeProbeRecovered : EventNameRegistry.NativeProbeFailed,
      message, OperationIdContext.Current, TraceIdContext.Current, properties: properties);
  }
```

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter FullyQualifiedName~NativeProbeLoggingTests --logger "trx;LogFileName=task4-green.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 7 passed。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs Tests/SmartGuard.Engine.Tests/NativeProbeLoggingTests.cs
git commit -m "feat(engine): log execution and held-child cleanup evidence separately" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Guardian 闩锁持有未证实子进程

**Files:**
- Modify: `src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs`
- Modify: `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs:489-490`
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs`（`RunPowerCfgAsync`、`SyncPlanBrightnessAsync`）
- Modify: `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs`（`TryLoadOnceAsync` 链路）
- Test: `Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
  [Fact]
  public async Task Proven_cleanup_reopens_plan_probing_and_records_recovery()
  {
    var certificate = new TaskCompletionSource<PowerCfgCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;
    var observations = new List<WmiProbeObservation>();
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) =>
    {
      calls++;
      return Task.FromResult(calls == 1
        ? new PowerCfgCommandResult(true, true, null, "", "")
          {
            FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false,
            PendingCleanup = UnconfirmedChild.FromExecution(certificate.Task),
          }
        : new PowerCfgCommandResult(true, false, 0,
          $"GUID: {PowerPlanCatalogProvider.HighPerformancePlanGuid:D} (high)", ""));
    };
    try
    {
      await WithRunner(async (runner, state, _) =>
      {
        state.PlanCatalog = new Dictionary<Guid, string>();
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
        calls.Should().Be(1, "the unproven child still blocks a sibling");
        var refusal = observations.Should().ContainSingle(x => x.Operation == "plan-read" && x.FailureKind == "cleanup-pending").Which;
        refusal.CleanupEvidence.Should().Be("not-started");
        refusal.PendingCleanupEvidence.Should().Be("executor-pending", "the held child is still unproven");

        certificate.SetResult(new PowerCfgCommandResult(true, false, 0, "", ""));
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
        calls.Should().Be(2, "proof of the same child reopens spawning");
        observations.Should().ContainSingle(x => x.Operation == "process-cleanup" && x.Recovered);
        observations.Should().Contain(x => x.Operation == "plan-read" && x.Recovered);
      }, useNativePlanReader: true, observe: observations.Add);
    }
    finally { ResetOverrides(); }
  }

  [Fact]
  public async Task Brightness_sync_budget_expiry_keeps_the_real_cleanup_classification()
  {
    var commands = new List<string>();
    var observations = new List<WmiProbeObservation>();
    PowerCfgProcessRunner.RunAsyncOverrideForTests = async (command, _, token) =>
    {
      commands.Add(command);
      try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
      catch (OperationCanceledException) { }
      return new PowerCfgCommandResult(true, false, null, "", "")
      {
        FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false,
        PendingCleanup = UnconfirmedChild.Unprovable("pipes-open"),
      };
    };
    try
    {
      await WithRunner(async (runner, state, _) =>
      {
        state.PlanCatalog = new Dictionary<Guid, string>();
        await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      }, paused: false, brightness: 50, observedPlan: PowerPlanCatalogProvider.BalancedPlanGuid,
        observe: observations.Add,
        apply: (target, _) => Task.FromResult(
          new PowerPlanApplyResult(false, target, null, PowerPlanFailureKind.CommandFailed, "")));
      commands.Should().ContainSingle().Which.Should().StartWith("/setacvalueindex");
      var sync = observations.Should().ContainSingle(x => x.Operation == "plan-brightness-ac").Which;
      sync.FailureKind.Should().Be("cleanup-unconfirmed", "budget expiry must not hide an unproven cleanup");
      sync.TerminationConfirmed.Should().BeFalse();
      sync.CleanupEvidence.Should().Be("pipes-open");
    }
    finally { ResetOverrides(); }
  }

  [Fact]
  public async Task Catalog_budget_expiry_keeps_the_real_cleanup_classification()
  {
    var observations = new List<WmiProbeObservation>();
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    PowerCfgProcessRunner.RunAsyncOverrideForTests = async (_, _, token) =>
    {
      try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
      catch (OperationCanceledException) { }
      return new PowerCfgCommandResult(true, false, null, "", "")
      {
        FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false,
        PendingCleanup = UnconfirmedChild.Unprovable("pipes-open"),
      };
    };
    try
    {
      await WithRunner((runner, state, _) => runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None),
        observe: observations.Add);
      var catalog = observations.Should().ContainSingle(x => x.Operation == "plan-catalog").Which;
      catalog.FailureKind.Should().Be("cleanup-unconfirmed", "budget expiry must not hide an unproven cleanup");
      catalog.CleanupEvidence.Should().Be("pipes-open");
    }
    finally { ResetOverrides(); }
  }
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~Proven_cleanup_reopens_plan_probing_and_records_recovery|FullyQualifiedName~Brightness_sync_budget_expiry_keeps_the_real_cleanup_classification|FullyQualifiedName~Catalog_budget_expiry_keeps_the_real_cleanup_classification" --logger "trx;LogFileName=task5-red.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 3 failed —— 重开用例第二轮为 `cleanup-unconfirmed`（旧伪造结果）且无 evidence；亮度与目录用例为 `timed-out`。

- [ ] **Step 3: 实现**

`GuardianIterationRunner.NativeProbes.cs`：

(a) `private bool _processExecutionClosed;` 替换为：

```csharp
  // Non-null while powercfg spawning is closed: the one child whose termination is unproven.
  // Only the main round touches it, so no lock is needed (PWR-INV-CLEANUP-REOPEN).
  private UnconfirmedChild? _pendingCleanup;
  private long _pendingCleanupSince;
```

(b) `RunObservedPowerCfgAsync` 中获取结果的三行替换为：

```csharp
    var result = TryReopenProcessExecution()
      ? await PowerCfgExecutor.RunPowerCfgAsync(arguments, token)
      : CleanupPendingRefusal();
```

(c) `ReportProcessResult` 整体替换为：

```csharp
  private void ReportProcessResult(string operation, PowerCfgCommandResult result, long budgetMs)
  {
    if (!result.TerminationConfirmed && _pendingCleanup is null)
    {
      // Spawning stays closed until this same child is proven gone; time alone never reopens it.
      _pendingCleanup = result.PendingCleanup ?? UnconfirmedChild.Unprovable();
      _pendingCleanupSince = (timeProvider ?? TimeProvider.System).GetTimestamp();
    }
    // Two objects, two facts: this execution's cleanup, and the child the latch still holds.
    var evidence = result.FailureKind == "cleanup-pending" ? "not-started"
      : result.TerminationConfirmed ? "confirmed"
      : result.PendingCleanup?.Evidence ?? "executor-pending";
    var pendingEvidence = _pendingCleanup?.Evidence;
    if (observeNativeProbe is null) return;
    var time = timeProvider ?? TimeProvider.System;
    var now = time.GetTimestamp();
    var hadFailure = _processFailures.TryGetValue(operation, out var previous);
    if (result.Succeeded)
    {
      if (!hadFailure) return;
      _processFailures.Remove(operation);
      SafeObserveNativeProbe(new(operation, "none", result.ElapsedMilliseconds, budgetMs,
        result.TerminationConfirmed, result.ExitCode, previous.Count, true, evidence, pendingEvidence));
      return;
    }
    var kind = result.FailureKind != "none" ? result.FailureKind
      : result.TimedOut ? "timed-out" : !result.Started ? "start-failed" : "command-failed";
    var count = hadFailure ? previous.Count + 1 : 1;
    var emit = !hadFailure || previous.Kind != kind || time.GetElapsedTime(previous.At, now) >= TimeSpan.FromMinutes(1);
    _processFailures[operation] = (kind, emit ? 0 : count, emit ? now : previous.At);
    if (emit) SafeObserveNativeProbe(new(operation, kind, result.ElapsedMilliseconds, budgetMs,
      result.TerminationConfirmed, result.ExitCode, count - 1, false, evidence, pendingEvidence));
  }

  private bool TryReopenProcessExecution()
  {
    if (_pendingCleanup is null) return true;
    if (!_pendingCleanup.TryConfirm()) return false;
    var closedFor = (timeProvider ?? TimeProvider.System).GetElapsedTime(_pendingCleanupSince);
    _pendingCleanup = null;
    SafeObserveNativeProbe(new("process-cleanup", "none", (long)closedFor.TotalMilliseconds, 0,
      true, null, 0, true, "confirmed"));
    return true;
  }

  // Nothing was started, so this execution leaves no child of its own to prove.
  private static PowerCfgCommandResult CleanupPendingRefusal()
    => new(false, false, null, "", "Previous child cleanup is not yet confirmed.") { FailureKind = "cleanup-pending" };
```

(d) `LoadPlanCatalogAsync`：

方法体最前面加入：

```csharp
    if (!TryReopenProcessExecution())
    {
      ReportProcessResult("plan-catalog", CleanupPendingRefusal(), 2000);
      return new Dictionary<Guid, string>();
    }
```

将

```csharp
        if (budget.IsCancellationRequested && !token.IsCancellationRequested)
          result = result with { TimedOut = true, FailureKind = "timed-out" };
```

替换为：

```csharp
        // Budget expiry is a timeout only when cleanup was proven; otherwise keep the real class.
        if (budget.IsCancellationRequested && !token.IsCancellationRequested && result.TerminationConfirmed)
          result = result with { TimedOut = true, FailureKind = "timed-out" };
```

两个 `catch (OperationCanceledException)` 分支的初始化器中，`ElapsedMilliseconds = (long)time.GetElapsedTime(started).TotalMilliseconds` 之后追加 `, PendingCleanup = last?.PendingCleanup`。

`GuardianIterationRunner.cs` 第 489–490 行：

```csharp
      else await PowerCfgExecutor.SyncPlanBrightnessAsync(target, brightnessBefore, cancellationToken,
        (operation, result) => ReportProcessResult(operation, result, 2000),
        () => canMutate() && TryReopenProcessExecution());
```

`PowerCfgExecutor.cs`：

```csharp
  // Guardian-only entry: the iteration runner's latch owns any child left unproven.
  internal static Task<PowerCfgCommandResult> RunPowerCfgAsync(
    string arguments,
    CancellationToken cancellationToken)
    => PowerCfgProcessRunner.RunPowerCfgAsync(arguments, cancellationToken, retainUnconfirmed: true);
```

`SyncPlanBrightnessAsync` 循环内：

```csharp
      var result = await PowerCfgProcessRunner.RunPowerCfgAsync(
        $"/set{mode}valueindex {planGuid:D} SUB_VIDEO VIDEONORMALLEVEL {brightness}", budget.Token,
        retainUnconfirmed: true);
      // Budget expiry is a timeout only when cleanup was proven; otherwise keep the real class.
      if (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested && result.TerminationConfirmed)
        result = result with { TimedOut = true, FailureKind = "timed-out" };
```

`PowerPlanCatalogProvider.cs`（只有 Guardian 的单次加载保留；Settings 路径默认 false，行为不变）：

```csharp
  internal static Task<IReadOnlyDictionary<Guid, string>> TryLoadOnceAsync(CancellationToken cancellationToken,
    Action<PowerCfgCommandResult>? observe = null)
    => TryLoadAsync(cancellationToken, observe, retainUnconfirmed: true);
```

`TryLoadAsync`、`LoadCoreAsync`、`EnrichWithHiddenSchemesAsync`、`TryQueryPlanNameAsync` 各增加末位参数 `bool retainUnconfirmed = false` 并逐层传递；`LoadCoreAsync` 的 `/list` 与 `TryQueryPlanNameAsync` 的 `/query` 调用改为：

```csharp
    var listResult = await PowerCfgProcessRunner.RunPowerCfgAsync(
        "/list",
        cancellationToken,
        TimeSpan.FromSeconds(15),
        retainUnconfirmed)
      .ConfigureAwait(false);
```

```csharp
      var result = await PowerCfgProcessRunner.RunPowerCfgAsync(
          $"/query {guid:D}",
          cancellationToken,
          TimeSpan.FromSeconds(15),
          retainUnconfirmed)
        .ConfigureAwait(false);
```

`TryLoadAsync` 内调用改为 `LoadCoreAsync(cancellationToken, observe, retainUnconfirmed)`；`LoadCoreAsync` 内两处 `EnrichWithHiddenSchemesAsync(catalog, cancellationToken, observe, retainUnconfirmed)`；`EnrichWithHiddenSchemesAsync` 内 `TryQueryPlanNameAsync(guid, cancellationToken, observe, retainUnconfirmed)`。

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --logger "trx;LogFileName=task5-green.trx" --results-directory .work/process-cleanup-reopen
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --logger "trx;LogFileName=task5-config.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 两个项目全部通过；`Unconfirmed_process_cleanup_blocks_further_process_creation_in_that_guardian`（无证明对象 → `Unprovable` → `calls == 1`）、`Catalog_unconfirmed_cleanup_prevents_the_remaining_hidden_queries`、`Persistent_plan_read_failure_does_not_flood_the_legacy_warning_path` 保持通过。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs
git commit -m "fix(engine): reopen powercfg probing once the unconfirmed child is proven gone" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: `WmiProbeProcess` 闩锁持有未证实子进程

**Files:**
- Modify: `src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs`
- Test: `Tests/SmartGuard.Engine.Tests/WmiProbeProcessTests.cs`

> 语义变更（设计已批准）：原用例 `Nonreturning_executor_is_bounded_and_late_result_cannot_reopen_boundary` 断言“晚到结果不能重开”。新不变量下，晚到的**业务回复**仍永不采用，执行器晚到返回的**清理证明**可以重开启动资格；用例按新不变量改写。

- [ ] **Step 1: 写失败测试**

(a) `Unconfirmed_cleanup_permanently_closes_spawning` 重命名为 `Unprovable_cleanup_keeps_spawning_closed`，方法体不变。

(b) `Nonreturning_executor_is_bounded_and_late_result_cannot_reopen_boundary` 替换为：

```csharp
  [Fact]
  public async Task Nonreturning_executor_late_reply_is_never_accepted_but_its_cleanup_certificate_reopens_spawning()
  {
    var clock = new Clock();
    var late = new TaskCompletionSource<PowerCfgCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var events = new List<WmiProbeObservation>();
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) =>
    {
      calls++;
      return calls == 1 ? late.Task : Task.FromResult(Reply("SGWMI1|brightness|44"));
    }, clock, events.Add);
    Assert.Equal(-1, await Task.Run(() => probe.ReadBrightness()).WaitAsync(TimeSpan.FromSeconds(5)));
    var failure = Assert.Single(events);
    Assert.Equal("cleanup-unconfirmed", failure.FailureKind);
    Assert.Equal("executor-pending", failure.CleanupEvidence);

    late.SetResult(Reply("SGWMI1|brightness|44"));
    Assert.Equal(-1, probe.ReadBrightness()); // late business reply discarded; operation still cooling down
    Assert.Equal(1, calls);
    Assert.Contains(events, e => e.Operation == "process-cleanup" && e.Recovered);

    clock.Advance(TimeSpan.FromSeconds(30));
    Assert.Equal(44, probe.ReadBrightness()); // a fresh execution, not the late reply
    Assert.Equal(2, calls);
  }
```

(c) 新增：

```csharp
  [Fact]
  public void Proven_cleanup_reopens_spawning_and_records_process_cleanup_recovery()
  {
    var events = new List<WmiProbeObservation>();
    var certificate = new TaskCompletionSource<PowerCfgCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) =>
    {
      calls++;
      return Task.FromResult(calls == 1
        ? new PowerCfgCommandResult(true, true, null, "", "")
          {
            FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false,
            PendingCleanup = UnconfirmedChild.FromExecution(certificate.Task),
          }
        : Reply("SGWMI1|battery|80|ac"));
    }, new Clock(), events.Add);
    Assert.Equal(-1, probe.ReadBrightness());
    Assert.Null(probe.ReadBattery()); // still unproven: no sibling may start
    Assert.Equal(1, calls);

    certificate.SetResult(Reply("ignored"));
    Assert.Equal((80, (bool?)true), probe.ReadBattery());
    Assert.Equal(2, calls);
    var recovery = Assert.Single(events, e => e.Operation == "process-cleanup");
    Assert.True(recovery.Recovered);
    Assert.Equal("confirmed", recovery.CleanupEvidence);
  }

  [Fact]
  public async Task Confirmation_is_single_flight_and_concurrent_calls_are_rejected_without_queueing()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var proofs = 0;
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) =>
    {
      calls++;
      return Task.FromResult(calls == 1
        ? new PowerCfgCommandResult(true, true, null, "", "")
          {
            FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false,
            PendingCleanup = UnconfirmedChild.ForTests(() =>
            {
              Interlocked.Increment(ref proofs);
              entered.Set();
              Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
              return true;
            }, "pipes-open"),
          }
        : Reply("SGWMI1|battery|80|ac"));
    });
    Assert.Equal(-1, probe.ReadBrightness());
    var confirming = Task.Run(() => probe.ReadBattery());
    Assert.True(entered.Wait(TimeSpan.FromSeconds(3)), "the first caller is inside confirmation");
    try
    {
      Assert.Null(await Task.Run(() => probe.ReadBattery()).WaitAsync(TimeSpan.FromSeconds(1)));
      Assert.Equal(1, Volatile.Read(ref proofs));
      Assert.Equal(1, calls);
    }
    finally { release.Set(); }
    Assert.Equal((80, (bool?)true), await confirming);
    Assert.Equal(1, Volatile.Read(ref proofs));
    Assert.Equal(2, calls);
  }
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter FullyQualifiedName~WmiProbeProcessTests --logger "trx;LogFileName=task6-red.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 3 failed —— `CleanupEvidence` 为 null、证明完成后仍拒绝（`calls` 停在 1）、单飞用例 `entered` 未触发（旧代码从不确认）。

- [ ] **Step 3: 实现**

(a) 字段：

```csharp
  private bool _busy;
  // Non-null while spawning is closed: the one child whose termination is unproven.
  // Reopened only by proving that same child gone (PWR-INV-CLEANUP-REOPEN).
  private UnconfirmedChild? _pending;
  private long _pendingSince;
  // The command vocabulary is fixed at three operations, so health state cannot
  // grow with input. Success only recovers that same operation's failure.
  private readonly Dictionary<string, FailureState> _failures = new(3);
  private sealed record FailureState(string Kind, long ReportedAt, long FailedAt, long Elapsed,
    bool Terminated, int? ExitCode, long Suppressed, string Evidence);
```

(b) `Run` 首行之后加入 `TryReopenSpawning();`；锁内条件 `_busy || _closed || ...` 改为 `_busy || _pending is not null || ...`；汇总观测改为：

```csharp
            summary = Observation(operation, failed.Kind, failed.Elapsed, failed.Terminated, failed.ExitCode, false,
              failed.Suppressed, failed.Evidence, _pending?.Evidence);
```

(c) `Run` 中执行与失败处理（从 `catch (TimeoutException)` 到失败判定）替换为：

```csharp
      catch (TimeoutException)
      {
        Fail(operation, "cleanup-unconfirmed", 2500, false, null, UnconfirmedChild.FromExecution(pending));
        return null;
      }
      catch (OperationCanceledException)
      {
        // Business cancellation discards the reply, but the same execution still
        // owns cleanup. A bounded certificate grace avoids disabling unrelated
        // probes while a correctly cancelling executor is finishing termination.
        PowerCfgCommandResult? cleanup = null;
        try
        {
          cleanup = pending.WaitAsync(TimeSpan.FromMilliseconds(CancellationCleanupGraceMilliseconds))
            .GetAwaiter().GetResult();
        }
        catch
        {
          // Not certified yet: the execution itself becomes the certificate that can later prove it.
        }
        Fail(operation, "cancelled", (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
          cleanup?.TerminationConfirmed == true, cleanup?.ExitCode,
          cleanup is null ? UnconfirmedChild.FromExecution(pending) : cleanup.PendingCleanup);
        return null;
      }
      if (token.IsCancellationRequested)
      {
        Fail(operation, "cancelled", result.ElapsedMilliseconds, result.TerminationConfirmed, result.ExitCode,
          result.PendingCleanup);
        return null;
      }
      if (!result.Succeeded || !result.TerminationConfirmed || result.FailureKind != "none")
      {
        Fail(operation, result.FailureKind == "none" ? SafeChildError(result) : result.FailureKind,
          result.ElapsedMilliseconds, result.TerminationConfirmed, result.ExitCode, result.PendingCleanup);
        return null;
      }
```

(d) 构造函数默认执行器改为保留：

```csharp
    _execute = execute ?? ((file, args, budget, token) =>
      PowerCfgProcessRunner.RunProcessAsync(file, args, budget, token, retainUnconfirmed: true));
```

(e) 新增方法：

```csharp
  private void TryReopenSpawning()
  {
    UnconfirmedChild? pending;
    lock (_sync)
    {
      if (_pending is null || _busy) return;
      pending = _pending;
      _busy = true; // single-flight: one confirmer, and no probe can start meanwhile
    }
    WmiProbeObservation? recovery = null;
    try
    {
      // Outside the ownership lock: confirmation may issue a direct process termination.
      if (!pending.TryConfirm()) return;
      lock (_sync)
      {
        _pending = null;
        recovery = new("process-cleanup", "none", (long)_time.GetElapsedTime(_pendingSince).TotalMilliseconds,
          0, true, null, 0, true, "confirmed");
      }
    }
    finally
    {
      lock (_sync) _busy = false;
      Publish(recovery);
    }
  }
```

(f) `ReportRestoreDegraded` 中 `terminated = !_closed && !_busy;` 改为 `terminated = _pending is null && !_busy;`。

(g) `Fail` 与 `Observation`：

```csharp
  private void Fail(string operation, string kind, long elapsed, bool terminated, int? exitCode,
    UnconfirmedChild? certificate = null)
  {
    var evidence = terminated ? "confirmed" : certificate?.Evidence ?? "executor-pending";
    WmiProbeObservation? failure = null;
    lock (_sync)
    {
      if (!terminated && _pending is null)
      {
        _pending = certificate ?? UnconfirmedChild.Unprovable();
        _pendingSince = _time.GetTimestamp();
      }
      var report = !_failures.TryGetValue(operation, out var previous) || previous.Kind != kind ||
        _time.GetElapsedTime(previous.ReportedAt) >= TimeSpan.FromSeconds(60);
      var suppressed = previous?.Suppressed ?? 0;
      if (!report) suppressed++; // Repeated actual failures are also coalesced observations.
      _failures[operation] = new(kind, report ? _time.GetTimestamp() : previous!.ReportedAt,
        _time.GetTimestamp(), elapsed, terminated, exitCode, report ? 0 : suppressed, evidence);
      if (report) failure = Observation(operation, kind, elapsed, terminated, exitCode, false, suppressed,
        evidence, _pending?.Evidence);
    }
    Publish(failure);
  }

  private WmiProbeObservation Observation(string operation, string kind, long elapsed, bool terminated, int? exitCode,
    bool recovered, long suppressed, string evidence = "confirmed", string? pendingEvidence = null)
    => new(operation, kind, elapsed, (long)Budget.TotalMilliseconds, terminated, exitCode, suppressed, recovered,
      evidence, pendingEvidence);
```

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --logger "trx;LogFileName=task6-green.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 全部通过；`Unprovable_cleanup_keeps_spawning_closed`、`Busy_probe_has_zero_queue_and_ignores_invalid_reply`、`Observer_does_not_hold_process_ownership_lock` 保持通过。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs Tests/SmartGuard.Engine.Tests/WmiProbeProcessTests.cs
git commit -m "fix(engine): reopen WMI probing once the unconfirmed child is proven gone" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: 架构契约与门禁

**Files:**
- Modify: `docs/ARCHITECTURE-CONTRACT.md:3,5`
- Test: `Tests/SmartGuard.Architecture.Tests/GuardianProbeIsolationArchitectureTests.cs`

- [ ] **Step 1: 写测试**

```csharp
  [Fact]
  public void Cleanup_latches_hold_the_unconfirmed_child_and_the_contract_registers_reopen()
  {
    foreach (var file in new[]
    {
      "src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs",
      "src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs",
    })
    {
      var source = SourceScanHelper.ReadSource(file);
      source.Should().Contain("UnconfirmedChild", "a closed latch must hold the same child it will later prove gone");
      source.Should().NotContain("_processExecutionClosed").And.NotContain("_closed",
        "a bare boolean latch can never be reopened by evidence");
    }
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md").Should().Contain("PWR-INV-CLEANUP-REOPEN");
  }

  [Fact]
  public void Only_registered_cleanup_owners_retain_unconfirmed_children()
  {
    var allowed = new[]
    {
      Path.Combine("src", "SmartGuard.Engine", "Infrastructure", "PowerCfgExecutor.cs"),
      Path.Combine("src", "SmartGuard.Engine", "Infrastructure", "WmiProbeProcess.cs"),
      Path.Combine("src", "SmartGuard.Configuration", "PowerPlanCatalogProvider.cs"),
    };
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      if (!File.ReadAllText(file).Contains("retainUnconfirmed: true", StringComparison.Ordinal)) continue;
      allowed.Should().Contain(path => file.EndsWith(path, StringComparison.OrdinalIgnoreCase),
        $"{file} retains unconfirmed children without a registered latch owner");
    }
  }
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --filter FullyQualifiedName~GuardianProbeIsolationArchitectureTests --logger "trx;LogFileName=task7-red.trx" --results-directory .work/process-cleanup-reopen
```

Expected: `Cleanup_latches_hold...` 失败（契约不含 `PWR-INV-CLEANUP-REOPEN`）；`Only_registered...` 为保护性门禁，Task 5/6 后即通过。

- [ ] **Step 3: 更新契约**

第 3 行，将 `按操作冷却30秒；清理未确认后关闭后续启动；` 替换为：

```text
按操作冷却30秒；清理未确认后关闭后续启动，并持有同一未证实子进程（`UnconfirmedChild`），仅在其已退出且 stdout/stderr 均读到 EOF 后重开并记录 `process-cleanup` 恢复；读取取消、异常或超限提前结束都不是证明，不以经过时间或其他操作成功推断（PWR-INV-CLEANUP-REOPEN）；清理判定在250ms收尾等待之后进行且收尾期间不取消读取；确认路径只做状态查询与一次直接终止，不枚举进程；
```

第 5 行段末追加：

```text
Runner 的 powercfg 启动资格（目录、计划读取、亮度同步、setactive）同样遵守 PWR-INV-CLEANUP-REOPEN；只有登记持有方（Guardian 的 powercfg 入口、单次目录加载与 `WmiProbeProcess`）以 `retainUnconfirmed: true` 保留未证实子进程，其他调用方由运行器立即释放、行为不变。闩锁拒绝记为 `cleanup-pending` / `ProcessCleanupPending`，不得显示为启动失败；`cleanupEvidence` 描述本次执行，`pendingCleanupEvidence` 描述持有对象。设计与根因：[子进程清理闩锁可证实重开](implementation-plans/2026-11-19-process-cleanup-reopen.md)。
```

- [ ] **Step 4: 运行确认 GREEN 与文档校验**

```powershell
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --logger "trx;LogFileName=task7-green.trx" --results-directory .work/process-cleanup-reopen
$b = [IO.File]::ReadAllBytes('docs/ARCHITECTURE-CONTRACT.md'); "bom=$($b[0] -eq 0xEF)"; $null = [Text.UTF8Encoding]::new($false,$true).GetString($b); 'utf8 ok'
git diff --check -- docs/ARCHITECTURE-CONTRACT.md
```

Expected: 架构测试全部通过；`bom=False`、`utf8 ok`；`git diff --check` 无输出。

- [ ] **Step 5: 提交**

```powershell
git add -- docs/ARCHITECTURE-CONTRACT.md Tests/SmartGuard.Architecture.Tests/GuardianProbeIsolationArchitectureTests.cs
git commit -m "docs(contract): register PWR-INV-CLEANUP-REOPEN with architecture gates" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: 全量验证、性能记录、安装与验收

**Files:**
- Modify: `docs/implementation-plans/2026-11-19-process-cleanup-reopen.md`（追加“实施与验证记录”）

- [ ] **Step 1: 全量测试**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1; $LASTEXITCODE
Get-ChildItem .work\test-results -Filter *.summary.json | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -Encoding utf8
```

Expected: 退出码 0；Pester、.NET、Tray Integration、Installer Integration 全部 Passed，记录各层通过/失败/跳过数与未运行原因。任何失败按 systematic-debugging 定位，不改测试迁就实现。

- [ ] **Step 2: 性能记录**

从 `task2-green.trx` 输出与全量运行中该用例的输出摘取 `TryConfirm` 的 p50/p95/max，写入记录。以 50ms 单次上限为本路径预算；任何一次全量运行超出即视为回退。

- [ ] **Step 3: 生成安装包**

```powershell
dotnet run --project src\SmartGuard.Packaging -- stage --root . --configuration Release
dotnet run --project src\SmartGuard.Packaging -- installer --root . --configuration Release
Get-ChildItem dist -Filter SmartGuard-Setup-*-x64.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { $_.FullName; (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
```

Expected: 两步退出 0；记录安装包路径与 SHA256。

- [ ] **Step 4: 安装（需用户确认 UAC）**

安装前备份 `D:\Apps\SmartGuard\bin` 与配置到 `.work/process-cleanup-reopen/original-install`，记录配置 SHA256；由用户运行安装包并确认 UAC。安装后核对 Engine 与 Configuration DLL 与候选一致、配置哈希未变、Engine/Tray 在交互会话运行。

- [ ] **Step 5: 真实用户路径验收**

- WPS 文字切到前台：`PowerPlanSwitched` 且“已确认生效”，`powercfg /getactivescheme` 为高性能。
- WPS 切到后台：出现 180 秒观察期事件；返回后恢复检测。
- 运行 5 分钟：无 `cleanup-pending`、无 `ProcessStartFailed`；`NativeProbeFailed` 若出现须带 `cleanupEvidence`。
- 自然触发“清理未确认 → 自动重开”难以现场复现：标记 `NOT RUN`，证据为 Task 2 真实进程用例与 Task 5/6 用例。

- [ ] **Step 6: 记录并提交**

在设计文档末尾追加“实施与验证记录”：各任务 RED/GREEN 证据文件、Task 0 基线比对、全量 summary 文件名与各层计数、`TryConfirm` 分布、安装包 SHA256、安装核对、真实路径结果、`NOT RUN` 项与原因。

```powershell
git add -- docs/implementation-plans/2026-11-19-process-cleanup-reopen.md
git commit -m "docs(engine): record process cleanup reopen verification" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## 自查记录

- 规格覆盖：3.1/3.4→Task 1；3.2/3.3→Task 0+2；3.5→Task 5/6；3.6→Task 2（非保留用例）+Task 5（目录链路）+Task 7（门禁）；3.7→Task 3/5；第 4 节→Task 4/5/6；第 6 节→Task 2 成本用例+Task 8；第 9 节→Task 7；第 10 节 1–10→Task 2/2/2/1/2/3+5/5/5/6/4+7；第 11 节审查项全部有对应任务。
- RED 合规：Task 0 为无行为脚手架并以全量通过证明；Task 1–7 的 RED 均可编译运行，因目标行为缺失而失败。
- 时序固定：竞态用例由 `AfterTerminationRequestForTests` 钩子在“取消已分类、终止已请求”之后释放后代，并以“后代就绪 + 父进程已退出”握手开始；单飞用例以 `ManualResetEventSlim` 屏障把确认阻塞在临界区内。
- 类型一致：`UnconfirmedChild.FromProcess/FromExecution/Unprovable/ForTests/TryConfirm/Evidence/PipeEvidence`、`PipeReadOutcome(Text, ReachedEof)`、`PowerCfgCommandResult.PendingCleanup`、`retainUnconfirmed`、`AfterTerminationRequestForTests`、`WmiProbeObservation.CleanupEvidence/PendingCleanupEvidence`、`PowerPlanFailureKind.ProcessCleanupPending`、`"cleanup-pending"`、`"not-started"`、`"process-cleanup"` 在各任务中拼写一致。
- 残余限制（已写入设计）：未继承重定向管道的后代不在证明范围；Settings 目录加载的单次调用内重试属范围外缺口，本次不改变其行为。
