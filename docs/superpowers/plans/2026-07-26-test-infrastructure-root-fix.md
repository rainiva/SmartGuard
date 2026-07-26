# SmartGuard 测试基础设施根治执行计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 根治 SmartGuard 全量门禁“子测试失败但总结果成功”、WPF UI 测试跨用例时序污染、托盘集成测试受高权限残留实例干扰三类问题，使一次红灯必然可解释、一次绿灯必然可信。

**Architecture:** 测试编排层以子进程真实退出码和 TRX 计数作为唯一真值，并生成结构化总结果；提权集成测试统一通过脚本文件、JSON 结果和任务快照完成 Capture → Stop → Test → Restore；WPF 测试继续共用单一 STA `Application`，但生产对象必须拥有可取消、可观察的异步生命周期，测试等待改为条件驱动并在每例结束时验证静默状态。

**Tech Stack:** PowerShell 5.1、Pester 5、.NET 8、xUnit、FluentAssertions、WPF Dispatcher、VSTest TRX、Windows UAC、计划任务。

## Global Constraints

- 严格遵守仓库 TDD：每个行为改动先增加 RED，贴出预期失败，再写最小 GREEN。
- 本计划阶段只写文档，不修改生产代码、测试代码或脚本实现。
- 不接受“失败后重跑一次通过”作为修复；重复运行只用于最终稳定性证明，任一轮失败即验收失败。
- 不允许通过增大固定超时、增加 `Thread.Sleep`、跳过测试、降低断言或捕获后忽略异常来变绿。
- `Run-Tests.ps1`、子 `dotnet test`、Pester、提权子进程四层结果必须逐层传递；任一层失败，父进程最终退出码必须非零。
- 提权流程只运行磁盘上的显式 `.ps1` 文件；禁止恢复内联 `-Command`，禁止仅依赖 `Start-Process -Wait`。
- 集成测试必须遵循 **Capture → Stop → Test → Restore**，恢复失败本身必须使门禁失败。
- 不新增第二套计划任务注册、引擎停止或安装根目录解析真源；继续使用 `ScheduledTaskRegistrar`、`EngineLifecycle`、`InstallRootResolver` 和既有 Restore helper。
- WPF 测试仍使用 `[Collection("WpfUiTests")]` 串行共享 STA；不得并行创建多个 `Application`。
- 中文文档、PowerShell 结果 JSON 和新增源文件统一 UTF-8；中文相关文件使用 UTF-8 无 BOM。
- 每个任务独立提交；只提交本任务文件，不纳入用户已有 `.work/`、`.superpowers/` 等未跟踪内容。
- 修改共享测试基础设施后必须更新 `docs/ARCHITECTURE-CONTRACT.md` 并接入 `Run-Tests.ps1` 门禁。

---

## 1. 已确认的根因与反证

### 1.1 子测试失败被总门禁掩盖

| 证据 | 结论 |
|---|---|
| `scripts/Invoke-SmartGuardDotNetTests.ps1` 通过 `cmd` 执行 `dotnet test ... & echo %ERRORLEVEL%` | `%ERRORLEVEL%` 在整条命令解析时提前展开，写入的可能是执行 `dotnet test` 之前的旧值 |
| 控制台明确出现 Settings `失败: 2`，`Run-Tests.ps1` 最终仍输出 `PASSED=79 FAILED=0 TOTAL=79` 并退出 0 | 当前总门禁是假绿，不能作为发布证据 |
| `Run-Tests.ps1` 最终计数只累加 Pester 主体、Tray integration、Installer integration | 最终 `PASSED` 数字没有包含 .NET 各测试项目，名称和语义误导 |

### 1.2 WPF UI 测试存在跨用例异步污染

| 证据 | 结论 |
|---|---|
| 第一轮 Settings 失败 1 项，第二轮失败变成另外 2 项 | 不是稳定的同一行为回归 |
| 三个失败项分别隔离复跑通过，完整 Settings 隔离运行 309/309 | 产品行为可以通过，失败与套件运行时状态相关 |
| `WpfApplicationFixture` 为整组测试复用一个 `Application` 和 Dispatcher | 窗口、计时器、异步延续和静态测试状态共享同一进程 |
| `WpfStaTestHost.Run` 清理只关闭窗口并执行 3 次 `Thread.Sleep(5)` + Dispatcher Background | 15ms 不能证明 `Task.Run`、Dispatcher continuation、timer 已结束 |
| `SettingsLogPageHost.StartRefreshFlight` 后台读取后无条件排队 Dispatcher continuation；`Dispose` 仅停 timer/搜索 coordinator | 已关闭窗口的 refresh 仍可能在下一测试中完成并触碰共享 Dispatcher |
| refresh 任务 fault 时只检查 `task.IsFaulted` 后返回 | 真正异常被吞掉，测试只看到“正在加载”直到超时 |
| `WaitUntilLogContent` 使用 `Thread.Sleep(1)` 和嵌套 `DispatcherFrame`，只等待状态文本不等于 Loading | 等待条件过宽，无法区分成功、失败、旧状态和跨用例回调 |

### 1.3 托盘集成测试对机器状态有隐式依赖

| 证据 | 结论 |
|---|---|
| 机器存在高权限 Engine/Tray 残留实例时，普通权限 `Stop-Process` 被拒绝，但 helper 使用 `SilentlyContinue` | 测试继续执行，却没有真正建立“无现有实例”的前置条件 |
| 残留 Engine 持有 `Global\SmartGuard.Core`；临时测试 Engine 正常返回 0 但未成为 owner | 测试等待 25 秒仍得不到 `SmartGuard.status.json` |
| 精确终止残留 PID 后，相同 Tray 集成测试立即 2/2 通过 | 失败来自未验证的环境清理，不是托盘读状态功能回归 |
| 失败输出只说 `$payload` 为空 | 当前诊断信息没有暴露进程、权限、mutex 或启动进程退出状态 |

---

## 2. 修复边界

### 2.1 本计划必须修复

1. .NET 子测试退出码、TRX 计数和父门禁结果一致。
2. Pester discovery/container failure、TRX 缺失或损坏、提权 JSON 缺失均 fail closed。
3. WPF 日志刷新任务在 Dispose 后不能再更新 UI；异常必须可观察。
4. WPF 测试等待精确业务条件，不使用固定睡眠碰运气。
5. 每个 WPF 用例结束后窗口、refresh flight、timer、静态测试开关回到基线。
6. Tray integration 在停止现有实例失败时立即给出明确错误，不能继续等空状态文件。
7. Tray integration 在完整门禁中以受控提权运行，并恢复运行前的计划任务/进程基线。
8. 完整门禁输出机器可解析的结构化汇总，并用真实退出码决定成功/失败。

### 2.2 明确不做

- 不修改三个已修复的产品功能：电量解析、电源事件、日志滚动位置。
- 不通过放宽 UI 断言、延长所有测试到 30/60 秒来掩盖竞态。
- 不把 WPF 测试拆成 309 个独立进程；成本高且不能修复生产对象生命周期。
- 不改变 SmartGuard 正常运行时的全局单实例语义。
- 不为测试新增第二套电源计划、计划任务或安装根目录实现。
- 不自动安装新包、不发布 GitHub Release。

---

## 3. 文件职责图

