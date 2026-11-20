# 子进程清理闩锁可证实重开 — 第三阶段 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**状态：** 第 1 版草案，待外部审查与用户批准后实施。设计见 [2026-11-19-process-cleanup-reopen.md](2026-11-19-process-cleanup-reopen.md) 第 3 版（3v.1–3v.7），第 2 版正文中被第 3 版保留的小节一并适用。旧计划 [2026-11-19-process-cleanup-reopen-plan.md](2026-11-19-process-cleanup-reopen-plan.md) 已作废，本计划不复用其内容。

**基线：** 第二阶段完成后的 `fix/logging-observability`（`f410f960` 及之后）。已核实：
- `PowerCfgProcessRunner` 只剩 `RunProcess`/`RunProcessAsync`，生产调用方只有 `WmiProbeProcess`（全仓 grep）。设计 3.6 表因此只剩 `WmiProbeProcess` 一行。
- `WmiProbeObservation` 现有 9 个参数，第 9 个为 `int? Win32Error = null`。
- `GuardianLoop.CreateNativeProbeEvent` 含电源 API 分支（`schemeApi`）与 `win32Error` 属性。

**Goal:** WMI 子进程清理未确认后，`WmiProbeProcess` 不再永久停止探测：持有该子进程，在它被证实已退出且两路管道读到 EOF 时自动重开，并把“本次未启动”与“上次未证实”分别记录。

**Architecture:**
- **运行器（`PowerCfgProcessRunner`）：** 以 EOF 为清理证据，在 250ms 收尾等待之后判定。读取任务使用独立令牌，收尾期间不取消读取。删除预先快照的 `orphanedPipes`。调用方可以选择保留未证实的子进程。
- **`UnconfirmedChild`（新，Configuration）：** 唯一职责是持有一个终止尚未证实的子进程，并提供非阻塞、常数成本的证明。
- **`WmiProbeProcess`：** 裸布尔 `_closed` 改为持有至多一个 `UnconfirmedChild`。每次请求先在锁外尝试证明（`_busy` 单飞），通过则重开并记录 `process-cleanup`。未通过时，本次请求单独记录一条 `cleanup-pending`，不影响原操作的 30 秒冷却。

**Tech Stack:** .NET 8、C#、xUnit 2.5.3、FluentAssertions 8。证据目录 `.work/process-cleanup-phase3/`。

---

## 变更影响（相对设计第 7 节的第 3 版差异）

| 影响域 | 结论 |
|--------|------|
| Function | 亮度读取、亮度写入、电池补充探测在清理被证实后自动恢复；切换后的亮度恢复随之恢复 |
| Architecture | 新增 `UnconfirmedChild`；运行器新增 `retainUnconfirmed` 选择权（默认 false）；Owner 与入口不变 |
| Data | 无持久化变化；日志新增 `cleanupEvidence`、`pendingCleanupEvidence` 两个可选属性，以及 `cleanup-pending`、`process-cleanup` 两个取值 |
| Performance | 闩锁关闭期间每次 WMI 请求多一次 `TryConfirm`：已退出时为 `GetExitCodeProcess` 加两个任务状态读取，未退出时为一次 `TerminateProcess`；不枚举进程。以 Task 3 实测为准 |
| Resource | 全局至多保留 1 个进程句柄、2 个读取任务；无新 Timer 或队列。拒绝状态字典固定 3 项 |
| Concurrency & Time | 证明在锁外执行、`_busy` 单飞；确认进行中的其他请求立即拒绝，不排队；重开不依赖时间推断 |
| Reliability | 消除“一次误判永久停工”；真实挂死仍保持关闭；读取取消或异常后永久不可证实 |
| Observability | `cleanup-pending`（每个操作首次立即记录，之后每 60 秒合并）；`process-cleanup` 恢复记录含闩锁关闭时长 |
| Compatibility | 新属性默认 null；`retainUnconfirmed` 默认 false；`WmiProbeObservation` 只在末尾追加参数 |
| Security / Operations | 无变化 |

---

## 文件结构

| 文件 | 动作 | 责任 |
|------|------|------|
| `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs` | 修改 | `PipeReadOutcome`、独立读取令牌、EOF 判定、`retainUnconfirmed`、`AfterTerminationRequestForTests` |
| `src/SmartGuard.Configuration/PowerCfgCommandResult.cs` | 修改 | `CleanupEvidence`、`PendingCleanup` |
| `src/SmartGuard.Configuration/UnconfirmedChild.cs` | 新建 | 未证实子进程的唯一所有权载体与证明 |
| `src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs` | 修改 | `_closed` → `_pending`；重开；`cleanup-pending` 观测 |
| `src/SmartGuard.Engine/Worker/GuardianLoop.NativeProbes.cs` | 修改 | 非电源 API 分支的清理文案与两个属性；`process-cleanup` 标签与恢复范围 |
| `Tests/SmartGuard.Configuration.Tests/PowerCfgProcessRunnerTests.cs` | 修改 | 竞态、保留移交、证据取值 |
| `Tests/SmartGuard.Configuration.Tests/UnconfirmedChildTests.cs` | 新建 | 证明规则与成本 |
| `Tests/SmartGuard.Engine.Tests/WmiProbeProcessTests.cs`、`BrightnessServiceTests.cs`、`NativeProbeLoggingTests.cs` | 修改 | 重开、拒绝观测、单飞、用户可见后果、日志 |
| `Tests/SmartGuard.Architecture.Tests/ProcessCleanupReopenArchitectureTests.cs` | 新建 | 门禁 |
| `docs/ARCHITECTURE-CONTRACT.md`、设计文档 | 修改 | `PWR-INV-CLEANUP-REOPEN`、实施记录 |

**通用约束：**
- UTF-8 无 BOM；精确补丁；只暂存本任务文件；Conventional Commits，结尾 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。
- 每个 RED 前先 `dotnet build` 受影响测试项目并确认编译退出码为 0；RED 必须因行为缺口失败。
- 本计划不修改 `.ps1`。
- 受影响测试项目：Configuration、Engine、Architecture。
- 真实子进程夹具只启动 `powershell.exe`/`cmd.exe`，不碰电源设置。夹具自己负责结束它派生的后代，断言失败也要清理。

---

### Task 0: 基线

- [ ] **Step 1:**

```powershell
New-Item -ItemType Directory -Force .work\process-cleanup-phase3 | Out-Null
foreach ($p in 'Configuration','Engine','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task0-baseline-$p.trx" --results-directory .work/process-cleanup-phase3 }
```

Expected: 全部通过，记录各项目计数。

---

### Task 1: 脚手架（不改变行为）

**Files:** `PowerCfgCommandResult.cs`、`PowerCfgProcessRunner.cs`、新建 `UnconfirmedChild.cs`

- [ ] **Step 1: 结果类型**

`PowerCfgCommandResult` 中 `ElapsedMilliseconds` 之后加入：

