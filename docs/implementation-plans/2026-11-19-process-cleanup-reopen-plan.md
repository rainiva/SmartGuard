# 子进程清理闩锁可证实重开 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**状态：** 待用户批准后实施。设计见 [2026-11-19-process-cleanup-reopen.md](2026-11-19-process-cleanup-reopen.md)。

**Goal:** 子进程清理未确认后，powercfg 与 WMI 探测的启动资格只在同一子进程被证实退出且两路管道结束后自动重开，不再因一次误判永久停工。

**Architecture:** 新增 `UnconfirmedChild` 作为未证实子进程的唯一所有权载体；`PowerCfgProcessRunner` 在 250ms 收尾等待之后判定清理并在未确认时移交 `Process`；`GuardianIterationRunner` 与 `WmiProbeProcess` 的布尔闩锁改为持有该对象，每次启动前非阻塞确认一次。闩锁拒绝使用 `cleanup-pending` / `ProcessCleanupPending`，日志新增 `cleanupEvidence` 与 `process-cleanup` 恢复事件。

**Tech Stack:** .NET 8、C#、xUnit、FluentAssertions、PowerShell（测试夹具子进程）。

**证据目录：** `.work/process-cleanup-reopen/`（每个 RED/GREEN 的 TRX 或构建输出存于此）。

**通用约束：** 所有源文件 UTF-8 无 BOM；用精确补丁编辑；每个任务只暂存本任务文件；提交信息使用 Conventional Commits 并以 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` 结尾；当前分支 `fix/logging-observability`。

---

## 文件结构

| 文件 | 动作 | 责任 |
|------|------|------|
| `src/SmartGuard.Configuration/UnconfirmedChild.cs` | 新建 | 持有一个终止未证实的子进程；`TryConfirm()` 非阻塞证明 |
| `src/SmartGuard.Configuration/PowerCfgCommandResult.cs` | 修改 | 新增 `PendingCleanup` |
| `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs` | 修改 | 收尾等待后判定清理；未确认时移交进程 |
| `src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs` | 修改 | 新增 `ProcessCleanupPending` |
| `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs` | 修改 | 映射 `cleanup-pending`；亮度同步不覆盖未确认分类 |
| `src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs` | 修改 | 观测记录新增 `CleanupEvidence`；闩锁改持有 `UnconfirmedChild` |
| `src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs` | 修改 | 日志文案、`cleanupEvidence`、`process-cleanup` 范围 |
| `src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs` | 修改 | 闩锁改持有 `UnconfirmedChild`，统一启动门控与恢复 |
| `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs` | 修改 | 亮度同步经同一门控 |
| `docs/ARCHITECTURE-CONTRACT.md` | 修改 | 登记 `PWR-INV-CLEANUP-REOPEN` |
| `Tests/SmartGuard.Configuration.Tests/UnconfirmedChildTests.cs` | 新建 | 证明对象单元测试 |
| `Tests/SmartGuard.Configuration.Tests/PowerCfgProcessRunnerTests.cs` | 修改 | 真实进程竞态与移交测试 |
| `Tests/SmartGuard.Engine.Tests/NativeProbeLoggingTests.cs` | 修改 | 日志格式测试 |
| `Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs` | 修改 | Runner 拒绝映射、重开、亮度分类测试 |
| `Tests/SmartGuard.Engine.Tests/WmiProbeProcessTests.cs` | 修改 | WMI 重开测试 |
| `Tests/SmartGuard.Architecture.Tests/GuardianProbeIsolationArchitectureTests.cs` | 修改 | 架构门禁 |

---

### Task 1: `UnconfirmedChild` 与 `PendingCleanup`

**Files:**
- Create: `src/SmartGuard.Configuration/UnconfirmedChild.cs`
- Modify: `src/SmartGuard.Configuration/PowerCfgCommandResult.cs:10-12`
- Test: `Tests/SmartGuard.Configuration.Tests/UnconfirmedChildTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `Tests/SmartGuard.Configuration.Tests/UnconfirmedChildTests.cs`：

```csharp
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class UnconfirmedChildTests
{
    [Fact]
    public void Unprovable_child_never_confirms_and_keeps_its_evidence()
    {
        var child = UnconfirmedChild.Unprovable("pipes-open");
        child.TryConfirm().Should().BeFalse();
        child.TryConfirm().Should().BeFalse();
        child.Evidence.Should().Be("pipes-open");
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
        child.TryConfirm().Should().BeTrue("a proven child stays proven");
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
    public void Results_have_no_pending_cleanup_by_default()
    {
        new PowerCfgCommandResult(true, false, 0, "", "").PendingCleanup.Should().BeNull();
    }
}
```