| 文件 | 责任 |
|---|---|
| `scripts/Invoke-SmartGuardDotNetTests.ps1` | 启动/轮询 .NET 测试子进程，读取真实退出码和 TRX，返回结构化结果 |
| `Run-Tests.ps1` | 汇总 Pester、.NET、Tray integration、Installer integration，输出最终 JSON 和退出码 |
| `Tests/SmartGuard.Tests.ps1` | 测试编排、失败传播、输出锁、提权结果契约的 Pester RED/GREEN 门禁 |
| `scripts/Invoke-ElevatedPesterFile.ps1` | 通用提权 Pester 子进程：transcript + UTF-8 JSON 结果 |
| `scripts/Test-IsProcessElevated.ps1` | 父进程发起单次 UAC、轮询结果、超时和结果校验 |
| `Tests/Integration/SmartGuardStop.ps1` | 停止进程并验证退出；权限失败时 fail fast |
| `Tests/Integration/Restore-SmartGuardScheduledTasks.ps1` | 捕获并恢复机器级计划任务/进程基线 |
| `Tests/Integration/TrayCoreUserFlow.Lifecycle.ps1` | Tray 提权子流程的外层 Capture/Stop/Restore 事务 |
| `Tests/Integration/TrayCoreUserFlow.Tests.ps1` | 真实启动 Engine、验证状态文件；不再吞掉启动/停止前置条件失败 |
| `Tests/Integration/TrayCoreUserFlow.Helpers.ps1` | 启动进程、等待状态、收集退出码/日志诊断 |
| `src/SmartGuard.Settings/SettingsLogPageHost.cs` | 拥有 refresh flight、取消、generation、fault 和 Dispose 生命周期 |
| `src/SmartGuard.Settings/LogView/LogTailReaderTestMetrics.cs` | 复用并扩展现有测试缝，确定性阻塞或注入一次读盘异常 |
| `src/SmartGuard.Settings/SettingsWindowController.cs` | Dispose 入口，保证所有子 coordinator 生命周期结束 |
| `Tests/SmartGuard.Settings.Tests/WpfStaTestHost.cs` | 条件等待、窗口清理、静默状态验证、静态基线恢复 |
| `Tests/SmartGuard.Settings.Tests/WpfApplicationFixture.cs` | 唯一 STA Application/Dispatcher 生命周期 |
| `Tests/SmartGuard.Settings.Tests/SettingsLogNavResponsivenessTests.cs` | 日志内容/刷新精确等待 helper |
| `Tests/SmartGuard.Settings.Tests/*` | 将失败过的 UI 用例迁移到统一等待和 Dispose 契约 |
| `docs/ARCHITECTURE-CONTRACT.md` | 登记测试结果真值、提权结果、WPF 生命周期和集成恢复契约 |
| `.github/workflows/test.yml` | CI 执行快速/完整门禁与稳定性重复门禁 |

---

## 4. 分阶段执行清单

### Task 1：冻结失败样本并建立“假绿必须变红”的 RED

**Files:**
- Modify: `Tests/SmartGuard.Tests.ps1`
- Read only: `scripts/Invoke-SmartGuardDotNetTests.ps1`
- Read only: `Run-Tests.ps1`

**Interfaces:**
- Produces: 编排行为测试，证明非零子退出码必须传到 helper 和父门禁。
- Produces: TRX 缺失、损坏、Outcome 非 Completed 时必须失败的契约。
- Produces: fast/full 不选择纯 Performance 项目、nightly 才选择该项目的分层契约。

- [ ] **Step 1：增加真实非零子进程 RED**

在 `Describe 'Run-Tests optimization contract'` 中 dot-source helper，调用计划在 Task 2 提供的公共测试缝：

```powershell
It 'captured test process preserves the real non-zero child exit code' {
    $root = Split-Path -Parent $PSScriptRoot
    . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')

    $temp = Join-Path ([IO.Path]::GetTempPath()) ('sg-exit-truth-' + [guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $temp)
    try {
        $entry = Start-SmartGuardCapturedCommand `
            -CommandText '"%SystemRoot%\System32\cmd.exe" /d /c exit 23' `
            -ResultDirectory $temp `
            -RunId 'expected-failure'

        $result = Complete-SmartGuardCapturedCommand -Entry $entry -TimeoutSeconds 10
        $result.ExitCode | Should -Be 23
    }
    finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
```

- [ ] **Step 2：运行 RED**

```powershell
Import-Module Pester -MinimumVersion 5.0
Invoke-Pester -Path Tests/SmartGuard.Tests.ps1 -FullNameFilter '*real non-zero child exit code*'
```

Expected: FAIL，原因是 `Start-SmartGuardCapturedCommand` / `Complete-SmartGuardCapturedCommand` 尚不存在。保存 RED 输出。

- [ ] **Step 3：增加 TRX fail-closed RED**

覆盖以下输入：

```powershell
It 'missing or malformed TRX never becomes a passed project result' {
    $missing = Read-SmartGuardTrxResult -Path (Join-Path $TestDrive 'missing.trx') -ExitCode 0
    $missing.Result | Should -Be 'Failed'
    $missing.Error | Should -Match 'TRX'

    [IO.File]::WriteAllText(
        (Join-Path $TestDrive 'broken.trx'),
        '<not-valid-trx',
        [Text.UTF8Encoding]::new($false))
    $broken = Read-SmartGuardTrxResult -Path (Join-Path $TestDrive 'broken.trx') -ExitCode 0
    $broken.Result | Should -Be 'Failed'
}
```

- [ ] **Step 4：增加父门禁契约 RED**

断言 `Run-Tests.ps1`：

```powershell
$runTests | Should -Match '\$dotNetResult\.Result\s*-ne\s*''Passed'''
$runTests | Should -Match 'run-tests-.*summary\.json'
$runTests | Should -Not -Match 'PASSED=\$total FAILED=\$failed'
```

Expected: 当前实现 FAIL，因为 helper 只返回 bool，最终汇总不包含 .NET 计数。

- [ ] **Step 5：增加项目选择 RED**

当前 `SmartGuard.Engine.PerformanceTests` 中所有测试均标记 `Category=Performance`。先固定以下行为，避免 fast/full 选择该项目后被过滤成 `total == 0`，与 fail-closed 契约互相冲突：

```powershell
$fastProjects = Get-SmartGuardDotNetTestProjectPaths -RepoRoot $root -Tier fast
$fullProjects = Get-SmartGuardDotNetTestProjectPaths -RepoRoot $root -Tier full
$nightlyProjects = Get-SmartGuardDotNetTestProjectPaths -RepoRoot $root -Tier nightly

$fastProjects | Should -Not -Match 'SmartGuard\.Engine\.PerformanceTests'
$fullProjects | Should -Not -Match 'SmartGuard\.Engine\.PerformanceTests'
$nightlyProjects | Should -Match 'SmartGuard\.Engine\.PerformanceTests'
```

Expected: 当前实现 FAIL，因为项目选择不接收 Tier，并且三层都包含 Performance 项目。

- [ ] **Step 6：保存 RED 证据但不提交失败状态**

```powershell
Import-Module Pester -MinimumVersion 5.0
$red = Invoke-Pester -Path Tests/SmartGuard.Tests.ps1 -PassThru
$redEvidence = [pscustomobject]@{
    Result = [string]$red.Result
    Failed = @($red.Tests | Where-Object Result -eq 'Failed' | ForEach-Object { $_.ExpandedName })
    CompletedAtUtc = [DateTime]::UtcNow.ToString('O')
} | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText(
    '.work\test-results\task1-false-green-red.json',
    $redEvidence,
    [Text.UTF8Encoding]::new($false))