```csharp
  // Cleanup proof of this execution: confirmed, kill-failed, not-exited, pipes-open, reader-failed.
  public string? CleanupEvidence { get; init; }

  // Set only when the caller asked to retain an unproven child; the caller then owns it.
  internal UnconfirmedChild? PendingCleanup { get; init; }
```

- [ ] **Step 2: `UnconfirmedChild` 骨架**

`src/SmartGuard.Configuration/UnconfirmedChild.cs`：

```csharp
using System.Diagnostics;

namespace SmartGuard.Configuration;

/// <summary>Scaffold only; the proof arrives with its tests.</summary>
internal sealed class UnconfirmedChild
{
  private UnconfirmedChild(string evidence) => Evidence = evidence;

  internal string Evidence { get; private set; }

  internal static UnconfirmedChild FromProcess(Process process, Task<PipeReadOutcome> stdout, Task<PipeReadOutcome> stderr,
    CancellationTokenSource readerCts, string evidence) => new(evidence);

  internal static UnconfirmedChild FromExecution(Task<PowerCfgCommandResult> execution) => new("executor-pending");

  internal static UnconfirmedChild Unprovable() => new("unprovable");

  internal static UnconfirmedChild ForTests(Func<(bool Confirmed, string Evidence)> proof, string evidence) => new(evidence);

  internal bool TryConfirm() => false;
}
```

- [ ] **Step 3: 运行器骨架**

`PowerCfgProcessRunner.cs`：
- 文件末尾（类外）加入 `internal sealed record PipeReadOutcome(string Text, bool ReachedEof);`。
- 类内加入测试钩子。它是 `AsyncLocal`，并行测试互不影响：

```csharp
  private static readonly AsyncLocal<Action?> AfterTerminationRequest = new();

  // Test-only: runs after the termination request and before the 250ms settle wait, to fix race timing.
  internal static void UseAfterTerminationRequestForTests(Action? hook) => AfterTerminationRequest.Value = hook;
```

- `RunProcessAsync` 增加参数 `bool retainUnconfirmed = false`，并传给 `RunProcessCoreAsync`。骨架阶段忽略它。
- `ReadBoundedAsync` 返回 `Task<PipeReadOutcome>`：`count == 0` 时返回 `new(output.ToString(), true)`，超限时返回 `new(output.ToString(), false)`。成功路径取 `.Text`。
- `var killSucceeded = TryKill(proc);` 之后加入 `AfterTerminationRequest.Value?.Invoke();`。
- 两处 `Result(...)` 返回补上 `CleanupEvidence`：成功为 `"confirmed"`；失败路径按现有判定，`confirmed` 为 true 时 `"confirmed"`，否则 `"pipes-open"`。现有判定（含 `orphanedPipes` 快照与 `linkedCts.Cancel()`）不变。

- [ ] **Step 4: 验证无行为变化**

```powershell
foreach ($p in 'Configuration','Engine','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task1-scaffold-$p.trx" --results-directory .work/process-cleanup-phase3 }
```

Expected: 计数与 Task 0 相同，全部通过。

- [ ] **Step 5: 提交** `refactor(config): scaffold cleanup evidence and unconfirmed child ownership`

---

### Task 2: 运行器以 EOF 判定清理

**Files:** `PowerCfgProcessRunner.cs`、`PowerCfgProcessRunnerTests.cs`

- [ ] **Step 1: 写失败测试**

在 `PowerCfgProcessRunnerTests` 中加入夹具与两例。夹具父进程派生一个继承管道的后代后正常退出；后代先写就绪文件，再等待释放文件（至多 20 秒）：

```csharp
    private sealed class DescendantFixture : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sg-descendant-{Guid.NewGuid():N}");
        internal DescendantFixture() => Directory.CreateDirectory(_dir);
        internal string PidPath => Path.Combine(_dir, "pid");
        internal string ReadyPath => Path.Combine(_dir, "ready");
        internal string ReleasePath => Path.Combine(_dir, "release");

        // The descendant inherits the redirected pipes (-NoNewWindow) and outlives its parent.
        internal string ParentArguments =>
            "-NoProfile -Command \"$p = Start-Process powershell.exe -NoNewWindow -PassThru -ArgumentList " +
            $"'-NoProfile -Command [IO.File]::WriteAllText(''{ReadyPath}'',''1''); $d=(Get-Date).AddSeconds(20); while(-not (Test-Path ''{ReleasePath}'') -and (Get-Date) -lt $d){{Start-Sleep -Milliseconds 20}}'; " +
            $"[IO.File]::WriteAllText('{PidPath}', [string]$p.Id)\"";

        internal bool WaitReady(TimeSpan timeout) => SpinWait.SpinUntil(() => File.Exists(ReadyPath) && File.Exists(PidPath), timeout);

        // Releases the descendant and waits until it has exited.
        internal void ReleaseAndWait()
        {
            File.WriteAllText(ReleasePath, "1");
            if (TryGetDescendant() is { } child) using (child) child.WaitForExit(5000);
        }

        internal void Kill()
        {
            if (TryGetDescendant() is not { } child) return;
            using (child) { try { child.Kill(entireProcessTree: true); child.WaitForExit(5000); } catch (InvalidOperationException) { } }
        }

        private System.Diagnostics.Process? TryGetDescendant()
        {
            try { return File.Exists(PidPath) ? System.Diagnostics.Process.GetProcessById(int.Parse(File.ReadAllText(PidPath))) : null; }
            catch (ArgumentException) { return null; }
        }

        public void Dispose()
        {
            Kill();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Cleanup_is_judged_after_the_settle_wait_by_eof_not_by_an_early_snapshot()
    {
        using var fixture = new DescendantFixture();
        using var stop = new CancellationTokenSource();
        PowerCfgProcessRunner.UseAfterTerminationRequestForTests(fixture.ReleaseAndWait);
        try
        {
            var run = PowerCfgProcessRunner.RunProcessAsync("powershell.exe", fixture.ParentArguments, TimeSpan.FromSeconds(15), stop.Token);
            fixture.WaitReady(TimeSpan.FromSeconds(10)).Should().BeTrue("the descendant holds the pipes before the cancel");
            stop.Cancel();
            var result = await run;
            result.FailureKind.Should().Be("cancelled");
            result.TerminationConfirmed.Should().BeTrue("the pipes reached EOF inside the settle wait");
            result.CleanupEvidence.Should().Be("confirmed");
        }
        finally { PowerCfgProcessRunner.UseAfterTerminationRequestForTests(null); }
    }

    [Fact]
    public async Task Held_pipes_after_parent_exit_are_reported_as_pipes_open()
    {
        using var fixture = new DescendantFixture();
        var result = await PowerCfgProcessRunner.RunProcessAsync("powershell.exe", fixture.ParentArguments, TimeSpan.FromSeconds(2));
        result.FailureKind.Should().Be("cleanup-unconfirmed");
        result.TerminationConfirmed.Should().BeFalse();
        result.CleanupEvidence.Should().Be("pipes-open");
        result.PendingCleanup.Should().BeNull("a caller that does not retain gets nothing to own");
    }
```