- [ ] **Step 2: 运行确认 RED**

```powershell
New-Item -ItemType Directory -Force .work\process-cleanup-reopen | Out-Null
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~UnconfirmedChildTests *> .work/process-cleanup-reopen/task1-red.txt; $LASTEXITCODE
```

Expected: 非 0；`task1-red.txt` 含 `CS0246`（`UnconfirmedChild` 未定义）。

- [ ] **Step 3: 最小实现**

新建 `src/SmartGuard.Configuration/UnconfirmedChild.cs`：

```csharp
using System.Diagnostics;

namespace SmartGuard.Configuration;

/// <summary>
/// Owns one child process whose termination has not been proven. A spawning owner holds it while
/// its start eligibility is closed and reopens only when <see cref="TryConfirm"/> proves that the
/// same child exited and both redirected pipes ended (PWR-INV-CLEANUP-REOPEN). Elapsed time or
/// another operation's success is never proof. Not thread-safe: the owning latch serializes calls.
/// </summary>
public sealed class UnconfirmedChild
{
  private Process? _process;
  private readonly Task? _pipes;
  private readonly Task<PowerCfgCommandResult>? _execution;
  private bool _confirmed;

  private UnconfirmedChild(Process? process, Task? pipes, Task<PowerCfgCommandResult>? execution, string evidence)
  {
    _process = process;
    _pipes = pipes;
    _execution = execution;
    Evidence = evidence;
  }

  /// <summary>Fixed vocabulary: kill-failed, not-exited, pipes-open, executor-pending, confirmed.</summary>
  public string Evidence { get; private set; }

  internal static UnconfirmedChild FromProcess(Process process, Task pipes, string evidence)
  {
    ObserveFault(pipes);
    return new(process, pipes, null, evidence);
  }

  internal static UnconfirmedChild FromExecution(Task<PowerCfgCommandResult> execution)
  {
    ObserveFault(execution);
    return new(null, null, execution, "executor-pending");
  }

  internal static UnconfirmedChild Unprovable(string evidence = "executor-pending") => new(null, null, null, evidence);

  /// <summary>Non-blocking. Issues at most one tree kill per call while the child is still running.</summary>
  public bool TryConfirm()
  {
    if (_confirmed) return true;
    try
    {
      _confirmed = _execution is not null ? ConfirmExecution() : ConfirmProcess();
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
    if (_process is null || _pipes is null) return false;
    if (!_process.HasExited)
    {
      try
      {
        _process.Kill(entireProcessTree: true);
        Evidence = "not-exited";
      }
      catch
      {
        Evidence = "kill-failed";
      }
      // Never wait here; a later check observes the exit.
      return false;
    }
    if (!_pipes.IsCompleted)
    {
      Evidence = "pipes-open";
      return false;
    }
    _process.Dispose();
    _process = null;
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

`src/SmartGuard.Configuration/PowerCfgCommandResult.cs` 在 `ElapsedMilliseconds` 之后加入：

```csharp
  public long ElapsedMilliseconds { get; init; } = 0;
  /// <summary>Non-null only when <see cref="TerminationConfirmed"/> is false and a child was handed over.</summary>
  public UnconfirmedChild? PendingCleanup { get; init; }