if ($red.Result -eq 'Passed') {
    throw 'Expected Task 1 RED, but the targeted false-green contracts all passed.'
}
```

记录预期失败名称和退出状态；保留工作区改动直接进入 Task 2。项目规则禁止把已知失败测试单独提交，Task 1 的测试与 Task 2 的最小 GREEN 实现一起提交。

**Task 1 验收：**

- RED 必须由缺失的真实结果接口或错误的当前行为触发。
- 禁止只写源码正则 `Should -Not -Match '%ERRORLEVEL%'` 作为唯一测试；必须实际启动一个退出 23 的子进程。
- RED 日志已保存，但 Git 历史中不得出现“仅含失败测试”的提交。

---

### Task 2：建立 .NET 子测试唯一真值和结构化汇总

**Files:**
- Modify: `scripts/Invoke-SmartGuardDotNetTests.ps1`
- Modify: `Run-Tests.ps1`
- Modify: `Tests/SmartGuard.Tests.ps1`

**Interfaces:**
- Produces: `Start-SmartGuardCapturedCommand(...) -> process entry`
- Produces: `Complete-SmartGuardCapturedCommand(...) -> { ExitCode; Output; TimedOut }`
- Produces: `Read-SmartGuardTrxResult(...) -> { Result; PassedCount; FailedCount; SkippedCount; TotalCount; Error }`
- Changes: `Get-SmartGuardDotNetTestProjectPaths -RepoRoot -Tier` 只返回该层实际应执行且至少发现一项测试的项目。
- Changes: `Invoke-SmartGuardDotNetTestSuite(...)` 返回结构化 summary，不再返回 bool。

- [ ] **Step 1：最小实现真实退出码捕获**

核心要求：

```powershell
function Start-SmartGuardCapturedCommand {
    param(
        [Parameter(Mandatory)][string]$CommandText,
        [Parameter(Mandatory)][string]$ResultDirectory,
        [Parameter(Mandatory)][string]$RunId
    )

    $stdoutPath = Join-Path $ResultDirectory "dotnet-test-$RunId.stdout.log"
    $command = "$CommandText > `"$stdoutPath`" 2>&1"
    $process = Start-Process -FilePath $env:ComSpec `
        -ArgumentList @('/d', '/c', $command) `
        -PassThru

    [pscustomobject]@{
        Process = $process
        StdoutPath = $stdoutPath
        RunId = $RunId
    }
}
```

`Complete-SmartGuardCapturedCommand` 必须轮询 `Process.HasExited`，完成后调用 `WaitForExit()`，直接读取 `Process.ExitCode`。不得再通过 `echo %ERRORLEVEL%`、`$LASTEXITCODE` 或解析控制台文本推断退出码。

- [ ] **Step 2：超时必须终止完整进程树**

新增明确 helper：

```powershell
function Stop-SmartGuardCapturedProcessTree {
    param([int]$ProcessId)

    & taskkill.exe /PID $ProcessId /T /F 2>$null | Out-Null
    if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
        throw "Unable to stop timed-out test process tree. PID=$ProcessId"
    }
}
```

只允许对当前 runner 捕获的 PID 调用；禁止按名称全局杀 `dotnet`、`cmd`、`conhost`。

- [ ] **Step 3：每个项目生成唯一 TRX**

`dotnet test` 参数增加：

```powershell
$trxName = "dotnet-test-$runId.trx"
$arguments += @(
    '--logger', "trx;LogFileName=$trxName",
    '--results-directory', $resultDirectory
)
```

`Read-SmartGuardTrxResult` 使用 XML 的 `ResultSummary/Counters`，至少读取：

```text
total
executed
passed
failed
error
timeout
aborted
inconclusive
notExecuted
```

判定规则：

```text
Passed =
  ExitCode == 0
  AND TRX 存在且可解析
  AND outcome == Completed
  AND failed + error + timeout + aborted == 0
  AND total > 0
```

任何条件不满足均 `Result = Failed`。不得把 `total == 0` 当成功。

项目选择必须先按 Tier 收敛：

- `fast` / `full`：不选择纯 `Category=Performance` 的 `SmartGuard.Engine.PerformanceTests`；
- `nightly`：选择 Performance 项目且不应用排除 Performance 的过滤器；
- 不允许通过放宽 `total > 0`、忽略空 TRX 或给纯 Performance 项目添加伪测试来消除冲突。

- [ ] **Step 4：返回结构化 .NET summary**

`Invoke-SmartGuardDotNetTestSuite` 返回：

```powershell
[pscustomobject]@{
    Result = 'Passed' # or Failed
    PassedCount = 0
    FailedCount = 0
    SkippedCount = 0
    TotalCount = 0
    ProjectResults = @(...)
}
```

并行项目和串行项目都使用同一结果类型；任何项目失败后允许打印其完整输出，但 summary 必须保持失败。

- [ ] **Step 5：Run-Tests 接收结构化结果**

```powershell
$dotNetResult = Invoke-SmartGuardDotNetTestSuite -RepoRoot $PSScriptRoot -Tier $tier
if ($dotNetResult.Result -ne 'Passed') {
    Write-Host "DotNet FAILED=$($dotNetResult.FailedCount)" -ForegroundColor Red
    exit 1
}
```

最终汇总必须分别显示来源，不能再用只含 Pester 的 `PASSED=79` 冒充全量：

```text
PESTER: Passed=...
DOTNET: Passed=... Failed=... Skipped=... Total=...
TRAY_INTEGRATION: Passed=...
INSTALLER_INTEGRATION: Passed=...
RESULT: Passed|Failed
```

同时以 UTF-8 无 BOM 写入：

```text
.work/test-results/run-tests-<runId>.summary.json
```

JSON 至少包含 `CompletedAtUtc`、`Tier`、`Result`、四层结果、总退出码和各 TRX 路径。

- [ ] **Step 6：运行 GREEN**

```powershell
Import-Module Pester -MinimumVersion 5.0
Invoke-Pester -Path Tests/SmartGuard.Tests.ps1
```

Expected: Task 1 的退出 23、TRX fail-closed、父门禁契约全部 PASS。

- [ ] **Step 7：反向注入失败验证**

通过测试缝运行一个确定失败的临时子命令，验证：

```text
child exit = 23
project Result = Failed
dotnet summary Result = Failed
parent gate exit != 0
```

- [ ] **Step 8：提交 GREEN**

```powershell
git add scripts/Invoke-SmartGuardDotNetTests.ps1 Run-Tests.ps1 Tests/SmartGuard.Tests.ps1
git commit -m "fix(test): propagate real dotnet child results"
```

**Task 2 验收：**

- 控制台、TRX、summary JSON、父退出码四者不得矛盾。
- 删除 `%ERRORLEVEL%` 结果文件协议。
- 缺失/空/损坏 TRX 必须红，不能降级为仅信任退出码。
- 每个被选中的项目必须 `total > 0`；fast/full 不得启动纯 Performance 项目。
- [x] Task 2A adjacent architecture-gate migration: replace the obsolete `.exit` assertions with the captured-process, TRX fail-closed, structured-summary, and `Run-Tests.ps1` propagation contract.
- [x] Task 2A review follow-up: require the structured summary's `ConvertTo-Json` serialization and explicit UTF-8 no-BOM `WriteAllText` persistence, not only its filename.

---

### Task 3：统一提权 Pester 结果协议，禁止“父进程 0 tests”

**Files:**
- Create: `scripts/Invoke-ElevatedPesterFile.ps1`
- Modify: `scripts/Test-IsProcessElevated.ps1`
- Modify: `scripts/Invoke-ElevatedInstallerUserFlow.ps1`（迁移后删除或改成无逻辑薄包装）
- Modify: `Tests/Integration/InstallerUserFlow.Tests.ps1`
- Modify: `Tests/SmartGuard.Tests.ps1`
- Modify: `Run-Tests.ps1`

**Interfaces:**
- Produces: `Invoke-ElevatedPesterFile.ps1 -TestPath -ResultPath -TranscriptPath -Tier`
- Produces: JSON `{ Result; ExitCode; PassedCount; FailedCount; SkippedCount; TotalCount; Error; CompletedAtUtc }`
- Produces: `Invoke-PesterFileSingleElevation(...)` 父侧轮询接口。

- [ ] **Step 1：写 RED**

新增 Pester 契约：

```powershell
It 'elevated Pester result rejects zero discovered tests' {
    $result = Test-SmartGuardElevatedPesterResult -Payload ([pscustomobject]@{
        Result = 'Passed'
        ExitCode = 0
        PassedCount = 0
        FailedCount = 0
        TotalCount = 0
    })
    $result.IsValid | Should -BeFalse
}
```

再增加结构断言：

```powershell
$runner | Should -Match 'Invoke-Pester'
$runner | Should -Match '\.Result\s*-ne\s*''Passed'''
$runner | Should -Match 'TotalCount'
$runner | Should -Match 'UTF8Encoding'
$runner | Should -Match 'CompletedAtUtc'
$runner | Should -Not -Match 'ArgumentList.*-Command'
```

- [ ] **Step 2：运行 RED**

Expected: FAIL，因为当前只有 installer 专用 runner，且父层曾出现 `Discovery found 0 tests` 后仍继续。

- [ ] **Step 3：实现通用磁盘脚本 runner**

子脚本职责：

1. 校验 TestPath 存在。
2. 开启 transcript。
3. 设置明确环境标记，避免 Pester 文件再次自提权。
4. `Invoke-Pester -Path $TestPath -PassThru`。
5. 以整体 `Result` 和 `TotalCount > 0` 判断。
6. `finally` 中始终写 UTF-8 JSON，即使 discovery/container 抛异常。
7. PowerShell 进程退出码与 JSON `ExitCode` 一致。

- [ ] **Step 4：父侧轮询并 fail closed**

父侧每 250ms 轮询，最长 10 分钟；以下任一情况返回失败：

- UAC 取消；
- 子进程提前退出但无 JSON；
- JSON 无法解析；
- `CompletedAtUtc` 缺失；
- `Result != Passed`；
- `ExitCode != 0`；
- `TotalCount == 0`；
- 轮询超时。

- [ ] **Step 5：迁移 Installer flow**

`Run-Tests.ps1` 显式调用父 helper 并接收结构化结果，不再把自提权 Pester 文件在父进程“发现 0 项”视为完成。

直接运行 `InstallerUserFlow.Tests.ps1` 时可以保留开发者便利入口，但必须复用同一通用 helper，不允许第二套 JSON 协议。

- [ ] **Step 6：GREEN 与失败注入**

至少验证：

```text
正常 Pester 文件：TotalCount > 0，父 exit 0
故意失败 Pester 文件：父 exit 1
空 Describe/零测试文件：父 exit 1
损坏 JSON：父 exit 1
子进程异常退出且无 JSON：父 exit 1
```

- [ ] **Step 7：提交**

```powershell
git add scripts/Invoke-ElevatedPesterFile.ps1 scripts/Test-IsProcessElevated.ps1 scripts/Invoke-ElevatedInstallerUserFlow.ps1 Tests/Integration/InstallerUserFlow.Tests.ps1 Tests/SmartGuard.Tests.ps1 Run-Tests.ps1
git commit -m "fix(test): unify observable elevated Pester results"
```

**Task 3 验收：**

- 全量门禁不得再次出现“管理员子流程执行了测试，但父 Pester 显示 0 tests 并算成功”。
- transcript 成功不能替代 JSON、父退出码和总门禁退出码。

---

### Task 4：进程停止从 best-effort 改成可证明前置条件

**Files:**
- Modify: `Tests/Integration/SmartGuardStop.ps1`
- Modify: `Tests/Integration/TrayCoreUserFlow.Helpers.ps1`
- Modify: `Tests/Integration/TrayCoreUserFlow.Tests.ps1`
- Modify: `Tests/SmartGuard.Tests.ps1`

**Interfaces:**
- Produces: `Stop-SmartGuardProcessesWithoutElevation(...)` 成功无输出；失败抛出包含 PID、名称、权限建议的异常。
- Produces: `Assert-SmartGuardProcessesExited(...)`.
- Produces: Tray 启动诊断 `{ ProcessId; ExitCode; StatusPath; StartupLogTail }`。

- [ ] **Step 1：写权限失败 RED**

使用 Pester Mock 模拟 `Stop-Process` AccessDenied 和残留查询：

```powershell
It 'process stop fails fast when a target remains alive' {
    . (Join-Path $root 'Tests\Integration\SmartGuardStop.ps1')
    Mock Stop-Process { throw [UnauthorizedAccessException]::new('Access denied') }
    Mock Get-Process {
        [pscustomobject]@{ Id = 4242; ProcessName = 'SmartGuard.Engine' }
    }

    { Stop-SmartGuardProcessesWithoutElevation -ProcessId 4242 } |
        Should -Throw '*4242*administrator*'
}
```

- [ ] **Step 2：运行 RED**

Expected: 当前 `SilentlyContinue` 吞掉错误，测试 FAIL。

- [ ] **Step 3：实现停止后验证**

规则：

1. 记录每个目标 PID 和异常。
2. 发出 Stop 后按条件轮询最多 5 秒。
3. 目标 PID 或 Engine/Tray 仍存在即抛异常。
4. 异常包含：
   - PID；
   - ProcessName；
   - 当前会话是否管理员；
   - 建议通过完整门禁提权入口运行；
   - 不得继续执行 Tray integration。

禁止全局终止 `dotnet`、`powershell`、`cmd` 或 `conhost`。

- [ ] **Step 4：Tray 启动过程早退出诊断**

`Wait-StatusFile` 每轮同时检查 `$proc.HasExited`。若 Engine 在状态文件出现前退出，立即抛出：

```text
Engine exited before status publication.
PID=<pid>
ExitCode=<code>
StatusPath=<path>
StartupLogTail=<last lines>
PossibleCause=single-instance lock or startup failure
```

不再无条件等待 25 秒后只报告 `$payload` 为空。

- [ ] **Step 5：GREEN**

Pester helper 测试全部通过；手工保留一个高权限 Engine 时，非提权 Tray 测试应在 5 秒内明确失败，而不是 50 秒后两个空 payload。

- [ ] **Step 6：提交**

```powershell
git add Tests/Integration/SmartGuardStop.ps1 Tests/Integration/TrayCoreUserFlow.Helpers.ps1 Tests/Integration/TrayCoreUserFlow.Tests.ps1 Tests/SmartGuard.Tests.ps1
git commit -m "fix(test): fail fast when SmartGuard processes cannot stop"
```

**Task 4 验收：**

- “无法停止现有实例”与“Engine 功能错误”必须成为两种不同错误。
- 进程停止失败后不得创建临时测试 Engine。

---

### Task 5：Tray integration 使用受控提权和任务快照

**Files:**
- Modify: `Run-Tests.ps1`
- Modify: `Tests/Integration/TrayCoreUserFlow.Tests.ps1`
- Modify: `Tests/Integration/TrayCoreUserFlow.Helpers.ps1`
- Create: `Tests/Integration/TrayCoreUserFlow.Lifecycle.ps1`
- Reuse/Modify: `Tests/Integration/Restore-SmartGuardScheduledTasks.ps1`
- Modify: `scripts/Invoke-ElevatedPesterFile.ps1`
- Modify: `Tests/SmartGuard.Tests.ps1`

**Interfaces:**
- Consumes: Task 3 的通用提权 Pester JSON 协议。
- Consumes: `Capture-SmartGuardScheduledTasks` / `Restore-SmartGuardScheduledTasks`.
- Changes: 通用 runner 增加可选 `-LifecycleScriptPath`；由同一 runner 在 Pester 外层执行 Enter/Exit，不引入第二套结果协议。
- Produces: Tray result `TotalCount == 2`，并由父门禁验证。

- [ ] **Step 1：写 Capture/Restore RED**

契约断言 Tray flow 必须：

```powershell
$lifecycle | Should -Match 'Enter-SmartGuardElevatedPesterScope'
$lifecycle | Should -Match 'Capture-SmartGuardScheduledTasks'
$lifecycle | Should -Match 'Stop-SmartGuardForTrayCoreTest'
$lifecycle | Should -Match 'Exit-SmartGuardElevatedPesterScope'
$lifecycle | Should -Match 'Restore-SmartGuardScheduledTasks'
$runner | Should -Match 'LifecycleScriptPath'
$runner | Should -Match 'finally'
```

顺序断言必须满足 Capture 在 Stop 之前。

- [ ] **Step 2：由提权 runner 的外层生命周期建立并恢复基线**

先用 Step 1 的失败契约驱动 `Invoke-ElevatedPesterFile.ps1` 增加 `-LifecycleScriptPath`。若提供该参数，dot-source 后必须存在 `Enter-SmartGuardElevatedPesterScope` 与 `Exit-SmartGuardElevatedPesterScope`；Exit 在 Pester 外层 `finally` 中执行，且 Exit 失败必须把最终结果改为 Failed。

`TrayCoreUserFlow.Lifecycle.ps1` 提供：

```powershell
function Enter-SmartGuardElevatedPesterScope {
    $snapshot = Capture-SmartGuardScheduledTasks
    try {
        Stop-SmartGuardForTrayCoreTest
        return $snapshot
    }
    catch {
        Restore-SmartGuardScheduledTasks -Snapshot $snapshot
        throw
    }
}

function Exit-SmartGuardElevatedPesterScope {
    param($State)
    try {
        Stop-SmartGuardForTrayCoreTest
    }
    finally {
        Restore-SmartGuardScheduledTasks -Snapshot $State
    }
}
```

该生命周期脚本先 dot-source `TrayCoreUserFlow.Helpers.ps1` 与 `Restore-SmartGuardScheduledTasks.ps1`。通用提权 runner 的控制流必须是：

```powershell
$scopeEntered = $false
try {
    $scopeState = Enter-SmartGuardElevatedPesterScope
    $scopeEntered = $true
    $pesterResult = Invoke-Pester -Path $TestPath -PassThru
}
finally {
    if ($scopeEntered) {
        Exit-SmartGuardElevatedPesterScope -State $scopeState
    }
}
```

Capture/Stop/Restore 不再放在测试文件的 `BeforeAll`/`AfterAll` 中。这样 discovery、BeforeAll、It、AfterAll 任一失败仍由 runner 外层恢复；Restore 完成后才允许写最终 JSON。Restore 失败必须使 JSON `Result=Failed` 且进程退出非零，不能只写 Warning。

`Capture-SmartGuardScheduledTasks` 同时记录有效生产任务对应的 Guardian/Tray 进程在测试前是否运行；`Restore-SmartGuardScheduledTasks` 根据快照决定是否重启，不再由调用方硬编码 `-StartProcesses:$true`：

- 有有效任务且测试前进程运行：恢复任务后重启；
- 有有效任务但测试前进程未运行：恢复任务但保持停止；
- 任务缺失但存在高权限孤儿进程：将孤儿视为需清理的不一致残留，不创建任务、不重启孤儿。

- [ ] **Step 3：Run-Tests 显式提权运行 Tray flow**

父门禁调用 Task 3 通用 helper，并传入 `TrayCoreUserFlow.Lifecycle.ps1`，验证：

```text
Result == Passed
ExitCode == 0
TotalCount == 2
FailedCount == 0
```

- [ ] **Step 4：验证三种机器基线**

1. Guardian/Tray 任务存在且进程运行；
2. 任务存在但进程未运行；
3. 任务缺失但有高权限孤儿进程。

每种情况下都必须：

- Tray tests 2/2；
- 测试结束后的任务根目录与 Capture 一致；
- 有效生产任务对应的进程运行/停止状态与 Capture 一致；
- poison root 不得出现在任务命令或工作目录；
- 原基线为 Missing 时仍保持 Missing；
- 无 `sg-user-flow-*` 目录残留；
- 无测试 Engine 残留。

- [ ] **Step 5：提交**

```powershell
git add Run-Tests.ps1 scripts/Invoke-ElevatedPesterFile.ps1 Tests/Integration/TrayCoreUserFlow.Tests.ps1 Tests/Integration/TrayCoreUserFlow.Helpers.ps1 Tests/Integration/TrayCoreUserFlow.Lifecycle.ps1 Tests/Integration/Restore-SmartGuardScheduledTasks.ps1 Tests/SmartGuard.Tests.ps1
git commit -m "fix(test): isolate elevated tray integration state"
```

**Task 5 验收：**

- 现有高权限 SmartGuard 实例不能再导致 payload 空超时。
- 测试不得永久删除、改写或指向临时目录的生产计划任务。

---

### Task 6：修复 SettingsLogPageHost 异步生命周期和异常吞噬

**Files:**
- Modify: `src/SmartGuard.Settings/SettingsLogPageHost.cs`
- Modify: `src/SmartGuard.Settings/SettingsWindowController.cs`
- Modify: `src/SmartGuard.Settings/LogView/LogTailReaderTestMetrics.cs`
- Create: `Tests/SmartGuard.Settings.Tests/SettingsLogPageHostLifetimeTests.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/WpfStaTestHost.cs`

**Interfaces:**
- Produces: `_disposed`、每次 refresh 的 `CancellationTokenSource`、可观察的 active refresh count。
- Produces: `SettingsLogPageHost.ActiveRefreshFlightCountForTests`.
- Produces: `SettingsLogPageHost.ApplyAfterDisposeCountForTests`.
- Extends: `LogTailReaderTestMetrics.BeforeReadForTests`，复用现有 `MaybeDelayForTests()` 调用点，不新增第二套读盘测试钩子。
- Produces: Dispose 后禁止排队/应用 UI continuation。
- Produces: refresh fault 转成明确状态和测试可见错误，不保持 Loading。

- [ ] **Step 1：写 Dispose 后回调 RED**

扩展现有 `LogTailReaderTestMetrics` 测试缝；`Reset()` 必须同时清空 `BeforeReadForTests`：

```csharp
internal static Action? BeforeReadForTests { get; set; }

internal static void MaybeDelayForTests()
{
    BeforeReadForTests?.Invoke();
    // 保留现有 ArtificialDelayMs 行为
}

[Fact]
public void Disposing_controller_while_log_refresh_is_in_flight_never_applies_after_dispose()
{
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    LogTailReaderTestMetrics.BeforeReadForTests = () =>
    {
        entered.Set();
        if (!release.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Timed out waiting to release synthetic log read.");
    };

    WpfStaTestHost.Run(() =>
    {
        var controller = CreateControllerWithLog();
        var window = GetWindow(controller);
        WpfStaTestHost.ShowAndWait(window);

        controller.NavigateTo("logs");
        entered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        controller.Dispose();
        release.Set();

        WpfStaTestHost.WaitUntil(
            () => SettingsLogPageHost.ActiveRefreshFlightCountForTests == 0,
            TimeSpan.FromSeconds(10),
            "log refresh flights to drain after dispose");

        SettingsLogPageHost.ApplyAfterDisposeCountForTests.Should().Be(0);
    });
}
```

- [ ] **Step 2：运行 RED**

Expected: 当前 host 没有 active-flight/after-dispose 契约，或回调仍在 Dispose 后执行。

- [ ] **Step 3：写后台异常 RED**

同一测试缝改为抛出固定异常：

```csharp
LogTailReaderTestMetrics.BeforeReadForTests =
    () => throw new IOException("synthetic read failure");
controller.NavigateTo("logs");

WpfStaTestHost.WaitUntil(
    () => GetLogStatus(window).Contains("加载失败", StringComparison.Ordinal),
    TimeSpan.FromSeconds(10),
    "log load failure status");

GetLogStatus(window).Should().Contain("加载失败");
```

当前行为会吞 fault 并停留 Loading，测试应 RED。

- [ ] **Step 4：实现最小生命周期**

要求：

```csharp
private bool _disposed;
private CancellationTokenSource? _refreshCancellation;
private Task? _activeRefreshTask;
```

每次 flight：

1. 捕获 generation 和 cancellation token；
2. active count `Interlocked.Increment`；
3. 后台读前后检查 token；
4. continuation 排队前先检查 `_disposed`、token、generation；
5. UI callback 内再次检查；
6. fault 必须观察 `task.Exception` 并切换明确失败状态；
7. active count 必须在后台 Task 的完成路径中 `Interlocked.Decrement`，且不依赖 Dispatcher continuation 是否成功排队或执行；UI continuation 只负责 UI 状态和 pending 调度；
8. Dispose 设置 `_disposed`、递增 generation、取消 CTS、停止所有 timer/coordinator；
9. Dispose 后 `ScheduleLogRefresh` 立即返回。

不得在 UI 线程同步等待 `_activeRefreshTask`，避免 continuation 需要 Dispatcher 时死锁。

- [ ] **Step 5：Controller Dispose 幂等验证**

增加测试：

```csharp
controller.Dispose();
controller.Dispose();
SettingsLogPageHost.ActiveRefreshFlightCountForTests.Should().Be(0);
```

窗口 Closing 与显式 Dispose 同时发生时不得抛异常。

- [ ] **Step 6：GREEN**

```powershell
dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj `
  --filter "FullyQualifiedName~SettingsLogPageHostLifetimeTests"
```

Expected: 全部 PASS。

- [ ] **Step 7：提交**

```powershell
git add src/SmartGuard.Settings/SettingsLogPageHost.cs src/SmartGuard.Settings/SettingsWindowController.cs src/SmartGuard.Settings/LogView/LogTailReaderTestMetrics.cs Tests/SmartGuard.Settings.Tests/SettingsLogPageHostLifetimeTests.cs Tests/SmartGuard.Settings.Tests/WpfStaTestHost.cs
git commit -m "fix(settings): own and cancel log refresh lifetime"
```

**Task 6 验收：**

- Dispose 后应用 UI 次数恒为 0。
- 后台异常在 10 秒内变成明确错误，不得以测试超时表现。
- 不新增同步等待死锁。

---

### Task 7：把 WPF 测试等待和清理改成条件驱动

**Files:**
- Modify: `Tests/SmartGuard.Settings.Tests/WpfStaTestHost.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/SettingsLogNavResponsivenessTests.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/SettingsWindowControllerTests.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/SettingsLogPageRestoreTests.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/SettingsWindowResizeStabilityTests.cs`
- Modify: 其他使用相同旧 helper 的 WPF 测试文件
- Modify: `Tests/SmartGuard.Tests.ps1`

**Interfaces:**
- Produces: `WpfStaTestHost.WaitUntil(Func<bool>, TimeSpan, string)`.
- Produces: `WpfStaTestHost.WaitForRender(Window)`.
- Produces: `WpfStaTestHost.AssertQuiescent()`.
- Replaces: `WaitUntilLogContent` 的宽泛状态文本条件。

- [ ] **Step 1：新增 WaitUntil RED**

测试必须覆盖：

1. 条件在 Dispatcher callback 后成立；
2. timeout 异常包含 description、窗口数、active refresh 数；
3. 不依赖 `Thread.Sleep`；
4. 不吞 callback 异常。

- [ ] **Step 2：实现 DispatcherTimer + DispatcherFrame 条件等待**

示意：

```csharp
public static void WaitUntil(
    Func<bool> condition,
    TimeSpan timeout,
    string description)
{
    if (condition()) return;

    var frame = new DispatcherFrame();
    Exception? error = null;
    var deadline = DateTime.UtcNow + timeout;
    var timer = new DispatcherTimer(
        TimeSpan.FromMilliseconds(10),
        DispatcherPriority.Background,
        (_, _) =>
        {
            try
            {
                if (condition() || DateTime.UtcNow >= deadline)
                    frame.Continue = false;
            }
            catch (Exception ex)
            {
                error = ex;
                frame.Continue = false;
            }
        },
        Dispatcher.CurrentDispatcher);

    try
    {
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    finally
    {
        timer.Stop();
    }

    if (error is not null) throw error;
    if (!condition())
        throw new TimeoutException(BuildDiagnostic(description));
}
```

这里的嵌套 frame 是 WPF STA 测试允许 Dispatcher continuation 执行的显式机制；禁止同时 `Thread.Sleep`。

- [ ] **Step 3：精确替换日志等待**

拆成不同 helper，不再只看 `lblLogStatus != Loading`：

```csharp
WaitUntilLogItems(window, expectedMinimum: 1)
WaitUntilLogStatusContains(window, "匹配")
WaitUntilRefreshFlightCount(expected: 0)
WaitUntilVerticalOffset(viewer, predicate)
WaitUntilFlyoutHitTest(window, expectedButton)
```

失败过的三个用例必须分别等待自己的最终业务条件：

- `Log_page_more_flyout_keeps_bottom_action_clickable_above_lower_content`：等待 flyout 可见、按钮 ActualWidth/Height > 0、Render 完成，再做 hit test。
- `User_navigates_to_logs_still_loads_log_content`：等待 `Items.Count > 0` 且 active refresh == 0。
- `User_returning_from_logs_to_about_starts_at_top`：等待导航页面可见、layout/render 完成、offset 约等于 0。

- [ ] **Step 4：清理必须验证静默**

`WpfStaTestHost.Run` 的 finally：

1. 关闭全部窗口；
2. 等待 `ActiveRefreshFlightCountForTests == 0`；该计数由后台完成路径归零，不得依赖已关闭 Dispatcher 上的 UI callback；
3. 执行 ApplicationIdle/Render barrier；
4. 断言 `Application.Current.Windows.Count == 0`；
5. 恢复以下静态状态：
   - `SettingsUiTestMode`；
   - `ThemeTransitionAnimator`；
   - `LogViewTagPalette`；
   - log test hooks；
   - test metrics；
6. 若清理失败，当前测试必须失败，不能 catch 后忽略。

- [ ] **Step 5：源码门禁**

在 `Tests/SmartGuard.Tests.ps1` 增加：

```powershell
$wpfHost | Should -Not -Match 'Thread\.Sleep'
$waitHelper | Should -Not -Match 'Thread\.Sleep\(1\)'
$wpfHost | Should -Match 'AssertQuiescent'
```

只限制 WPF 等待/清理 helper；不粗暴禁止整个测试项目所有 Sleep。

- [ ] **Step 6：运行 GREEN**

```powershell
dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj
```

Expected: 309/309 或实施时最新总数全部 PASS。

- [ ] **Step 7：提交**

```powershell
git add Tests/SmartGuard.Settings.Tests Tests/SmartGuard.Tests.ps1
git commit -m "test(settings): make WPF waits and cleanup deterministic"
```

**Task 7 验收：**

- UI 测试失败必须指出等待的具体条件和诊断状态。
- 每例结束 active refresh=0、window count=0。
- 不允许用增大超时替代生命周期修复。

---

### Task 8：增加无重试稳定性压力门禁

**Files:**
- Create: `scripts/Invoke-TestStabilityGate.ps1`
- Modify: `.github/workflows/test.yml`
- Modify: `Tests/SmartGuard.Tests.ps1`

**Interfaces:**
- Produces: `Invoke-TestStabilityGate.ps1 -Project -Filter -Iterations -ResultPath`.
- Produces: UTF-8 JSON `{ Iterations; Completed; Failures; FirstFailure; CompletedAtUtc }`.

- [ ] **Step 1：写 RED**

契约：

```powershell
It 'stability gate stops at first failure and never retries to green' {
    # 使用第 2 轮固定失败的测试命令替身
    $result.Failures | Should -Be 1
    $result.Completed | Should -Be 2
    $result.Result | Should -Be 'Failed'
}
```

- [ ] **Step 2：实现**

要求：

- 每轮创建独立 TRX；
- 任一轮失败立即停止；
- 不自动重跑失败轮；
- 保存第一失败轮的 stdout、TRX、退出码；
- 成功时记录所有 TRX 路径；
- 结果 JSON 显式 UTF-8 无 BOM。

- [ ] **Step 3：本地目标压力门禁**

失败过的三个测试组合运行 50 轮：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-TestStabilityGate.ps1 `
  -Project Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj `
  -Filter "FullyQualifiedName~Log_page_more_flyout_keeps_bottom_action_clickable_above_lower_content|FullyQualifiedName~User_navigates_to_logs_still_loads_log_content|FullyQualifiedName~User_returning_from_logs_to_about_starts_at_top" `
  -Iterations 50 `
  -ResultPath .work\test-results\settings-wpf-targeted-stability.json
```

- [ ] **Step 4：完整 Settings 重复门禁**

完整 Settings 套件连续 5 轮，任一轮失败即失败：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-TestStabilityGate.ps1 `
  -Project Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj `
  -Iterations 5 `
  -ResultPath .work\test-results\settings-wpf-full-stability.json
```

- [ ] **Step 5：CI 分层**

- 普通 PR：`SMARTGUARD_TEST_TIER=fast`，排除 `WpfUi` 和 Live/Performance。
- Windows 完整门禁：运行一次全量。
- Nightly/手动 stability：目标 50 轮 + Settings 5 轮。
- CI artifact 上传 summary JSON、失败 stdout、TRX；成功也保留 summary。

- [ ] **Step 6：提交**

```powershell
git add scripts/Invoke-TestStabilityGate.ps1 .github/workflows/test.yml Tests/SmartGuard.Tests.ps1
git commit -m "test(ci): add no-retry WPF stability gate"
```

**Task 8 验收：**

- Stability gate 自身遇到第 2 轮失败时必须整体失败，不得跑到后续轮次。
- CI 不以“重试后成功”覆盖第一失败证据。

---

### Task 9：架构契约、终验和发布放行

**Files:**
- Modify: `docs/ARCHITECTURE-CONTRACT.md`
- Modify: `Changelog.md`（仅当修复计划进入实际发布版本）
- Verify: all files above

- [ ] **Step 1：更新架构契约**

登记四条阻断契约：

1. 测试子进程真值 = `Process.ExitCode + TRX`；两者矛盾时失败。
2. 提权测试真值 = 子 JSON + transcript + 父验证；零测试或缺 JSON 失败。
3. WPF controller Dispose 后不得有 UI apply；测试结束必须 quiescent。
4. Integration 必须 Capture → Stop → Test → Restore；Stop/Restore 失败均红。

- [ ] **Step 2：运行聚焦测试**

```powershell
Import-Module Pester -MinimumVersion 5.0
Invoke-Pester -Path Tests/SmartGuard.Tests.ps1

dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj `
  --filter "FullyQualifiedName~SettingsLogPageHostLifetimeTests"

dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj
```

- [ ] **Step 3：运行稳定性门禁**

必须同时满足：

```text
targeted WPF: 50/50 iterations passed
full Settings: 5/5 iterations passed
Tray integration with elevated existing app: 3/3 runs passed
```

- [ ] **Step 4：运行完整门禁两次**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1
```

两次都必须：

- 进程退出码 0；
- summary JSON `Result = Passed`；
- 每个 .NET 项目 TRX `failed=0`；
- Tray `TotalCount=2`；
- Installer `TotalCount` 等于当前已登记用例数且大于 0；
- 没有 `dotnet test ... FAILED` 文本；
- 没有测试临时目录进入计划任务；
- 测试结束无 Engine/Tray 临时进程。

- [ ] **Step 5：反向失败传播终验**

使用专用测试夹具注入一个确定失败，不修改真实测试断言，验证：

```text
child exit != 0
TRX failed > 0
dotnet summary Result = Failed
Run-Tests parent exit != 0
final summary JSON Result = Failed
```

- [ ] **Step 6：工作区与编码终验**

```powershell
git diff --check
git status --short
```

逐个新增/修改的中文文件验证 UTF-8；不得因终端显示判断乱码。

- [ ] **Step 7：独立审查**

审查重点：

- 是否还有 `%ERRORLEVEL%` 提前展开；
- 是否还有“输出显示失败但父结果为 Passed”的路径；
- 是否仍 catch/ignore WPF background fault；
- 是否仍用固定 Sleep 作为 UI 完成条件；
- 是否有 Stop/Restore best-effort 后继续测试；
- 是否存在 retry-to-green。

- [ ] **Step 8：最终提交**

```powershell
git add docs/ARCHITECTURE-CONTRACT.md Changelog.md
git commit -m "docs(test): register trustworthy gate contracts"
```

---

## 5. 最终验收标准

### A. 正确性

- [ ] 任意 .NET 子项目退出非零，`Run-Tests.ps1` 最终退出非零。
- [ ] 任意 TRX 缺失、损坏、零测试、Outcome 非 Completed，最终退出非零。
- [ ] Pester discovery/container failure 即使 `FailedCount=0`，最终退出非零。
- [ ] 提权子进程 JSON 缺失/损坏/超时/零测试，最终退出非零。
- [ ] 最终 summary 数字包含全部 .NET 和 Pester 层，不再输出误导性的局部 `PASSED` 总数。

### B. WPF 稳定性

- [ ] 三个历史失败测试组合连续 50 轮零失败。
- [ ] 完整 Settings 套件连续 5 轮零失败。
- [ ] 每个 WPF 用例结束：窗口数 0、active refresh 0、timer/测试钩子恢复。
- [ ] Dispose 后 UI apply 次数 0。
- [ ] 后台日志读取异常显示明确失败，不以“Loading 超时”表现。
- [ ] WPF 等待 helper 无固定 `Thread.Sleep`。

### C. 集成隔离

- [ ] 高权限现有 Engine/Tray 不再导致测试 Engine 静默退出后等待 25 秒。
- [ ] Stop 失败 5 秒内 fail fast，错误包含 PID、进程名和权限信息。
- [ ] Tray integration 在三种机器基线下各运行 3 次全部 2/2。
- [ ] 测试后计划任务根目录与 Capture 一致，或原 Missing 仍 Missing。
- [ ] 无 `sg-user-flow-*` / `sg-installer-ui-*` poison root 遗留到任务。

### D. 完整门禁

- [ ] `Tests/SmartGuard.Tests.ps1` 全绿。
- [ ] Architecture.Tests 全绿。
- [ ] Settings targeted 50/50、full 5/5。
- [ ] `Run-Tests.ps1` 连续两次全绿且两次 summary JSON 均可解析。
- [ ] 反向失败注入完整传播到父退出码非零。
- [ ] 无测试重试掩盖第一次失败。

### E. 交付证据

- [ ] 每个任务保存 RED 输出、GREEN 输出、命令和退出码。
- [ ] 提权流程保存 transcript、JSON、父退出码。
- [ ] Stability 保存每轮 TRX 和总 summary。
- [ ] 最终回复列出准确通过数量、失败数量、commit、未完成项；不得只写“全部通过”。

---

## 6. 回滚策略

| 阶段 | 回滚方式 |
|---|---|
| Task 1 RED | 仅测试提交，可直接 revert 该独立 commit |
| Task 2 runner | revert runner commit，恢复旧门禁；但恢复后禁止发布，因为旧门禁已知假绿 |
| Task 3 UAC | 保留旧 installer wrapper 到新通用 runner 完成终验后再删除；迁移失败时切回旧 wrapper |
| Task 4/5 integration | Restore helper 必须先恢复机器基线；代码回滚不能代替机器状态恢复 |
| Task 6 Settings 生命周期 | 独立 revert production + lifetime tests；不得只删失败测试 |
| Task 7 WPF harness | 独立 revert harness commit；若回滚后 flake 恢复，门禁保持阻断 |
| Task 8 CI stability | 可暂时从 PR 移到 nightly，但不得删除本地脚本和验收证据 |

任何回滚后若已知假绿路径重新出现，发布门禁状态必须标记为 **BLOCKED**，不能降级为警告。

---

## 7. 执行顺序与提交边界

```text
Task 1 RED
  ↓
Task 2 子进程/TRX真值 GREEN
  ↓
Task 3 通用提权结果 GREEN
  ↓
Task 4 Stop fail-fast GREEN
  ↓
Task 5 Tray Capture/Restore GREEN
  ↓
Task 6 WPF 生命周期 GREEN
  ↓
Task 7 WPF 条件等待/静默清理 GREEN
  ↓
Task 8 无重试稳定性门禁 GREEN
  ↓
Task 9 契约与两轮终验
```

禁止跨越顺序：

- Task 2 未完成前，不可信任任何完整门禁绿灯。
- Task 3 未完成前，不可信任 UAC transcript 单独成功。
- Task 6 未完成前，不允许仅靠修改 WPF 等待 helper 解决 flake。
- Task 7 未完成前，不进入重复压力验收。
- 任一 Task 未独立全绿和提交，不开始下一 Task。

---

## 8. 自审结果

### Spec coverage

| 要求 | 覆盖任务 |
|---|---|
| 假绿退出码 | Task 1–2 |
| TRX 与结构化汇总 | Task 2 |
| Pester/提权零测试假绿 | Task 3 |
| Stop 权限失败被吞 | Task 4 |
| Tray 高权限残留实例 | Task 4–5 |
| 计划任务恢复 | Task 5 |
| WPF Dispose 后回调 | Task 6 |
| WPF 异常吞噬 | Task 6 |
| 固定 Sleep / 宽泛等待 | Task 7 |
| 跨测试状态污染 | Task 6–7 |
| 重复稳定性证明 | Task 8–9 |
| 反向失败传播 | Task 2、3、9 |
| 架构契约与回滚 | Task 9、§6 |

### 占位内容扫描

- 全文没有待定项或缺少执行细节的占位语句。
- 每个行为任务均给出文件、接口、RED、GREEN、命令和验收。
- 未将“多跑几次”作为修复；重复运行只出现在最终稳定性门禁。

---

## 9. Grill-me 自主阻塞审查记录（2026-07-26）

仅记录会使计划无法执行、产生假结论或破坏机器基线的阻塞项；均按推荐方案写回对应任务。

| 阻塞项 | 现场反证 | 推荐决策与写回结果 | 状态 |
|---|---|---|---|
| `total > 0` 与纯 Performance 项目在 fast/full 被过滤为空互相冲突 | `SmartGuard.Engine.PerformanceTests` 的测试标记为 `Category=Performance`，当前项目选择三层均包含该项目，而 fast/full 过滤器排除 Performance | Task 1 增加分层项目选择 RED；Task 2 要求 fast/full 不选择该项目，nightly 才选择；保留所有已选项目 `total > 0` | 已消除 |
| Task 6 引用了不存在的 `LogTailReaderTestHook` | 当前仓库只有 `LogTailReaderTestMetrics.ArtificialDelayMs` / `MaybeDelayForTests()`，没有该类型或 `BlockNextRead` / `ThrowNextRead` | 改为扩展现有 `LogTailReaderTestMetrics.BeforeReadForTests`，明确阻塞、放行、异常注入和 Reset 契约 | 已消除 |
| Tray 的 Capture/Restore 放在 Pester `BeforeAll`/`AfterAll`，不能覆盖 discovery/容器级失败 | 当前 installer 流程同样在测试容器内恢复；提权 runner 只包围 `Invoke-Pester` 并在其后写 JSON | 给通用提权 runner 增加唯一的外层 lifecycle 接口；Tray lifecycle 在 runner 的 try/finally 中 Capture/Stop/Restore，恢复完成后才写结果 | 已消除 |
| Tray 恢复硬编码 `StartProcesses:$true` 会改变“任务存在但进程原本停止”的机器状态 | 当前快照只保存任务槽，Task 5 又要求验证停止态基线，二者无法同时成立 | 快照增加有效生产进程运行态；恢复按快照决定是否重启；缺任务的孤儿进程只清理、不复活 | 已消除 |
| active refresh 计数可能依赖已被 Dispose 的 Dispatcher continuation 归零 | 计划原文只写“finally 清 active count”，同时又禁止 Dispose 后排队 UI callback，存在清理等待永不满足的路径 | Task 6 明确计数在后台 Task 完成路径归零，完全独立于 UI callback；Task 7 静默清理据此等待 | 已消除 |
| 稳定性脚本直接调用可能被本机 PowerShell 执行策略拦截 | 当前环境执行 `.ps1` 的可靠入口是显式 `powershell.exe -ExecutionPolicy Bypass -File` | Task 8 两条本地验收命令改为显式 PowerShell 5.1 Bypass 入口 | 已消除 |
| Task 1 要求提交已知 RED，直接违反仓库“禁止提交测试失败”的规则 | 原计划在确认失败后执行 `git commit`，而项目规则要求提交前相关测试必须通过 | Task 1 改为只保存 RED 日志、不提交；测试改动与 Task 2 最小 GREEN 实现一起提交 | 已消除 |

**审查结论：** 未留下需要用户决策的阻塞项；计划仍按 Task 1–9 顺序执行，不允许以放宽 fail-closed、增加重试或扩大超时替代根因修复。

---

## Execution Handoff

计划已保存到 `docs/superpowers/plans/2026-07-26-test-infrastructure-root-fix.md`。

执行方式：

1. **Subagent-Driven（推荐）**：按 Task 1–9 每项使用新执行者，任务间独立审查。
2. **Inline Execution**：在当前会话按顺序实施，每个 Task 完成 RED/GREEN、提交和检查点后再继续。