- [ ] **Step 2: RED**

```powershell
dotnet build Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore -v q; "build exit=$LASTEXITCODE"
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-build --filter "FullyQualifiedName~PowerCfgProcessRunnerTests" --logger "trx;LogFileName=task2-red.trx" --results-directory .work/process-cleanup-phase3
```

Expected: `Cleanup_is_judged_after…` 失败，`TerminationConfirmed` 为 false。原因是旧判定在终止请求与收尾等待之前就快照了 `orphanedPipes`（父进程已退出、读取未结束），而且在收尾等待前已取消读取。`Held_pipes…` 通过，是保护性用例。其余旧用例通过。

- [ ] **Step 3: 实现**

`ReadBoundedAsync` 替换为：

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
    var overLimit = false;
    try
    {
      while (true)
      {
        var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        if (count == 0) return new(output.ToString(), true);
        // Past the limit, keep reading and discarding with the fixed buffer so EOF stays observable.
        if (overLimit) continue;
        if (count > limit - output.Length)
        {
          overLimit = true;
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

`RunProcessCoreAsync` 中：
- 删除 `using var process = proc;`。
- 读取任务改用独立令牌：在 `failureSignal` 之前加入 `var readerCts = new CancellationTokenSource();`，两处 `ReadBoundedAsync(..., linkedCts.Token)` 改为 `readerCts.Token`。
- 成功分支改为：

```csharp
        var output = (await readOutTask.ConfigureAwait(false)).Text;
        var error = (await readErrTask.ConfigureAwait(false)).Text;
        var exitCode = proc.ExitCode;
        Release(proc, readerCts);
        return Result(true, "none", exitCode: exitCode, output: output, error: error) with { CleanupEvidence = "confirmed" };
```

- 从 `// Do not equate parent exit…` 到方法末尾替换为：

```csharp
    var killSucceeded = TryKill(proc);
    AfterTerminationRequest.Value?.Invoke();
    // Readers keep running through the settle wait: the proof is EOF on both pipes after the child exited.
    await Task.WhenAny(completion, Task.Delay(250)).ConfigureAwait(false);
    // Observe every reader/exit failure even if an OS pipe cannot settle inside the cleanup budget.
    _ = completion.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
      TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    var evidence = JudgeCleanup(killSucceeded, proc, readOutTask, readErrTask);
    if (evidence == "confirmed")
    {
      Release(proc, readerCts);
      return Result(true, failure) with { CleanupEvidence = evidence };
    }
    readerCts.Cancel();
    Release(proc, readerCts);
    return Result(true, "cleanup-unconfirmed", confirmed: false) with { CleanupEvidence = evidence };
  }

  // Proof = the child exited and both redirected pipes reached EOF (PWR-INV-CLEANUP-REOPEN). A cancelled
  // or faulted reader can never prove it.
  internal static string JudgeCleanup(bool killSucceeded, Process proc, Task<PipeReadOutcome> stdout, Task<PipeReadOutcome> stderr)
  {
    if (!killSucceeded) return "kill-failed";
    if (!proc.HasExited) return "not-exited";
    if (stdout.IsFaulted || stdout.IsCanceled || stderr.IsFaulted || stderr.IsCanceled) return "reader-failed";
    return stdout.IsCompletedSuccessfully && stderr.IsCompletedSuccessfully && stdout.Result.ReachedEof && stderr.Result.ReachedEof
      ? "confirmed" : "pipes-open";
  }

  private static void Release(Process proc, CancellationTokenSource readerCts)
  {
    proc.Dispose();
    readerCts.Dispose();
  }
```

- 启动失败两处返回前进程未创建，不需要释放。`Process.Start` 返回之后、读取任务创建之前没有其他退出路径。

- [ ] **Step 4: GREEN**

```powershell
foreach ($p in 'Configuration','Engine') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task2-green-$p.trx" --results-directory .work/process-cleanup-phase3 }
```

Expected: 全部通过。`RunProcess_limits_each_output_stream` 继续为 `output-limit` 且 `TerminationConfirmed=true`：超限后继续丢弃读取，终止后到达 EOF。`RunProcess_releases_redirected_pipe_handles_without_waiting_for_gc` 继续通过，证明成功路径仍释放句柄。

- [ ] **Step 5: 提交** `fix(config): judge child cleanup by pipe eof after the settle wait`

---

### Task 3: 保留移交与 `UnconfirmedChild` 证明

**Files:** `UnconfirmedChild.cs`、`PowerCfgProcessRunner.cs`、`PowerCfgProcessRunnerTests.cs`、新建 `UnconfirmedChildTests.cs`

- [ ] **Step 1: 写失败测试**

`PowerCfgProcessRunnerTests` 中加入：

```csharp
    [Fact]
    public async Task Retained_child_is_handed_over_and_proven_only_after_the_descendant_exits()
    {
        using var fixture = new DescendantFixture();
        var result = await PowerCfgProcessRunner.RunProcessAsync("powershell.exe", fixture.ParentArguments,
            TimeSpan.FromSeconds(2), retainUnconfirmed: true);
        result.FailureKind.Should().Be("cleanup-unconfirmed");
        var pending = result.PendingCleanup;
        pending.Should().NotBeNull("the retaining caller owns the unproven child");
        pending!.TryConfirm().Should().BeFalse();
        pending.Evidence.Should().Be("pipes-open");

        fixture.Kill();
        SpinWait.SpinUntil(() => pending.TryConfirm(), TimeSpan.FromSeconds(5)).Should().BeTrue();
        pending.Evidence.Should().Be("confirmed");
    }
```

`Tests/SmartGuard.Configuration.Tests/UnconfirmedChildTests.cs`：

```csharp
using System.Diagnostics;
using FluentAssertions;
using SmartGuard.Configuration;
using Xunit.Abstractions;

namespace SmartGuard.Configuration.Tests;

public class UnconfirmedChildTests(ITestOutputHelper output)
{
  private static Process ExitedChild()
  {
    var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true })!;
    process.WaitForExit(5000).Should().BeTrue();
    return process;
  }

  private static Task<PipeReadOutcome> Eof() => Task.FromResult(new PipeReadOutcome("", true));

  [Fact]
  public void Exited_child_with_both_pipes_at_eof_is_proven()
  {
    var child = UnconfirmedChild.FromProcess(ExitedChild(), Eof(), Eof(), new CancellationTokenSource(), "pipes-open");
    child.TryConfirm().Should().BeTrue();
    child.Evidence.Should().Be("confirmed");
  }

  [Fact]
  public void Reader_that_ended_without_eof_is_not_proof()
  {
    var child = UnconfirmedChild.FromProcess(ExitedChild(), Task.FromResult(new PipeReadOutcome("", false)), Eof(),
      new CancellationTokenSource(), "pipes-open");
    child.TryConfirm().Should().BeFalse();
    child.Evidence.Should().Be("pipes-open");
  }

  [Theory]
  [InlineData("cancelled")]
  [InlineData("faulted")]
  public void Cancelled_or_faulted_reader_is_never_provable(string how)
  {
    var reader = how == "cancelled"
      ? Task.FromCanceled<PipeReadOutcome>(new CancellationToken(true))
      : Task.FromException<PipeReadOutcome>(new IOException());
    var child = UnconfirmedChild.FromProcess(ExitedChild(), reader, Eof(), new CancellationTokenSource(), "pipes-open");
    child.TryConfirm().Should().BeFalse();
    child.TryConfirm().Should().BeFalse();
    child.Evidence.Should().Be("reader-failed");
  }

  [Fact]
  public void Execution_proof_follows_its_result_and_a_faulted_execution_is_unprovable()
  {
    var running = new TaskCompletionSource<PowerCfgCommandResult>();
    var held = UnconfirmedChild.FromExecution(running.Task);
    held.TryConfirm().Should().BeFalse();
    held.Evidence.Should().Be("executor-pending");
    var released = false;
    running.SetResult(new PowerCfgCommandResult(true, false, null, "", "")
    {
      FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false,
      PendingCleanup = UnconfirmedChild.ForTests(() => (released, released ? "confirmed" : "pipes-open"), "pipes-open"),
    });
    held.TryConfirm().Should().BeFalse();
    held.Evidence.Should().Be("pipes-open");
    released = true;
    held.TryConfirm().Should().BeTrue();

    UnconfirmedChild.FromExecution(Task.FromException<PowerCfgCommandResult>(new InvalidOperationException()))
      .TryConfirm().Should().BeFalse();
    UnconfirmedChild.Unprovable().TryConfirm().Should().BeFalse();
  }

  [Fact]
  public void Confirmation_cost_is_constant_on_the_exited_and_the_kill_paths()
  {
    // Exited path: the child exited but a reader never finished, so every call checks state only.
    var open = new TaskCompletionSource<PipeReadOutcome>();
    var exited = UnconfirmedChild.FromProcess(ExitedChild(), open.Task, Eof(), new CancellationTokenSource(), "pipes-open");
    var exitedCosts = Measure(200, _ => exited.TryConfirm().Should().BeFalse());

    // Kill path: each sample owns a fresh live child, so its first call performs one Process.Kill().
    var live = Enumerable.Range(0, 200).Select(_ => Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul")
      { UseShellExecute = false, CreateNoWindow = true })!).ToArray();
    try
    {
      var children = live.Select(p => UnconfirmedChild.FromProcess(p, open.Task, open.Task, new CancellationTokenSource(), "not-exited")).ToArray();
      var killCosts = Measure(200, i =>
      {
        children[i].TryConfirm().Should().BeFalse();
        children[i].Evidence.Should().Be("not-exited");
      });
      Report("exited", exitedCosts);
      Report("kill", killCosts);
      exitedCosts.Max().Should().BeLessThan(50);
      killCosts.Max().Should().BeLessThan(50);
    }
    finally
    {
      foreach (var p in live) { try { p.Kill(entireProcessTree: true); p.WaitForExit(5000); } catch (InvalidOperationException) { } p.Dispose(); }
    }
  }

  private static double[] Measure(int count, Action<int> call)
  {
    var costs = new double[count];
    for (var i = 0; i < count; i++)
    {
      var started = Stopwatch.GetTimestamp();
      call(i);
      costs[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
    return costs;
  }

  private void Report(string path, double[] costs)
  {
    var sorted = costs.Order().ToArray();
    output.WriteLine($"{path}: P50={sorted[sorted.Length / 2]:F3}ms P95={sorted[(int)(sorted.Length * 0.95)]:F3}ms max={sorted[^1]:F3}ms");
  }
}
```

`kill` 路径中，`ping` 子进程的 `cmd.exe` 父进程被 `Kill()`（非进程树）结束；`ping` 本身在 `finally` 中用进程树终止清理。被测的是 `TryConfirm` 单次调用耗时。

- [ ] **Step 2: RED**

```powershell
dotnet build Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore -v q; "build exit=$LASTEXITCODE"
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-build --filter "FullyQualifiedName~UnconfirmedChildTests|FullyQualifiedName~Retained_child_is_handed_over" --logger "trx;LogFileName=task3-red.trx" --results-directory .work/process-cleanup-phase3
```

Expected:
- `Retained_child…`：`PendingCleanup` 为 null（骨架忽略 `retainUnconfirmed`），失败。
- `Exited_child…`、`Execution_proof…`：骨架 `TryConfirm` 恒为 false，失败。
- `Cancelled_or_faulted…`、`Reader_that_ended…`：Evidence 停在构造时的 `pipes-open`，`Cancelled_or_faulted` 失败；`Reader_that_ended` 通过（保护性）。
- `Confirmation_cost…`：`kill` 路径断言 `TryConfirm` 后 Evidence 为 `not-exited`，骨架不变；构造时已是 `not-exited`，此例在骨架上通过，属成本门而非 RED。GREEN 后以真实实现重测并记录分布。

- [ ] **Step 3: 实现**

`UnconfirmedChild.cs` 整体替换为：

```csharp
using System.Diagnostics;

namespace SmartGuard.Configuration;

/// <summary>
/// Owns one child whose termination is not yet proven (PWR-INV-CLEANUP-REOPEN). Proof = the child exited
/// and both redirected pipes reached EOF. The proof never waits and never enumerates processes. Not
/// thread-safe: the holder serializes calls.
/// </summary>
internal sealed class UnconfirmedChild
{
  private Process? _process;
  private readonly Task<PipeReadOutcome>? _stdout;
  private readonly Task<PipeReadOutcome>? _stderr;
  private CancellationTokenSource? _readerCts;
  private readonly Task<PowerCfgCommandResult>? _execution;
  private readonly Func<(bool Confirmed, string Evidence)>? _proofForTests;
  private UnconfirmedChild? _delegate;
  private bool _confirmed;

  private UnconfirmedChild(string evidence, Process? process = null, Task<PipeReadOutcome>? stdout = null,
    Task<PipeReadOutcome>? stderr = null, CancellationTokenSource? readerCts = null,
    Task<PowerCfgCommandResult>? execution = null, Func<(bool, string)>? proofForTests = null)
  {
    Evidence = evidence;
    _process = process;
    _stdout = stdout;
    _stderr = stderr;
    _readerCts = readerCts;
    _execution = execution;
    _proofForTests = proofForTests;
  }

  /// <summary>kill-failed, not-exited, pipes-open, reader-failed, executor-pending, unprovable or confirmed.</summary>
  internal string Evidence { get; private set; }

  internal static UnconfirmedChild FromProcess(Process process, Task<PipeReadOutcome> stdout, Task<PipeReadOutcome> stderr,
    CancellationTokenSource readerCts, string evidence) => new(evidence, process, stdout, stderr, readerCts);

  // The executor did not return in time; its eventual result decides (a fault can never prove).
  internal static UnconfirmedChild FromExecution(Task<PowerCfgCommandResult> execution) => new("executor-pending", execution: execution);

  internal static UnconfirmedChild Unprovable() => new("unprovable");

  internal static UnconfirmedChild ForTests(Func<(bool Confirmed, string Evidence)> proof, string evidence)
    => new(evidence, proofForTests: proof);

  internal bool TryConfirm()
  {
    if (_confirmed) return true;
    try { _confirmed = Probe(); }
    catch { _confirmed = false; }
    if (!_confirmed) return false;
    Evidence = "confirmed";
    _process?.Dispose();
    _process = null;
    _readerCts?.Dispose();
    _readerCts = null;
    return true;
  }

  private bool Probe()
  {
    if (_proofForTests is { } proof)
    {
      var (confirmed, evidence) = proof();
      Evidence = evidence;
      return confirmed;
    }
    if (_execution is { } execution) return ProbeExecution(execution);
    if (_process is not { } process || _stdout is not { } stdout || _stderr is not { } stderr) return false;
    if (!process.HasExited)
    {
      // One TerminateProcess on the held handle, not a tree walk: constant cost.
      try { process.Kill(); Evidence = "not-exited"; }
      catch { Evidence = "kill-failed"; }
      return false;
    }
    if (stdout.IsFaulted || stdout.IsCanceled || stderr.IsFaulted || stderr.IsCanceled)
    {
      Evidence = "reader-failed";
      return false;
    }
    if (stdout.IsCompletedSuccessfully && stderr.IsCompletedSuccessfully && stdout.Result.ReachedEof && stderr.Result.ReachedEof)
      return true;
    Evidence = "pipes-open";
    return false;
  }

  private bool ProbeExecution(Task<PowerCfgCommandResult> execution)
  {
    if (_delegate is { } held)
    {
      var proven = held.TryConfirm();
      Evidence = held.Evidence;
      return proven;
    }
    if (!execution.IsCompleted) { Evidence = "executor-pending"; return false; }
    if (!execution.IsCompletedSuccessfully) { Evidence = "unprovable"; return false; }
    var result = execution.Result;
    if (result.TerminationConfirmed) return true;
    if (result.PendingCleanup is not { } child) { Evidence = "unprovable"; return false; }
    _delegate = child;
    var confirmed = child.TryConfirm();
    Evidence = child.Evidence;
    return confirmed;
  }
}
```

`PowerCfgProcessRunner.RunProcessCoreAsync` 末尾的未确认分支改为：

```csharp
    if (retainUnconfirmed)
      // The caller owns the child: the process stays undisposed and the readers keep running to EOF.
      return Result(true, "cleanup-unconfirmed", confirmed: false) with
      {
        CleanupEvidence = evidence,
        PendingCleanup = UnconfirmedChild.FromProcess(proc, readOutTask, readErrTask, readerCts, evidence),
      };
    readerCts.Cancel();
    Release(proc, readerCts);
    return Result(true, "cleanup-unconfirmed", confirmed: false) with { CleanupEvidence = evidence };
```

- [ ] **Step 4: GREEN 与成本记录**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --logger "trx;LogFileName=task3-green.trx" --logger "console;verbosity=detailed" --results-directory .work/process-cleanup-phase3 | Tee-Object .work/process-cleanup-phase3/task3-green.log
Select-String -Path .work/process-cleanup-phase3/task3-green.log -Pattern '^\s*(exited|kill): P50'
```

Expected: 全部通过；两条路径的 P50/P95/最大值写入证据，最大值都低于 50ms。不满足则不得声称确认路径为常数低成本，返回设计阶段。

- [ ] **Step 5: 提交** `feat(config): hand unproven children to a retaining caller with a constant-cost proof`

---

### Task 4: 观测字段与日志文案

**Files:** `WmiProbeProcess.cs`（只改 `WmiProbeObservation`）、`GuardianLoop.NativeProbes.cs`、`NativeProbeLoggingTests.cs`

- [ ] **Step 1: 观测字段（只追加）**

```csharp
internal sealed record WmiProbeObservation(string Operation, string FailureKind,
  long ElapsedMilliseconds, long BudgetMilliseconds, bool TerminationConfirmed,
  int? ExitCode, long SuppressedCount, bool Recovered, int? Win32Error = null,
  string? CleanupEvidence = null, string? PendingCleanupEvidence = null);
```

- [ ] **Step 2: 写失败测试**（追加到 `NativeProbeLoggingTests`）

```csharp
  [Fact]
  public void Unconfirmed_cleanup_names_the_evidence_of_this_execution()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("brightness-read", "cleanup-unconfirmed", 2250, 2000, false, null, 0, false,
      CleanupEvidence: "pipes-open"));
    record.Properties!["cleanupEvidence"].GetString().Should().Be("pipes-open");
    record.Properties.Should().NotContainKey("pendingCleanupEvidence");
    record.Message.Should().Contain("清理未确认（pipes-open）").And.Contain("确认其退出后自动恢复");
  }

  [Fact]
  public void Rejection_reports_not_started_and_the_held_child_separately()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("battery-read", "cleanup-pending", 0, 2000, true, null, 0, false,
      CleanupEvidence: "not-started", PendingCleanupEvidence: "not-exited"));
    record.Properties!["cleanupEvidence"].GetString().Should().Be("not-started");
    record.Properties["pendingCleanupEvidence"].GetString().Should().Be("not-exited");
    record.Message.Should().Contain("上次 WMI 子进程清理尚未确认（not-exited），本次未启动");
  }

  [Fact]
  public void Reopen_is_a_process_cleanup_recovery_with_the_closed_duration()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("process-cleanup", "none", 4200, 2000, true, null, 0, true,
      CleanupEvidence: "confirmed"));
    record.EventName.Should().Be(EventNameRegistry.NativeProbeRecovered);
    record.Properties!["recoveryScope"].GetString().Should().Be("process-cleanup");
    record.Properties["elapsedMs"].GetInt64().Should().Be(4200);
    record.Message.Should().Contain("子进程清理确认").And.Contain("恢复启动新的探测子进程");
  }