```

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~UnconfirmedChildTests --logger "trx;LogFileName=task1-green.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 5 passed, 0 failed。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Configuration/UnconfirmedChild.cs src/SmartGuard.Configuration/PowerCfgCommandResult.cs Tests/SmartGuard.Configuration.Tests/UnconfirmedChildTests.cs
git commit -m "feat(config): add unconfirmed child cleanup certificate" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: 运行器在收尾等待后判定清理并移交进程

**Files:**
- Modify: `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs:91-147`
- Test: `Tests/SmartGuard.Configuration.Tests/PowerCfgProcessRunnerTests.cs`

- [ ] **Step 1: 写失败测试（复现疑似竞态）**

在 `PowerCfgProcessRunnerTests` 文件顶部加入 `using System.Diagnostics;` 与 `using System.Text;`，并在类中加入：

```csharp
    [Fact]
    public async Task RunProcess_confirms_cleanup_when_held_pipes_close_inside_the_settle_window()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"smartguard-settle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var pidPath = Path.Combine(dir, "pids");
        var signalPath = Path.Combine(dir, "release");
        var child = Encode($"while (-not (Test-Path -LiteralPath '{signalPath}')) {{ Start-Sleep -Milliseconds 10 }}");
        var parent = Encode(
            $"$p = Start-Process powershell.exe -ArgumentList '-NoProfile','-EncodedCommand','{child}' -NoNewWindow -PassThru; " +
            $"[IO.File]::WriteAllText('{pidPath}', [string]$p.Id + ' ' + [string]$PID)");
        using var cancellation = new CancellationTokenSource();
        int? childPid = null;
        try
        {
            var running = PowerCfgProcessRunner.RunProcessAsync("powershell.exe",
                "-NoProfile -EncodedCommand " + parent, TimeSpan.FromSeconds(30), cancellation.Token);
            var ids = await WaitForPidsAsync(pidPath);
            childPid = ids.Child;
            await WaitForExitAsync(ids.Parent);
            // The parent exited normally while its descendant still holds the redirected pipes:
            // the state in which a pre-settle orphan snapshot misclassified the cleanup on 2026-11-19.
            File.WriteAllText(signalPath, "go");
            cancellation.Cancel();
            var result = await running;
            result.FailureKind.Should().Be("cancelled");
            result.TerminationConfirmed.Should().BeTrue("the held pipes closed inside the settle window");
            result.PendingCleanup.Should().BeNull();
        }
        finally
        {
            KillIfAlive(childPid);
            Directory.Delete(dir, true);
        }
    }

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static async Task<(int Child, int Parent)> WaitForPidsAsync(string path)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            try
            {
                var parts = File.ReadAllText(path, Encoding.UTF8).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2) return (int.Parse(parts[0]), int.Parse(parts[1]));
            }
            catch (IOException) { }
            await Task.Delay(20);
        }
        throw new TimeoutException("fixture did not publish its process ids");
    }

    private static async Task WaitForExitAsync(int pid)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited) return;
            }
            catch (ArgumentException) { return; }
            await Task.Delay(20);
        }
        throw new TimeoutException("fixture parent did not exit");
    }

    private static void KillIfAlive(int? pid)
    {
        if (pid is null) return;
        try
        {
            using var process = Process.GetProcessById(pid.Value);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(1000);
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }
```

在现有 `RunProcess_deadline_covers_descendant_held_pipes_and_reports_uncertain_cleanup` 的 `watch.Elapsed.Should()...` 之后加入移交与证明断言：

```csharp
            result.PendingCleanup.Should().NotBeNull();
            result.PendingCleanup!.TryConfirm().Should().BeFalse("the descendant still holds the pipes");
            result.PendingCleanup.Evidence.Should().Be("pipes-open");

            var heldBy = int.Parse(File.ReadAllText(pidPath, System.Text.Encoding.UTF8));
            using (var holder = System.Diagnostics.Process.GetProcessById(heldBy))
            {
                holder.Kill(entireProcessTree: true);
                holder.WaitForExit(1000).Should().BeTrue();
            }
            var proving = System.Diagnostics.Stopwatch.StartNew();
            while (!result.PendingCleanup.TryConfirm() && proving.Elapsed < TimeSpan.FromSeconds(2))
                await Task.Delay(20);
            result.PendingCleanup.TryConfirm().Should().BeTrue("the same child is now proven gone");
            result.PendingCleanup.Evidence.Should().Be("confirmed");
```

（该用例 `finally` 已对 `GetProcessById` 的 `ArgumentException` 容错，后代已被结束时不会失败。）

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerCfgProcessRunnerTests" --logger "trx;LogFileName=task2-red.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 2 failed —— 新用例 `TerminationConfirmed` 为 false（FailureKind `cleanup-unconfirmed`）；扩展用例 `PendingCleanup` 为 null。其余通过。

- [ ] **Step 3: 最小实现**

将 `PowerCfgProcessRunner.RunProcessCoreAsync` 中从 `if (proc is null)` 到方法结尾（当前第 91–147 行）替换为：

```csharp
    if (proc is null)
      return Result(false, "start-failed");

    // Disposed here unless an unconfirmed cleanup hands the process to UnconfirmedChild.
    var transferred = false;
    try
    {
      var failureSignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
      // Anonymous pipe implementations may perform synchronous work before their first await.
      // Capture before scheduling: a late worker must not access an already-disposed Process.
      // Reading the properties transfers stream ownership away from Process.Dispose.
      var standardOutput = proc.StandardOutput;
      var standardError = proc.StandardError;
      var readOutTask = Task.Run(() => ReadBoundedAsync(standardOutput, failureSignal, linkedCts.Token));
      var readErrTask = Task.Run(() => ReadBoundedAsync(standardError, failureSignal, linkedCts.Token));
      var exitTask = proc.WaitForExitAsync();
      var pipes = Task.WhenAll(readOutTask, readErrTask);
      var completion = Task.WhenAll(exitTask, pipes);
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
          var output = await readOutTask.ConfigureAwait(false);
          var error = await readErrTask.ConfigureAwait(false);
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
      linkedCts.Cancel();
      var settled = await Task.WhenAny(completion, Task.Delay(250)).ConfigureAwait(false) == completion;
      // Observe every reader/exit failure even if an OS pipe cannot settle inside the cleanup budget.
      _ = completion.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
      // Judge cleanup only after the settle window. Pipes that close inside it prove no descendant
      // kept them, even when the parent exited before the readers reached EOF. An exited parent
      // whose pipes stay open still cannot identify surviving descendants: that remains unconfirmed.
      var evidence = !killSucceeded ? "kill-failed" : !proc.HasExited ? "not-exited" : !settled ? "pipes-open" : "confirmed";
      if (evidence == "confirmed")
        return Result(true, failure);
      transferred = true;
      return Result(true, failure, confirmed: false) with
      {
        FailureKind = "cleanup-unconfirmed",
        PendingCleanup = UnconfirmedChild.FromProcess(proc, pipes, evidence),
      };
    }
    finally
    {
      if (!transferred) proc.Dispose();
    }
  }