```

- [ ] **Step 3: RED**

```powershell
dotnet build Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore -v q; "build exit=$LASTEXITCODE"
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-build --filter "FullyQualifiedName~NativeProbeLoggingTests" --logger "trx;LogFileName=task4-red.trx" --results-directory .work/process-cleanup-phase3
```

Expected: 三个新用例失败（缺少属性与文案）；原有 7 例通过，其中 `Scheme_api_wait_failures_do_not_mention_child_processes`、`Scheme_api_error_reports_its_win32_code` 是回归门。

- [ ] **Step 4: 实现**（只改非电源 API 分支）

`CreateNativeProbeEvent` 中：
- `label` 加入 `"process-cleanup" => "子进程清理确认"`。
- `degradation` 加入 `"process-cleanup" => "恢复启动新的探测子进程"`。
- `cleanup` 的非电源 API 分支替换为：

```csharp
      : observation.FailureKind == "cleanup-pending"
        ? $"上次 WMI 子进程清理尚未确认（{observation.PendingCleanupEvidence ?? "unknown"}），本次未启动"
        : observation.TerminationConfirmed ? "子进程已退出或未启动"
        : $"清理未确认（{observation.CleanupEvidence ?? "unknown"}），暂停启动新的探测子进程，确认其退出后自动恢复";
```

- 恢复消息：

```csharp
    var message = observation.Recovered
      ? observation.Operation == "process-cleanup"
        ? $"系统探测已恢复：子进程清理确认，上次未证实的 WMI 子进程已退出，恢复启动新的探测子进程（关闭 {observation.ElapsedMilliseconds}ms）。"
        : $"系统探测已恢复：{label}。此前合并省略 {observation.SuppressedCount} 次同类异常。"
      : …（失败消息不变）;
```

- `recoveryScope`：`"plan-apply" => "command-execution"`，`"process-cleanup" => "process-cleanup"`，其他为 `"probe"`。
- 在 `win32Error` 之后加入：

```csharp
    if (observation.CleanupEvidence is { } cleanupEvidence)
      properties["cleanupEvidence"] = JsonSerializer.SerializeToElement(cleanupEvidence);
    if (observation.PendingCleanupEvidence is { } pendingEvidence)
      properties["pendingCleanupEvidence"] = JsonSerializer.SerializeToElement(pendingEvidence);
```

- [ ] **Step 5: GREEN**：Engine 全部通过。
- [ ] **Step 6: 提交** `feat(engine): log cleanup evidence of this execution and of the held child`

---

### Task 5: `WmiProbeProcess` 持有并重开

**Files:** `WmiProbeProcess.cs`、`WmiProbeProcessTests.cs`、`BrightnessServiceTests.cs`

- [ ] **Step 1: 写失败测试**（`WmiProbeProcessTests`，复用文件内的 `Clock` 与 `Reply`）

```csharp
  private static PowerCfgCommandResult Unconfirmed(UnconfirmedChild? child) =>
    new(true, true, null, "", "") { FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false,
      CleanupEvidence = "pipes-open", PendingCleanup = child };

  [Fact]
  public void Proven_cleanup_reopens_spawning_and_is_recorded_once()
  {
    var clock = new Clock();
    var events = new List<WmiProbeObservation>();
    var released = false;
    var calls = 0;
    var probe = new WmiProbeProcess((_, args, _, _) => Task.FromResult(++calls == 1
      ? Unconfirmed(UnconfirmedChild.ForTests(() => (released, released ? "confirmed" : "pipes-open"), "pipes-open"))
      : Reply("SGWMI1|battery|80|ac")), clock, events.Add);

    Assert.Equal(-1, probe.ReadBrightness());
    Assert.Null(probe.ReadBattery());
    Assert.Equal(1, calls);
    clock.Advance(TimeSpan.FromSeconds(5));
    released = true;
    Assert.NotNull(probe.ReadBattery());
    Assert.Equal(2, calls);
    var reopen = Assert.Single(events, e => e.Operation == "process-cleanup");
    Assert.True(reopen.Recovered);
    Assert.Equal(5000, reopen.ElapsedMilliseconds);
  }

  [Fact]
  public void Unprovable_cleanup_keeps_spawning_closed()
  {
    var clock = new Clock();
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) => { calls++; return Task.FromResult(Unconfirmed(null)); }, clock);
    Assert.Equal(-1, probe.ReadBrightness());
    clock.Advance(TimeSpan.FromDays(1));
    Assert.Null(probe.ReadBattery());
    probe.SetBrightness(50);
    Assert.Equal(1, calls);
  }

  [Fact]
  public void Rejection_is_its_own_observation_and_never_extends_the_failure_cooldown()
  {
    var clock = new Clock();
    var events = new List<WmiProbeObservation>();
    var released = false;
    var calls = 0;
    var probe = new WmiProbeProcess((_, args, _, _) => Task.FromResult(++calls == 1
      ? Unconfirmed(UnconfirmedChild.ForTests(() => (released, released ? "confirmed" : "not-exited"), "pipes-open"))
      : args.Contains("battery-read") ? Reply("SGWMI1|battery|80|ac") : Reply("SGWMI1|brightness|50")), clock, events.Add);

    Assert.Equal(-1, probe.ReadBrightness());                 // closes spawning
    Assert.Null(probe.ReadBattery());                         // first rejection: reported at once
    var first = Assert.Single(events, e => e.FailureKind == "cleanup-pending");
    Assert.Equal("battery-read", first.Operation);
    Assert.Equal("not-started", first.CleanupEvidence);
    Assert.Equal("not-exited", first.PendingCleanupEvidence);
    Assert.True(first.TerminationConfirmed, "this call started nothing");
    for (var i = 0; i < 5; i++) Assert.Null(probe.ReadBattery());
    Assert.Single(events, e => e.FailureKind == "cleanup-pending");   // coalesced inside 60 s
    clock.Advance(TimeSpan.FromSeconds(60));
    Assert.Null(probe.ReadBattery());
    Assert.Equal(5, events.Last(e => e.FailureKind == "cleanup-pending").SuppressedCount);

    released = true;
    Assert.NotNull(probe.ReadBattery());                      // battery has no failure of its own
    Assert.Equal(80, probe.ReadBattery()!.Value.Percent);
    Assert.Equal(50, probe.ReadBrightness());                 // its 30 s cooldown from the original failure has passed
  }

  [Fact]
  public async Task Confirmation_in_progress_rejects_concurrent_requests_without_a_second_proof()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var proofs = 0;
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) => Task.FromResult(++calls == 1
      ? Unconfirmed(UnconfirmedChild.ForTests(() =>
        {
          Interlocked.Increment(ref proofs);
          entered.Set();
          release.Wait(TimeSpan.FromSeconds(10));
          return (true, "confirmed");
        }, "pipes-open"))
      : Reply("SGWMI1|battery|80|ac")));
    Assert.Equal(-1, probe.ReadBrightness());
    var confirming = Task.Run(() => probe.ReadBattery());
    try
    {
      Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
      Assert.Null(await Task.Run(() => probe.ReadBattery()).WaitAsync(TimeSpan.FromSeconds(1)));
      Assert.Equal(1, Volatile.Read(ref proofs));
    }
    finally { release.Set(); }
    Assert.NotNull(await confirming);
    Assert.Equal(1, proofs);
  }

  [Fact]
  public async Task Nonreturning_executor_is_held_and_its_late_proof_reopens_spawning()
  {
    var late = new TaskCompletionSource<PowerCfgCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) => ++calls == 1 ? late.Task : Task.FromResult(Reply("SGWMI1|battery|80|ac")));
    Assert.Equal(-1, await Task.Run(() => probe.ReadBrightness()).WaitAsync(TimeSpan.FromSeconds(5)));
    Assert.Null(probe.ReadBattery());
    late.SetResult(Reply("SGWMI1|brightness|44"));            // the late result certifies its own cleanup
    Assert.NotNull(probe.ReadBattery());
    Assert.Equal(2, calls);
  }