```

（删除旧的 `using var process = proc;` 与 `orphanedPipes` 变量；`TryKill`、`ReadBoundedAsync`、`CreateConsoleEncoding` 不变。）

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerCfgProcessRunnerTests|FullyQualifiedName~UnconfirmedChildTests" --logger "trx;LogFileName=task2-green.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 全部通过（含句柄释放用例 `RunProcess_releases_redirected_pipe_handles_without_waiting_for_gc`，证明成功路径仍及时释放句柄）。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowerCfgProcessRunner.cs Tests/SmartGuard.Configuration.Tests/PowerCfgProcessRunnerTests.cs
git commit -m "fix(config): judge process cleanup after settling and hand over unproven children" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 闩锁拒绝的独立失败类型

**Files:**
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs:3-11`
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs:29-36,182-187`
- Test: `Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs`

- [ ] **Step 1: 写失败测试**

在 `GuardianProbeHardeningTests` 类中加入：

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
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter FullyQualifiedName~Cleanup_pending_refusal_is_not_reported_as_a_process_start_failure *> .work/process-cleanup-reopen/task3-red.txt; $LASTEXITCODE
```

Expected: 非 0；`CS0117`（`PowerPlanFailureKind` 不含 `ProcessCleanupPending`）。

- [ ] **Step 3: 最小实现**

`PowerPlanOperationResult.cs` 枚举末尾追加（追加而非插入，不改变已有成员值）：

```csharp
  VerificationMismatch,
  ProcessCleanupPending,
}
```

`PowerCfgExecutor.ParseProbeResult` 首行之前加入：

```csharp
  internal static PowerPlanProbeResult ParseProbeResult(PowerCfgCommandResult command)
  {
    if (command.FailureKind == "cleanup-pending")
      return FailedProbe(PowerPlanFailureKind.ProcessCleanupPending, command.StandardError);
    if (!command.Started)
```

`PowerCfgExecutor.MapFailureKind` 改为：

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

Expected: 全部通过。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Engine/Infrastructure/PowerPlanOperationResult.cs src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs
git commit -m "fix(engine): report cleanup-pending refusals as their own failure kind" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 观测记录与日志文案

**Files:**
- Modify: `src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs:5-7`
- Modify: `src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs:19-59`
- Test: `Tests/SmartGuard.Engine.Tests/NativeProbeLoggingTests.cs`

- [ ] **Step 1: 写失败测试**

在 `NativeProbeLoggingTests` 类中加入：