```

同时调整两个旧用例的预期（它们锁定的是旧的永久闩锁）：
- `Unconfirmed_cleanup_permanently_closes_spawning` **删除**，由 `Unprovable_cleanup_keeps_spawning_closed` 取代（同一断言，执行器结果无可证明对象）。
- `Nonreturning_executor_is_bounded_and_late_result_cannot_reopen_boundary` **删除**，由 `Nonreturning_executor_is_held…` 取代：迟到结果自证清理时应重开（设计 3.4 `FromExecution`）。
- `Sustained_failure_reports_bounded_summary_and_retains_skipped_count` 改为：首次失败一条 `cleanup-unconfirmed`；第 1 次拒绝一条 `cleanup-pending`（合并数 0）；之后 99 次合并；60 秒后再一次拒绝产生第二条 `cleanup-pending`，合并数 99。拒绝不再计入该操作的失败摘要。

`BrightnessServiceTests` 加入用户可见后果用例：

```csharp
  [Fact]
  public void Brightness_reads_return_after_the_unproven_child_is_proven()
  {
    var clock = new FakeClock();   // 若文件内无时钟替身，按 WmiProbeProcessTests.Clock 写一个私有类
    var released = false;
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) => Task.FromResult(++calls == 1
      ? new PowerCfgCommandResult(true, true, null, "", "") { FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false,
          PendingCleanup = UnconfirmedChild.ForTests(() => (released, released ? "confirmed" : "pipes-open"), "pipes-open") }
      : new PowerCfgCommandResult(true, false, 0, "SGWMI1|brightness|63", "")), clock);
    var brightness = new BrightnessService(probe);

    Assert.Equal(-1, brightness.GetBrightness());
    clock.Advance(TimeSpan.FromSeconds(31));
    Assert.Equal(-1, brightness.GetBrightness());             // still unproven: no new child
    Assert.Equal(1, calls);
    released = true;
    Assert.Equal(63, brightness.GetBrightness());             // proven: a new child reads the real level
    Assert.Equal(2, calls);
  }
```

- [ ] **Step 2: RED**

```powershell
dotnet build Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore -v q; "build exit=$LASTEXITCODE"
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-build --filter "FullyQualifiedName~WmiProbeProcessTests|FullyQualifiedName~BrightnessServiceTests" --logger "trx;LogFileName=task5-red.trx" --results-directory .work/process-cleanup-phase3
```

Expected：`Proven_cleanup…`、`Rejection_is_its_own…`、`Confirmation_in_progress…`、`Nonreturning_executor_is_held…`、`Brightness_reads_return…` 与改写后的 `Sustained_failure…` 失败，原因都是 `_closed` 永久关闭（不重开、拒绝不单独记录）。`Unprovable_cleanup…` 通过（保护性）。

- [ ] **Step 3: 实现**

`WmiProbeProcess` 中：
- 字段：`private bool _busy, _closed;` 改为

```csharp
  private bool _busy;
  // At most one child whose cleanup is unproven; while held, no new child starts (PWR-INV-CLEANUP-REOPEN).
  private UnconfirmedChild? _pending;
  private long _pendingSince;
  // Rejections while a child is held are their own fact and never touch the failure cooldown in _failures.
  private readonly Dictionary<string, RejectionState> _rejections = new(3);
  private sealed record RejectionState(long ReportedAt, long Suppressed);
```

- 默认执行器：`_execute = execute ?? ((file, args, budget, token) => PowerCfgProcessRunner.RunProcessAsync(file, args, budget, token, retainUnconfirmed: true));`
- `Run` 开头 `if (token.IsCancellationRequested) return null;` 之后加入 `if (!TryReopen(operation)) return null;`，并从锁内条件删除 `|| _closed`。
- 加入：

```csharp
  // While a child is held, each request first tries the constant-cost proof outside the ownership lock.
  // _busy makes it single-flight: concurrent requests are rejected at once, never queued or re-proven.
  private bool TryReopen(string operation)
  {
    UnconfirmedChild pending;
    lock (_sync)
    {
      if (_pending is null) return true;
      if (_busy) return false;
      _busy = true;
      pending = _pending;
    }
    bool confirmed;
    try { confirmed = pending.TryConfirm(); }
    catch { confirmed = false; }
    WmiProbeObservation? observation;
    lock (_sync)
    {
      _busy = false;
      if (confirmed)
      {
        _pending = null;
        _rejections.Clear();
        observation = new("process-cleanup", "none", (long)_time.GetElapsedTime(_pendingSince).TotalMilliseconds,
          (long)Budget.TotalMilliseconds, true, null, 0, true, CleanupEvidence: "confirmed");
      }
      else observation = RecordRejection(operation, pending.Evidence);
    }
    Publish(observation);
    return confirmed;
  }

  // First rejection of each operation is reported at once, then at most every 60 seconds with the count
  // merged since the last record. This call started nothing, so it reports a confirmed termination.
  private WmiProbeObservation? RecordRejection(string operation, string pendingEvidence)
  {
    if (_rejections.TryGetValue(operation, out var previous) &&
        _time.GetElapsedTime(previous.ReportedAt) < TimeSpan.FromSeconds(60))
    {
      _rejections[operation] = previous with { Suppressed = previous.Suppressed + 1 };
      return null;
    }
    _rejections[operation] = new(_time.GetTimestamp(), 0);
    return new(operation, "cleanup-pending", 0, (long)Budget.TotalMilliseconds, true, null, previous?.Suppressed ?? 0, false,
      CleanupEvidence: "not-started", PendingCleanupEvidence: pendingEvidence);
  }
```

- `Fail` 签名改为 `Fail(string operation, string kind, long elapsed, bool terminated, int? exitCode, string? cleanupEvidence = null, UnconfirmedChild? pendingCleanup = null)`。`_closed |= !terminated;` 替换为：

```csharp
      if (!terminated && _pending is null)
      {
        _pending = pendingCleanup ?? UnconfirmedChild.Unprovable();
        _pendingSince = _time.GetTimestamp();
      }