```csharp
  [Fact]
  public void Unconfirmed_cleanup_names_the_failed_condition_and_promises_proven_recovery()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(
      new("plan-brightness-dc", "cleanup-unconfirmed", 1237, 2000, false, null, 0, false, "pipes-open"));
    record.Properties!["cleanupEvidence"].GetString().Should().Be("pipes-open");
    record.Message.Should().Contain("清理未确认（pipes-open）")
      .And.Contain("确认其退出后自动恢复")
      .And.NotContain("资格已关闭");
  }

  [Fact]
  public void Cleanup_pending_refusal_says_nothing_was_started()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("plan-read", "cleanup-pending", 0, 5000, true, null, 0, false));
    record.Message.Should().Contain("等待上次子进程清理确认，本次未启动").And.NotContain("已退出");
    record.Properties!["cleanupEvidence"].GetString().Should().Be("confirmed");
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
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter FullyQualifiedName~NativeProbeLoggingTests *> .work/process-cleanup-reopen/task4-red.txt; $LASTEXITCODE
```

Expected: 非 0；`CS1729`（`WmiProbeObservation` 无 9 参数构造）。

- [ ] **Step 3: 最小实现**

`WmiProbeProcess.cs` 第 5–7 行记录改为（可选参数保持现有 8 参数构造可用）：

```csharp
internal sealed record WmiProbeObservation(string Operation, string FailureKind,
  long ElapsedMilliseconds, long BudgetMilliseconds, bool TerminationConfirmed,
  int? ExitCode, long SuppressedCount, bool Recovered, string? CleanupEvidence = null);
```

`GuardianLoop.NativeProbes.cs` 的 `CreateNativeProbeEvent` 整体替换为：

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
    var evidence = observation.CleanupEvidence ?? (observation.TerminationConfirmed ? "confirmed" : "executor-pending");
    var cleanup = observation.FailureKind == "cleanup-pending" ? "等待上次子进程清理确认，本次未启动"
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
    return EngineLogEvents.Create(observation.Recovered ? LogLevel.Info : LogLevel.Warn,
      observation.Recovered ? EventNameRegistry.NativeProbeRecovered : EventNameRegistry.NativeProbeFailed,
      message, OperationIdContext.Current, TraceIdContext.Current,
      properties: new Dictionary<string, JsonElement>
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
      });
  }
```

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~NativeProbeLoggingTests|FullyQualifiedName~WmiProbeProcessTests" --logger "trx;LogFileName=task4-green.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 全部通过（原 3 个日志用例保持通过）。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs Tests/SmartGuard.Engine.Tests/NativeProbeLoggingTests.cs
git commit -m "feat(engine): log cleanup evidence and process-cleanup recovery" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Runner 闩锁持有未证实子进程并统一门控

**Files:**
- Modify: `src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs:12,66-102,114-168`
- Modify: `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs:489-490`
- Modify: `src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs:158-159`
- Test: `Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs`

- [ ] **Step 1: 写失败测试**

在 `GuardianProbeHardeningTests` 类中加入：

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
        observations.Should().Contain(x => x.Operation == "plan-read" && x.FailureKind == "cleanup-pending",
          "a refusal is not a start failure");

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
  public async Task Brightness_sync_keeps_the_real_cleanup_classification_and_evidence()
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
      sync.FailureKind.Should().Be("cleanup-unconfirmed", "budget exhaustion must not hide an unproven cleanup");
      sync.TerminationConfirmed.Should().BeFalse();
      sync.CleanupEvidence.Should().Be("pipes-open");
    }
    finally { ResetOverrides(); }
  }
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~Proven_cleanup_reopens_plan_probing_and_records_recovery|FullyQualifiedName~Brightness_sync_keeps_the_real_cleanup_classification_and_evidence" --logger "trx;LogFileName=task5-red.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 2 failed —— 重开用例第二轮 FailureKind 为 `cleanup-unconfirmed` 而非 `cleanup-pending`；亮度用例 FailureKind 为 `timed-out`。

- [ ] **Step 3: 最小实现**

`GuardianIterationRunner.NativeProbes.cs`：

(a) 第 12 行 `private bool _processExecutionClosed;` 替换为：

```csharp
  // Non-null while powercfg spawning is closed: the one child whose termination is unproven.
  // Only the main round touches it, so no lock is needed (PWR-INV-CLEANUP-REOPEN).
  private UnconfirmedChild? _pendingCleanup;
  private long _pendingCleanupSince;
```

(b) `RunObservedPowerCfgAsync` 中结果获取改为：

```csharp
    var result = TryReopenProcessExecution()
      ? await PowerCfgExecutor.RunPowerCfgAsync(arguments, token)
      : CleanupPendingRefusal();
```

(c) `ReportProcessResult` 开头的 `_processExecutionClosed |= !result.TerminationConfirmed;` 替换为下列代码，并把方法内两处 `SafeObserveNativeProbe(new(...))` 的末尾各追加实参 `evidence`：

```csharp
    if (!result.TerminationConfirmed && _pendingCleanup is null)
    {
      // Spawning stays closed until this same child is proven gone; time alone never reopens it.
      _pendingCleanup = result.PendingCleanup ?? UnconfirmedChild.Unprovable();
      _pendingCleanupSince = (timeProvider ?? TimeProvider.System).GetTimestamp();
    }
    var evidence = result.TerminationConfirmed ? "confirmed" : result.PendingCleanup?.Evidence ?? "executor-pending";
```

即：

```csharp
      SafeObserveNativeProbe(new(operation, "none", result.ElapsedMilliseconds, budgetMs,
        result.TerminationConfirmed, result.ExitCode, previous.Count, true, evidence));
```

```csharp
    if (emit) SafeObserveNativeProbe(new(operation, kind, result.ElapsedMilliseconds, budgetMs,
      result.TerminationConfirmed, result.ExitCode, count - 1, false, evidence));
```

(d) 在 `ReportProcessResult` 之前加入两个方法：

```csharp
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

(e) `LoadPlanCatalogAsync` 方法体最前面加入门控：

```csharp
    if (!TryReopenProcessExecution())
    {
      ReportProcessResult("plan-catalog", CleanupPendingRefusal(), 2000);
      return new Dictionary<Guid, string>();
    }
```

并在两个 `catch (OperationCanceledException)` 分支的初始化器中，`ElapsedMilliseconds = ...` 之后追加 `PendingCleanup = last?.PendingCleanup`，使目录取消路径交出真实的未证实子进程。

`GuardianIterationRunner.cs` 第 489–490 行改为（所有 powercfg 启动走同一门控）：

```csharp
      else await PowerCfgExecutor.SyncPlanBrightnessAsync(target, brightnessBefore, cancellationToken,
        (operation, result) => ReportProcessResult(operation, result, 2000),
        () => canMutate() && TryReopenProcessExecution());
```

`PowerCfgExecutor.SyncPlanBrightnessAsync` 第 158–159 行改为：

```csharp
      // Budget exhaustion is a timeout only when cleanup was proven; otherwise keep the real class.
      if (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested && result.TerminationConfirmed)
        result = result with { TimedOut = true, FailureKind = "timed-out" };
```

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~GuardianProbeHardeningTests|FullyQualifiedName~NativeProbeLoggingTests" --logger "trx;LogFileName=task5-green.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 全部通过；原 `Unconfirmed_process_cleanup_blocks_further_process_creation_in_that_guardian`（无证明对象 → `Unprovable` → `calls == 1`）、`Catalog_unconfirmed_cleanup_prevents_the_remaining_hidden_queries`、`Persistent_plan_read_failure_does_not_flood_the_legacy_warning_path` 保持通过。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Engine/Worker/GuardianIterationRunner.NativeProbes.cs src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs src/SmartGuard.Engine/Infrastructure/PowerCfgExecutor.cs Tests/SmartGuard.Engine.Tests/GuardianProbeHardeningTests.cs
git commit -m "fix(engine): reopen powercfg probing once the unconfirmed child is proven gone" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: `WmiProbeProcess` 闩锁持有未证实子进程

**Files:**
- Modify: `src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs:18-23,49-149,170-195`
- Test: `Tests/SmartGuard.Engine.Tests/WmiProbeProcessTests.cs`

> 语义变更说明（设计已批准）：原用例 `Nonreturning_executor_is_bounded_and_late_result_cannot_reopen_boundary` 断言“晚到结果不能重开启动资格”。新设计下，晚到的**业务回复**仍永不采用，但执行器晚到返回的**清理证明**可以重开启动资格。该用例按新不变量改写，不是迁就实现。

- [ ] **Step 1: 写失败测试**

在 `WmiProbeProcessTests` 中：