```

  并把 `cleanupEvidence` 传入 `Observation(...)`（`Observation` 增加同名可选参数，赋给 `CleanupEvidence`）。
- 调用处：
  - 执行器未在兜底期限内返回：`Fail(operation, "cleanup-unconfirmed", 2500, false, null, "executor-pending", UnconfirmedChild.FromExecution(pending));`
  - 业务取消：宽限内拿到 `cleanup` 时 `Fail(operation, "cancelled", elapsed, cleanup.TerminationConfirmed, cleanup.ExitCode, cleanup.CleanupEvidence, cleanup.PendingCleanup)`；宽限超时或执行故障时 `Fail(operation, "cancelled", elapsed, false, null, "executor-pending", UnconfirmedChild.FromExecution(pending))`。
  - 结果后取消与失败结果：传入 `result.CleanupEvidence, result.PendingCleanup`。
  - 两处 `catch`（执行器抛出）：`Fail(..., false, null, "unprovable")`，持有 `Unprovable()`。
- `ReportRestoreDegraded`：`terminated = !_closed && !_busy;` 改为 `terminated = _pending is null && !_busy;`。

- [ ] **Step 4: GREEN**

```powershell
foreach ($p in 'Configuration','Engine') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task5-green-$p.trx" --results-directory .work/process-cleanup-phase3 }
```

Expected: 全部通过；`FinalBrightnessObservationTests`、`GuardianFirstRunInitializerTests` 等经 `WmiProbeProcess` 的用例不受影响。

- [ ] **Step 5: 提交** `fix(engine): reopen wmi probes once the unproven child is proven gone`

提交说明列明删除的 2 个旧用例与改写的 1 个用例及原因。

---

### Task 6: 契约与门禁

**Files:** `docs/ARCHITECTURE-CONTRACT.md`、新建 `Tests/SmartGuard.Architecture.Tests/ProcessCleanupReopenArchitectureTests.cs`、设计文档

- [ ] **Step 1: 门禁 RED**

```csharp
using System.Text;
using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ProcessCleanupReopenArchitectureTests
{
  [Fact]
  public void Wmi_probe_holds_an_unconfirmed_child_instead_of_a_bare_latch()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs");
    source.Should().NotContain("_closed").And.Contain("UnconfirmedChild? _pending");
  }

  [Fact]
  public void Only_the_wmi_probe_retains_unconfirmed_children()
  {
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;
      if (file.EndsWith("WmiProbeProcess.cs", StringComparison.Ordinal)) continue;
      File.ReadAllText(file, Encoding.UTF8).Should().NotContain("retainUnconfirmed: true", $"{file} is not a registered holder");
    }
  }

  [Fact]
  public void Contract_registers_the_cleanup_reopen_invariant()
  {
    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    contract.Should().Contain("PWR-INV-CLEANUP-REOPEN").And.Contain("UnconfirmedChild");
    contract.Should().NotContain("清理未确认后关闭后续启动");
  }
}
```

Expected: 第 1、2 例在 Task 5 后通过（保护性）；第 3 例失败。

- [ ] **Step 2: 契约**
  - `PWR-NATIVE-PROBE-ISOLATION` 中“清理未确认后关闭后续启动”改为“清理未确认时持有该子进程（`UnconfirmedChild`），证实其退出且两路管道读到 EOF 后自动重开（PWR-INV-CLEANUP-REOPEN）”。
  - 不变量表加入 `PWR-INV-CLEANUP-REOPEN`（阻断）：重开只依据同一子进程“已退出且两路管道读到 EOF”的证据，不以时间、读取取消或其他操作成功推断；只有 `WmiProbeProcess` 可以保留未证实子进程，至多一个；拒绝记为 `cleanup-pending`，不影响操作冷却；日志属性 `cleanupEvidence`/`pendingCleanupEvidence`。门禁：`ProcessCleanupReopenArchitectureTests`。
  - 设计文档第 3 版末尾追加“实施与验证记录”标题（内容在 Task 7 填写）。

- [ ] **Step 3: GREEN 与文档校验**（Architecture 全部通过；UTF-8 无 BOM；`git diff --check -- docs`）
- [ ] **Step 4: 提交** `docs(contract): register the cleanup reopen invariant`

---

### Task 7: 全量验证、安装与验收

- [ ] **Step 1: 全量测试**：`Run-Tests.ps1`，核对 summary JSON 四层计数。
- [ ] **Step 2: 安装包**：同第二阶段 Task 7 Step 2，记录 SHA256；备份安装目录与配置。
- [ ] **Step 3: 用户安装**：确认 UAC；核对 DLL 与候选一致、配置哈希未变。
- [ ] **Step 4: 真实路径验收（正常路径不回退）**
  - 运行至少 10 分钟：日志有亮度读数、无 `NativeProbeFailed`（WMI 操作）。
  - WPS 前台切换一次：切换后亮度恢复正常，日志无 `brightness-restore` 降级。
  - **NOT RUN：** 真实触发清理未确认需要故意持有管道的后代进程，只在 Task 2、3 的真实进程夹具中复现；重开与 `cleanup-pending` 由 Task 5 用例覆盖。
- [ ] **Step 5: 记录并提交**：设计文档“实施与验证记录”写入各任务 RED/GREEN、Task 3 成本分布、全量计数、安装哈希、验收结果与 `NOT RUN` 项。提交 `docs(engine): record process cleanup reopen verification`。

---

## 自查记录

- **设计覆盖：**
  - 3v.2 目标 1–2（不累积、持有并证实后重开）→ Task 3、5。
  - 目标 3（EOF 判定、收尾后判定、删除快照、收尾不取消读取）→ Task 2。
  - 目标 4（两个证据属性）→ Task 4。
  - 3v.4 契约与门禁 → Task 6；3v.5 测试 → Task 2–5；3v.6 成本与验收 → Task 3、7。
- **3v.7 约束：**
  1. 以第二阶段完成后的代码为基线，不复用旧计划 → 文首基线说明。
  2. `WmiProbeObservation` 只追加；`CreateNativeProbeEvent` 只改非电源 API 分支；两个回归用例保留 → Task 4。
  3. `cleanup-pending` 单独记录、首次立即、60 秒合并、不写 `FailureState.FailedAt`，重开后按原冷却规则 → Task 5 `Rejection_is_its_own…`。
  4. 两条确认路径各 200 次、最大值 < 50ms → Task 3 `Confirmation_cost…`。
- **RED 合规：** 新类型、参数与钩子先以骨架引入（Task 1），每个 RED 因行为缺口失败；`Confirmation_cost` 在骨架上即通过，明确标为成本门而非 RED。
- **类型一致：** `PipeReadOutcome(string Text, bool ReachedEof)`；`PowerCfgCommandResult.{CleanupEvidence, PendingCleanup}`；`UnconfirmedChild.{FromProcess, FromExecution, Unprovable, ForTests, TryConfirm, Evidence}`；`PowerCfgProcessRunner.{RunProcessAsync(…, retainUnconfirmed), UseAfterTerminationRequestForTests, JudgeCleanup}`；`WmiProbeObservation(…, Win32Error, CleanupEvidence, PendingCleanupEvidence)`；证据取值 `kill-failed`/`not-exited`/`pipes-open`/`reader-failed`/`executor-pending`/`unprovable`/`confirmed`/`not-started`。
- **相对设计的取舍：**
  - 设计 3.4 的证据取值未列 `unprovable`；本计划为“无证明材料”（执行器抛出、执行故障、结果无可证明对象）增加该取值，日志可区分“暂未证实”与“永远无法证实”。
  - `io-failed`（读取异常）此后一律判为 `reader-failed` 而不可证实，这是设计 3.1 的直接结果；旧行为在父进程退出且收尾完成时会判为已确认。
- **已知风险：**
  - 真实进程夹具依赖 PowerShell 启动速度；就绪握手、释放握手与 20 秒自退出保证不会悬挂，测试自己清理后代。
  - 成本用例启动 200 个短命 `cmd.exe`，耗时约数秒；只在 Configuration 测试中运行。