(a) 将 `Unconfirmed_cleanup_permanently_closes_spawning` 重命名为 `Unprovable_cleanup_keeps_spawning_closed`，方法体不变。

(b) 将 `Nonreturning_executor_is_bounded_and_late_result_cannot_reopen_boundary` 整体替换为：

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
    var result = await Task.Run(() => probe.ReadBrightness()).WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(-1, result);
    var failure = Assert.Single(events);
    Assert.Equal("cleanup-unconfirmed", failure.FailureKind);
    Assert.Equal("executor-pending", failure.CleanupEvidence);

    late.SetResult(Reply("SGWMI1|brightness|44"));
    Assert.Equal(-1, probe.ReadBrightness()); // the late business reply is discarded; the operation is cooling down
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
    var clock = new Clock();
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
    }, clock, events.Add);
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
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter FullyQualifiedName~WmiProbeProcessTests --logger "trx;LogFileName=task6-red.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 2 failed —— 两个新用例在证明完成后仍被拒绝（`calls` 停在 1），且 `CleanupEvidence` 为 null。

- [ ] **Step 3: 最小实现**

`WmiProbeProcess.cs`：

(a) 字段（第 18–23 行）改为：

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

(b) `Run` 首行 `if (token.IsCancellationRequested) return null;` 之后加入 `TryReopenSpawning();`；锁内拒绝条件中的 `_closed` 改为 `_pending is not null`；汇总观测改为携带证据：

```csharp
            summary = Observation(operation, failed.Kind, failed.Elapsed, failed.Terminated, failed.ExitCode, false,
              failed.Suppressed, failed.Evidence);
```

(c) `Run` 中各 `Fail` 调用交出证明对象（删除这两处已被 `FromExecution` 覆盖的 `pending.ContinueWith` 行）：

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
          // Timeout or executor fault cannot certify termination yet; the execution itself
          // becomes the certificate that can later prove the child gone.
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

（末尾两个 `catch` 保持 `Fail(operation, ..., 0, false, null)`，无证明对象 → `Unprovable`。）

(d) 新增方法：

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
      // Outside the ownership lock: confirmation may issue a process kill.
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

(e) `ReportRestoreDegraded` 中 `terminated = !_closed && !_busy;` 改为 `terminated = _pending is null && !_busy;`。

(f) `Fail` 与 `Observation` 改为：

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
      if (report) failure = Observation(operation, kind, elapsed, terminated, exitCode, false, suppressed, evidence);
    }
    Publish(failure);
  }

  private WmiProbeObservation Observation(string operation, string kind, long elapsed, bool terminated, int? exitCode,
    bool recovered, long suppressed, string evidence = "confirmed")
    => new(operation, kind, elapsed, (long)Budget.TotalMilliseconds, terminated, exitCode, suppressed, recovered, evidence);
```

- [ ] **Step 4: 运行确认 GREEN**

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --filter "FullyQualifiedName~WmiProbeProcessTests|FullyQualifiedName~BrightnessServiceTests|FullyQualifiedName~FinalBrightnessObservationTests" --logger "trx;LogFileName=task6-green.trx" --results-directory .work/process-cleanup-reopen
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

- [ ] **Step 1: 写失败测试**

在 `GuardianProbeIsolationArchitectureTests` 中加入：

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
      source.Should().Contain("UnconfirmedChild",
        "a closed latch must hold the same child it will later prove gone");
      source.Should().NotContain("_processExecutionClosed").And.NotContain("_closed",
        "a bare boolean latch can never be reopened by evidence");
    }
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md").Should().Contain("PWR-INV-CLEANUP-REOPEN");
  }
```

- [ ] **Step 2: 运行确认 RED**

```powershell
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --filter FullyQualifiedName~GuardianProbeIsolationArchitectureTests --logger "trx;LogFileName=task7-red.trx" --results-directory .work/process-cleanup-reopen
```

Expected: 1 failed —— 契约不含 `PWR-INV-CLEANUP-REOPEN`（源码部分在 Task 5/6 后已满足）。

- [ ] **Step 3: 更新契约**

`docs/ARCHITECTURE-CONTRACT.md` 第 3 行，将 `按操作冷却30秒；清理未确认后关闭后续启动；` 替换为：

```text
按操作冷却30秒；清理未确认后关闭后续启动，并持有同一未证实子进程（`UnconfirmedChild`），仅在其被证实已退出且两路管道均结束后重开并记录 `process-cleanup` 恢复，不以经过时间或其他操作成功推断（PWR-INV-CLEANUP-REOPEN）；清理确认在250ms收尾等待之后判定；
```

第 5 行段末追加：

```text
Runner 的 powercfg 启动资格（目录、计划读取、亮度同步、setactive）同样遵守 PWR-INV-CLEANUP-REOPEN；闩锁拒绝记为 `cleanup-pending` / `ProcessCleanupPending`，不得显示为启动失败；日志 `cleanupEvidence` 记录 kill-failed/not-exited/pipes-open/executor-pending/confirmed。设计与根因：[子进程清理闩锁可证实重开](implementation-plans/2026-11-19-process-cleanup-reopen.md)。
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
git commit -m "docs(contract): register PWR-INV-CLEANUP-REOPEN with architecture gate" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: 全量验证、安装与验收记录

**Files:**
- Modify: `docs/implementation-plans/2026-11-19-process-cleanup-reopen.md`（追加“实施与验证记录”）

- [ ] **Step 1: 全量测试**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1; $LASTEXITCODE
Get-ChildItem .work\test-results -Filter *.summary.json | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -Encoding utf8
```

Expected: 退出码 0；summary JSON 中 Pester、.NET、Tray Integration、Installer Integration 全部 Passed，记录各层通过/失败/跳过数与未运行原因。任何失败先按 systematic-debugging 定位，不得改测试迁就实现。

- [ ] **Step 2: 生成安装包**

```powershell
dotnet run --project src\SmartGuard.Packaging -- stage --root . --configuration Release
dotnet run --project src\SmartGuard.Packaging -- installer --root . --configuration Release
Get-ChildItem dist -Filter SmartGuard-Setup-*-x64.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { $_.FullName; (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
```

Expected: 两步退出 0；记录安装包路径与 SHA256。

- [ ] **Step 3: 安装（需用户确认 UAC）**

安装前备份 `D:\Apps\SmartGuard\bin` 与配置到 `.work/process-cleanup-reopen/original-install`，记录配置 SHA256；由用户运行安装包并确认 UAC。安装后核对 Engine DLL 与候选一致、配置哈希未变、Engine/Tray 进程在交互会话运行。

- [ ] **Step 4: 真实用户路径验收**

- WPS 文字切到前台：日志出现 `PowerPlanSwitched` 且“已确认生效”，`powercfg /getactivescheme` 为高性能。
- WPS 切到后台：出现 180 秒观察期事件；返回后恢复检测。
- 运行 5 分钟：无 `cleanup-pending`、无 `ProcessStartFailed`，`NativeProbeFailed` 若出现须带 `cleanupEvidence`。
- 自然触发“清理未确认 → 自动重开”难以现场复现：标记 `NOT RUN`，证据为 Task 2 真实进程故障注入（`task2-green.trx`）与 Task 5/6 用例。

- [ ] **Step 5: 记录并提交**

在设计文档末尾追加“实施与验证记录”：各任务 RED/GREEN 证据文件、全量 summary 文件名与各层计数、安装包 SHA256、安装核对结果、真实路径结果及 `NOT RUN` 项与原因。然后：

```powershell
git add -- docs/implementation-plans/2026-11-19-process-cleanup-reopen.md
git commit -m "docs(engine): record process cleanup reopen verification" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## 自查记录

- 规格覆盖：3.1→Task 1；3.2→Task 1；3.3→Task 2；3.4→Task 5/6；3.5→Task 3；第 4 节→Task 4/5/6；第 8 节→Task 7；第 9 节 1–8→Task 2/2/3+5/5/5/6/4/7；第 10 节→Task 8。
- 规格第 9 节第 6 项“并发确认单飞”：未写并发时序测试（非确定性、易抖动）。单飞由 `TryReopenSpawning` 复用 `_busy` 保证，`Busy_probe_has_zero_queue_and_ignores_invalid_reply` 覆盖“忙则拒绝且不排队”，其余由代码审查确认。
- 类型一致：`UnconfirmedChild.FromProcess/FromExecution/Unprovable/TryConfirm/Evidence`、`PowerCfgCommandResult.PendingCleanup`、`WmiProbeObservation.CleanupEvidence`、`PowerPlanFailureKind.ProcessCleanupPending`、`"cleanup-pending"`、`"process-cleanup"` 在各任务中拼写一致。
