# WPF 测试宿主稳定性根治与对抗审查接入计划

> **状态：** 方案已完成单模型反方审查并采纳必改项；用户选择跳过跨模型复核。本文只定义调查、实施与验收门禁，尚未宣称根因已最终闭合或实现完成。

**Goal:** 消除完整 `Run-Tests.ps1` 中 WPF Settings 测试随机换例失败的问题，建立可复现、可归因、资源有界、失败关闭的测试系统；同时让该测试系统的高影响变更全面经过确定性反例门禁和可追溯对抗审查。

**Architecture:** 先在独立子进程捕获失败现场并区分“测试污染”与“生产调度缺陷”。只在生产等价负载也证明共享线程池饥饿时，才按最小顺序尝试删除冗余外层 `Task.Run`，仍失败才引入页面生命周期拥有的单 worker 有界执行器。测试收尾只等待本模块明确拥有的资源归零，不把整个 CLR ThreadPool 当作可控资源。

**Tech Stack:** .NET 8、WPF、xUnit、PowerShell、TRX、`dotnet-counters`、`dotnet-stack`/`dotnet-trace`、JSON 结构化证据。

---

## 1. 先纠正前提

### 已确认事实

1. 旧提交 `53f2262` 只在 `WpfApplicationFixture` 中把 ThreadPool minimum worker 提高到至少 128；这是容量缓解，不是资源所有权或生命周期根治。
2. 当时 Settings 测试约 306 个；当前约 508 个。此后又加入日志增量加载、分页查询和工作台链路。
3. 最近两次完整门禁分别随机失败于：
   - `User_clicks_refresh_button_and_sees_new_log_lines`
   - `User_typing_custom_range_waits_for_debounce_before_updating_view`
4. 两次失败均停在 `paged-queued` 约 10 秒；现场约有 200 个 ThreadPool worker、15–29 个 pending work item，而分页 flight 尚未进入 `paged-reading`。
5. 两个失败用例分别定向连续 5/5 通过；Settings 单项目已有 546/546 通过证据；完整门禁仍为 Pester 121/121、.NET 1540/1541、Tray/Installer `NotRun`、父退出码 1。
6. `xunit.runner.json` 已关闭 test collection 并行，因此问题不能简单归因于普通 xUnit 并行执行。
7. `SettingsLogRefreshPipeline.StartPagedFlight` 使用外层 `Task.Run`；`LogQueryService.ReadPreviewPageAsync`、`BuildPageIndexAsync`、`ReadPageAsync` 又分别使用 `Task.Run`。
8. WPF 测试包含 `ManualResetEventSlim`、`Thread.Sleep`、`BeforeReadForTests` 等故意阻塞 worker 的场景；当前清理主要依赖手工 tracked controller，global flight 仅诊断、不参与 quiescence 门禁。

### 合理推测，尚非事实

1. 约 200 个 worker 很可能被尚未释放的阻塞型测试工作或后台 flight 占用，导致新分页 work item 排队；但尚无失败现场 worker 堆栈分组证明具体占用者。
2. 嵌套 `Task.Run` 会增加抢占全局 ThreadPool 的调度点和排队不确定性，但仅凭这一结构不能证明它是唯一根因。
3. 同一结构在真实 Settings 进程受到 ThreadPool 压力时可能造成日志页延迟；当前只有测试环境证据，不能直接推导为生产故障。

### 暂时无法验证

1. 失败现场约 200 个 worker 的准确调用栈、CPU 占用、队列长度时间线和 completed-work-item 速率。
2. 去掉故意阻塞测试后，干净子进程能否继续稳定复现。
3. 无测试钩子的生产等价日志负载能否使真实 Settings 路径发生同类 `paged-queued` 首堵。
4. 同步文件枚举取消的最坏响应时间，以及专用 worker 在异常文件系统延迟下能否按预算退出。

### 结论边界

“彻底根治”不能定义为绝对保证以后零 flaky；可验证定义是：有确定性复现、有现场归因、有单一 Owner 和资源上限、有故障反例、有连续干净进程稳定性证据，并且没有靠超时、重试或扩大线程池取得绿色。

---

## 2. 被破坏的不变量

1. **测试隔离：** 一个测试留下的 worker、hook、timer、window 或 flight 不得影响下一测试。
2. **任务所有权：** 每个后台查询必须能回答 Owner、启动、取消、异常观察、退出和最终资源释放。
3. **有界资源：** WPF 日志刷新不得依赖不断提高全局 ThreadPool 最低线程数；并发、pending、线程和队列必须有明确上限。
4. **单一查询真源：** preview/index/page 仍由 `LogQueryService` 定义，不得在 Settings 或测试中复制第二套查询算法。
5. **UI 非阻塞：** Dispatcher 不执行大文件扫描，也不等待后台线程退出。
6. **代际正确性：** 过期查询不得覆盖最新筛选、分页或窗口状态。
7. **门禁真相：** 退出码、TRX、summary JSON 任一缺失、损坏或不一致均失败；随机重跑成功不能抹掉首次失败。

---

## 3. Change Impact Matrix

| 维度 | 影响与控制 |
|---|---|
| Scope | WPF Settings 测试宿主、日志分页调度、.NET 测试编排和结构化证据。 |
| Owners | `WpfApplicationFixture`/`WpfStaTestHost` 拥有测试进程资源；`SettingsLogRefreshPipeline` 拥有页面 refresh lifetime；`LogQueryService` 保持查询语义真源。 |
| Concurrency | 高影响。必须定义 active、pending、cancel、异常观察、Dispose 和 worker stop。 |
| Performance | UI 导航不得被阻塞；分页现有 P95 预算不得回退；线程与队列必须固定上界。 |
| Persistence | 不改业务持久化格式；新增证据只写入带 run id 的 `.work/test-results/wpf-stability/`。 |
| Compatibility | 现有 async API 默认保留；只有证明全部调用者安全后才能删除或收窄。 |
| Failure | 采集失败、TRX 缺失、子进程超时、资源未收敛或首次 flaky 均 fail closed。 |
| Rollback | 每个 Task 单独提交；生产调度分支可独立回滚，不影响诊断与测试隔离成果。 |
| Non-goals | 不重写全部 WPF 测试、不按测试逐个起进程、不引入通用 Scheduler/Manager 框架、不把在线 AI 作为 `Run-Tests.ps1` 必需依赖。 |

---

## 4. 决策树

```text
独立子进程稳定复现并取得堆栈
            |
            +-- worker 主要属于测试钩子/阻塞夹具
            |       -> 只修测试隔离、释放和 cleanup
            |       -> 删除 SetMinThreads(128) 后验收
            |       -> 禁止新增生产 worker
            |
            +-- 无测试钩子的生产等价负载也复现
                    -> 先删除分页外层冗余 Task.Run
                    -> 仍复现才引入 1 active + 1 latest pending 单 worker
                    -> 任一步性能或关闭语义不合格即回滚
```

---

## Task 1：锁定契约、失败真相和对抗审查规则

**Files:**

- Modify: `docs/ARCHITECTURE-CONTRACT.md`
- Modify: `Tests/SmartGuard.Architecture.Tests/ArchitectureContractLoggingPhase4Tests.cs`
- Modify: `Tests/SmartGuard.Tests.ps1`
- Create: `docs/testing/WPF-TEST-ADVERSARIAL-REVIEW.md`

**Consumes:** 本文第 1–4 节。

**Produces:** WPF testhost Owner、不变量、证据格式、对抗审查触发条件和禁止项。

- [x] **Step 1：写架构 RED**

新增契约断言，要求存在以下稳定条款：

- `WPF-TEST-OWNER`：fixture/host 拥有 Window、Dispatcher、hook、timer、controller 和测试创建的后台 flight。
- `WPF-TEST-ISOLATION`：故意阻塞 ThreadPool 的试验必须在子进程；普通 WPF suite 不得修改全局 ThreadPool 容量。
- `WPF-TEST-EVIDENCE`：失败必须保留 exit、TRX、summary、test order、counters、stack/trace 或明确的采集失败原因。
- `WPF-TEST-ADVERSARIAL`：高影响测试基础设施变更必须完成假设、反例、污染、所有权、资源上限、fail-closed 六轴审查。

Run:

```powershell
dotnet test .\Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj --filter "FullyQualifiedName~ArchitectureContractLoggingPhase4Tests"
```

Expected RED: 缺少新契约条款。

- [x] **Step 2：写回唯一契约真源**

更新 `LOG-P4-UI-ASYNC`：只要求“受控后台执行 + Dispatcher apply”，不再把具体 `Task.Run` 写死为架构目标。新增上面四条 `WPF-TEST-*`，并明确在线 LLM 不是自动门禁依赖。

- [x] **Step 3：建立对抗审查规范**

`docs/testing/WPF-TEST-ADVERSARIAL-REVIEW.md` 只定义流程，不复制架构真源。每次触发审查必须记录：

1. 原始主张和可证伪条件；
2. 已确认事实、推测、未知项；
3. 至少一个能推翻方案的反例；
4. 跨测试污染、线程/队列/时间、取消/Dispose、证据完整性检查；
5. `ADOPT`、`REVISE` 或 `BLOCK` 结论及采纳变更；
6. 对应 commit、RED/GREEN 证据路径和 reviewer 类型（自审、独立上下文或外部模型）。

触发器包括修改：`WpfApplicationFixture`、`WpfStaTestHost`、测试项目编排、全局静态 hook、timeout/retry、parallelism、ThreadPool、后台 flight 生命周期或结构化结果格式。

- [x] **Step 4：GREEN 与提交**

Run Task 1 Step 1 命令，再运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester -Path '.\Tests\SmartGuard.Tests.ps1' -FullNameFilter '*WPF*contract*'"
```

如果现有 Pester 版本不支持该筛选参数，使用仓库当前受支持的等价过滤方式并把实际命令记录到证据；禁止把“零匹配测试”当作 PASS。

Commit:

```powershell
git add docs/ARCHITECTURE-CONTRACT.md docs/testing/WPF-TEST-ADVERSARIAL-REVIEW.md Tests/SmartGuard.Architecture.Tests/ArchitectureContractLoggingPhase4Tests.cs Tests/SmartGuard.Tests.ps1
git commit -m "test(wpf): define stability and adversarial review contract"
```

**执行记录（2026-08-23）：** 提交 `12d6405`。架构门禁 RED 为 0/1（缺少 `WPF-TEST-OWNER`），GREEN 为 1/1；Pester `-FullNameFilter` 定向门禁为 1/1，另有 121 项因过滤而 `NotRun`。四个目标文件均通过严格 UTF-8 无 BOM 与 `git diff --check`。独立任务审查结论为 Spec compliant、Task quality Approved；唯一 Minor 是证据报告未显式写静态检查退出码，已在本地报告补为 `exit 0; files 4`。本 Task 只建立契约和治理，未修复或关闭 WPF flaky 根因。

---

## Task 2：建立独立子进程复现和失败现场采集

**Files:**

- Create: `Tests/SmartGuard.Settings.StabilityProbe/SmartGuard.Settings.StabilityProbe.csproj`
- Create: `Tests/SmartGuard.Settings.StabilityProbe/Program.cs`
- Create: `scripts/Invoke-WpfStabilityProbe.ps1`
- Modify: `scripts/Invoke-SmartGuardDotNetTests.ps1`
- Modify: `Tests/SmartGuard.Tests.ps1`

**Consumes:** Task 1 的证据契约。

**Produces:** 不污染常规 testhost 的确定性压力入口和带 run id 的证据目录。

- [ ] **Step 1：先写 runner RED**

Pester 覆盖：

- 子进程非零退出必须传播；
- timeout 后必须终止精确 PID，并标记 `TimedOut=true`；
- 缺失/损坏/零测试 TRX 或 JSON 必须失败；
- 每次输出新目录，不覆盖历史证据；
- 记录随机种子、固定测试顺序、进程 id、commit、开始/结束 UTC、退出码和工具采集状态。

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester -Path '.\Tests\SmartGuard.Tests.ps1' -FullNameFilter '*WPF stability probe*'"
```

Expected RED: probe 与 fail-closed contract 尚不存在。

- [ ] **Step 2：实现最小 probe**

Probe 只做两种模式：

1. `test-contamination`：受控占用 ThreadPool，再执行真实 WPF 日志刷新入口；
2. `production-equivalent`：禁止 test hook 和人为 worker 阻塞，只使用真实日志规模、刷新/筛选/取消序列。

不得复制日志查询算法，不得通过提高 minimum worker、扩大 timeout 或自动 retry 制造绿色。

- [ ] **Step 3：采集时间线**

`Invoke-WpfStabilityProbe.ps1` 在同一失败窗口采集：

- `.trx` 和 stdout/stderr；
- `System.Runtime` counters：thread count、queue length、completed work item、CPU；
- 至少一次 `dotnet-stack` 或 `dotnet-trace`；
- 失败阶段、active/pending/generation、Window/Dispatcher/timer/hook 快照；
- `capture-status.json`，工具不可用或权限不足时明确写 `Unavailable/Blocked`，不得伪造空文件为成功。

- [ ] **Step 4：取得受控 RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WpfStabilityProbe.ps1 -Mode test-contamination -Iterations 5
```

Expected RED:

- 干净子进程 5/5 重现 `paged-queued` 首堵或同等业务条件失败；
- 至少一次堆栈能把主要 blocked worker 归属到具体测试/生产调用路径；
- 若无法满足，Task 2 保持 `BLOCKED`，禁止进入生产调度改造。

- [ ] **Step 5：提交**

```powershell
git add Tests/SmartGuard.Settings.StabilityProbe scripts/Invoke-WpfStabilityProbe.ps1 scripts/Invoke-SmartGuardDotNetTests.ps1 Tests/SmartGuard.Tests.ps1
git commit -m "test(wpf): add isolated starvation evidence probe"
```

---

## Task 3：按堆栈归属修复测试污染

**Files:**

- Modify: `Tests/SmartGuard.Settings.Tests/WpfApplicationFixture.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/WpfStaTestHost.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/WpfStaTestHostTests.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/SettingsLogPageHostLifetimeTests.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/LogWorkbenchPerformanceTests.cs`
- Modify: `src/SmartGuard.Settings/LogView/LogTailReaderTestMetrics.cs`

**Decision gate:** 只有 Task 2 堆栈证明测试钩子、阻塞夹具或未收敛 flight 是主要占用者才执行本 Task。若不是，记录 `NotApplicable` 并进入 Task 4。

- [ ] **Step 1：为每个已归属泄漏写单独 RED**

测试必须通过业务条件证明：即使测试体异常、超时或提前 Dispose，gate 也释放、hook 复位、后台 work 可终止、下一测试可立即刷新。禁止只断言私有字段。

- [ ] **Step 2：最小修复**

- 用 `try/finally` 或 disposable lease 绑定 test hook 和 blocker；
- cleanup 首先发出取消/释放，再等待模块 owner 的具体完成条件；
- 不等待全局 ThreadPool queue 清零；
- 不在 Dispatcher 上同步 join；
- 删除不能证明必要性的 `Thread.Sleep`，改为等待明确 started/released/completed 条件。

- [ ] **Step 3：删除旧容量补丁并加禁止门禁**

从 `WpfApplicationFixture` 删除 `ThreadPool.SetMinThreads(..., 128)`；架构/Pester 门禁禁止 Settings testhost 修改全局 ThreadPool minimum 或 maximum。

- [ ] **Step 4：GREEN**

```powershell
dotnet test .\Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj --filter "Category=WpfUi"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WpfStabilityProbe.ps1 -Mode test-contamination -Iterations 5
```

Expected: 受控阻塞仍能验证取消语义，但不再污染下一测试；无自动 retry。

- [ ] **Step 5：提交**

```powershell
git add Tests/SmartGuard.Settings.Tests src/SmartGuard.Settings/LogView/LogTailReaderTestMetrics.cs
git commit -m "fix(test): contain WPF worker and hook lifetime"
```

---

## Task 4：证明或否证生产调度缺陷

**Files:**

- Modify: `Tests/SmartGuard.Settings.StabilityProbe/Program.cs`
- Modify: `scripts/Invoke-WpfStabilityProbe.ps1`
- Create: `.work/test-results/wpf-stability/<run-id>/production-equivalent-summary.json`（运行产物，不提交）

**Consumes:** Task 3 后已无测试污染的环境。

- [ ] **Step 1：建立真实规模基线**

至少覆盖现有性能测试所用日志规模、自动刷新、手动刷新、快速筛选、分页和取消序列。禁止 `BeforeReadForTests`、`ManualResetEventSlim` 人为阻塞或 ThreadPool 参数修改。

- [ ] **Step 2：运行生产等价压力**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WpfStabilityProbe.ps1 -Mode production-equivalent -Iterations 500
```

记录 P50/P95/P99、CPU、工作集、ThreadPool threads/queue、flight active/pending、最终代际和 late apply。

- [ ] **Step 3：分叉裁决**

- 若 500 次均满足现有性能预算、无首堵且资源归零：确认根因局限于测试污染；禁止 Task 5–6 的生产调度改造。
- 若无钩子负载可重复出现首堵，并有堆栈/时间线归因于分页嵌套调度：保存 RED，进入 Task 5。
- 若失败来自磁盘、Dispatcher、GC、杀毒软件或其他路径：停止，针对新证据重写根因计划，不得沿用 ThreadPool 结论。

Commit 仅提交 probe 逻辑，不提交 `.work` 运行产物：

```powershell
git add Tests/SmartGuard.Settings.StabilityProbe/Program.cs scripts/Invoke-WpfStabilityProbe.ps1
git commit -m "test(wpf): classify production-equivalent scheduling pressure"
```

---

## Task 5：先消除分页外层冗余调度

**Files:**

- Modify: `src/SmartGuard.Settings/SettingsLogRefreshPipeline.cs`
- Modify: `src/SmartGuard.Settings/SettingsLogPageHost.Refresh.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/SettingsLogRefreshPipelineTests.cs`（若不存在，先验证不存在再创建）
- Modify: `Tests/SmartGuard.Settings.Tests/SettingsLogPageHostLifetimeTests.cs`

**Decision gate:** 仅当 Task 4 证明 production-equivalent 首堵且堆栈指向分页嵌套调度时执行。

- [ ] **Step 1：写 RED**

新增可控 executor seam，断言一次 paged flight 只有一个后台调度边界；取消旧代际后最新请求可启动；旧结果不得 apply；Dispatcher 保持响应。

- [ ] **Step 2：最小生产改动**

删除 `StartPagedFlight` 外层冗余 `Task.Run`，让 pipeline 只观察由查询 API 返回的任务；不改变 `LogQueryService` 查询算法、分页语义或现有 async API。

如果删除外层调度会导致任何同步前置扫描在 Dispatcher 执行，立即停止；不得以此方案换取测试绿色。

- [ ] **Step 3：GREEN 与压力复测**

```powershell
dotnet test .\Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~SettingsLogRefreshPipeline|FullyQualifiedName~SettingsLogPageHostLifetime"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WpfStabilityProbe.ps1 -Mode production-equivalent -Iterations 500
```

若仍复现同类首堵，保留证据后进入 Task 6；若已消除，禁止继续增加专用 worker。

- [ ] **Step 4：提交**

```powershell
git add src/SmartGuard.Settings/SettingsLogRefreshPipeline.cs src/SmartGuard.Settings/SettingsLogPageHost.Refresh.cs Tests/SmartGuard.Settings.Tests
git commit -m "fix(settings): remove redundant paged refresh scheduling"
```

---

## Task 6：最后手段——页面拥有的有界单 worker

**Files:**

- Create: `src/SmartGuard.Settings/SettingsLogQueryExecutor.cs`
- Modify: `src/SmartGuard.Settings/SettingsLogPageHost.cs`
- Modify: `src/SmartGuard.Settings/SettingsLogPageHost.Refresh.cs`
- Modify: `src/SmartGuard.Configuration/LogQueryService.cs`
- Create: `Tests/SmartGuard.Settings.Tests/SettingsLogQueryExecutorTests.cs`
- Modify: `Tests/SmartGuard.Configuration.Tests/LogQueryServicePagedQueryTests.cs`（若实际文件名不同，先用 CodeGraph 定位现有分页测试 Owner）
- Modify: `docs/ARCHITECTURE-CONTRACT.md`
- Modify: `Tests/SmartGuard.Architecture.Tests/ArchitectureContractLoggingPhase4Tests.cs`

**Decision gate:** Task 5 后 production-equivalent 仍可重复首堵，且堆栈证明全局 ThreadPool 调度是剩余原因。

- [ ] **Step 1：写资源与顺序 RED**

覆盖：

- 任意时刻 `active <= 1`、`pending <= 1`、页面 worker `<= 1`；
- 新请求取消 obsolete active，并原子替换 pending；
- 被替换 pending 的 Task 必须以 cancellation 完成，不能永远悬挂；
- worker 异常被观察并传播到现有失败 UI；
- CTS 全部释放；Dispose 后拒绝新请求；
- UI Dispose 小于 200 ms，不同步 join；
- 释放受控阻塞后 2 秒内 active/pending 归零且 worker stopped；
- Dispatcher shutdown 后不得 late apply；最终只 apply 最新 generation。

- [ ] **Step 2：建立单一同步 core**

`LogQueryService` 保持唯一 preview/index/page 算法。把当前私有同步实现整理为一个共享 core；现有 async wrapper 继续委托该 core，暂不删除公共 API。禁止在 Settings 复制查询代码。

- [ ] **Step 3：实现最小具体 executor**

`SettingsLogQueryExecutor` 必须是 concrete、internal、页面生命周期懒创建，不建立通用 scheduler abstraction：

- 一个持久 worker，不按请求创建线程；
- 容量严格为 `1 active + 1 latest pending`；
- 页面 Dispose 只发 cancel/complete，不在 UI 线程等待；
- worker 最终停止可观测；
- 对不可中断同步 IO 不承诺立即终止，只在释放/IO 返回后按 2 秒测试预算收敛；真实不可中断超时另报健康状态，不允许 `Thread.Abort`。

- [ ] **Step 4：更新契约门禁**

把 executor Owner、容量、取消、退出、单一 query core 和禁止 per-request thread 写入 `ARCHITECTURE-CONTRACT.md`。架构测试禁止第二套 preview/index/page 实现。

- [ ] **Step 5：GREEN、性能与提交**

```powershell
dotnet test .\Tests\SmartGuard.Configuration.Tests\SmartGuard.Configuration.Tests.csproj --filter "FullyQualifiedName~LogQueryService"
dotnet test .\Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~SettingsLogQueryExecutor|FullyQualifiedName~SettingsLogPageHostLifetime|FullyQualifiedName~LogWorkbenchPerformance"
dotnet test .\Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj --filter "FullyQualifiedName~ArchitectureContractLoggingPhase4Tests"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WpfStabilityProbe.ps1 -Mode production-equivalent -Iterations 500
```

保持现有绝对预算：preview P95 `< 500 ms`、indexed random page P95 `< 250 ms`；同时记录同机同数据 Before/After。CPU、工作集或线程数出现未经解释的回退即停止并回滚本 Task。

```powershell
git add src/SmartGuard.Settings src/SmartGuard.Configuration/LogQueryService.cs Tests/SmartGuard.Settings.Tests Tests/SmartGuard.Configuration.Tests docs/ARCHITECTURE-CONTRACT.md Tests/SmartGuard.Architecture.Tests/ArchitectureContractLoggingPhase4Tests.cs
git commit -m "fix(settings): bound paged query execution lifetime"
```

---

## Task 7：把模块级 quiescence 接入每个 WPF 测试边界

**Files:**

- Modify: `Tests/SmartGuard.Settings.Tests/WpfStaTestHost.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/WpfStaTestHostTests.cs`
- Modify: `src/SmartGuard.Settings/SettingsWindowController.cs`
- Modify: `src/SmartGuard.Settings/SettingsLogPageHost.cs`
- Modify: `src/SmartGuard.Settings/SettingsLogRefreshPipeline.cs`

**Produces:** 无需每条测试手工 `Track(controller)` 才能发现后台资源泄漏的测试边界。

- [ ] **Step 1：写 cleanup RED**

分别注入 Window、timer/hook、refresh、pending executor（若 Task 6 不适用则只覆盖现有 flight）泄漏，断言测试结束必然失败并报告准确 Owner；修复后的下一测试不受污染。

- [ ] **Step 2：以 lease/snapshot 管理 Owner**

每次 `WpfStaTestHost.Run` 开始记录模块资源 baseline，结束时自动：

1. 关闭本次创建的 Window/controller；
2. 取消本次 flight；
3. Pump Dispatcher 到确定条件；
4. 验证 active/pending、timer/hook、Window、late apply 归零；
5. 输出 stage、generation 和资源差量。

不得要求 CLR 全局 ThreadPool active 或 queue 为零，因为它不是本模块拥有的资源。

- [ ] **Step 3：GREEN 与提交**

```powershell
dotnet test .\Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~WpfStaTestHost|Category=WpfUi"
```

```powershell
git add Tests/SmartGuard.Settings.Tests src/SmartGuard.Settings
git commit -m "test(wpf): enforce owner-scoped quiescence"
```

---

## Task 8：执行测试系统对抗审查并封闭门禁

**Files:**

- Modify: `scripts/Invoke-WpfStabilityProbe.ps1`
- Modify: `scripts/Invoke-SmartGuardDotNetTests.ps1`
- Modify: `Tests/SmartGuard.Tests.ps1`
- Create: `.work/test-results/wpf-stability/<run-id>/adversarial-review.json`（运行产物，不提交）

### 8.1 确定性对抗门禁

每次 stability gate 自动运行以下反例，任何一项不能执行均不是 PASS：

1. test body 抛异常后资源仍收敛；
2. blocked read 被取消但延迟返回，禁止 late apply；
3. 新请求连续到达，只保留最新 pending；
4. Dispatcher 正在 shutdown；
5. TRX/summary 缺失或损坏；
6. probe 子进程超时；
7. 随机种子复现与固定顺序复现；
8. 删除 `SetMinThreads(128)` 后仍满足性能和稳定性预算。

这些检查是确定性代码，进入自动 PASS/FAIL。

### 8.2 判断型对抗审查

Task 1–7 每个高影响提交都要有一份审查记录，回答六轴问题并引用实际证据。可以由 Owner 自审，再用独立上下文或外部模型挑战；但外部模型不可用时不得让普通 `Run-Tests.ps1` 离线失败。缺少 required review receipt 时只能阻止“测试系统架构已关闭”的声明，不能伪造自动测试失败原因。

本计划已完成第一轮独立上下文反方审查，采纳项：

- 生产调度改造前增加“无测试钩子的 production-equivalent 复现”门槛；
- 先修测试污染，再尝试删除外层冗余调度，专用 worker 降为最后手段；
- 明确定义 active 取消、pending 原子替换、异常观察、CTS 和 worker 退出；
- 保留 async API 并统一到单一同步 core；
- 把 500 次压力转为可判定的资源和性能阈值。

用户已明确选择跳过跨模型第二意见；这不改变自动门禁，也不得记录为跨模型通过。

### 8.3 连续稳定性验收

1. 删除 `SetMinThreads(128)`；
2. Settings suite 在干净进程连续 10 次通过；
3. 完整 `Run-Tests.ps1` 连续 3 次通过；
4. 全程零 retry，首次失败立即保留并终止该轮；
5. 每轮核对父退出码、TRX 和 summary JSON；任一缺失/不一致失败；
6. 500 次刷新/筛选/取消压力满足：`active <= 1`、`pending <= 1`、页面 worker `<= 1`（若适用）、最终只 apply 最新代际、late apply=0；
7. `Dispose < 200 ms`；释放受控阻塞后 2 秒内模块资源归零；
8. preview P95 `< 500 ms`、indexed random page P95 `< 250 ms`，且同机 Before/After 无未经解释的 CPU、工作集或线程回退。

完整门禁：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1
```

必须逐层报告 Pester、.NET、Tray Integration、Installer Integration 的 Passed/Failed/Skipped/NotRun，以及父退出码和 summary 路径。Tray/Installer/UAC 未真实执行时只能标记 `NOT RUN` 或 `BLOCKED`。

- [ ] **Step 4：提交测试编排收口**

```powershell
git add scripts/Invoke-WpfStabilityProbe.ps1 scripts/Invoke-SmartGuardDotNetTests.ps1 Tests/SmartGuard.Tests.ps1
git commit -m "test(wpf): close adversarial stability gate"
```

---

## 5. 硬停止线

出现任一项立即停止当前实现方向：

1. 没有失败现场堆栈归属，却准备修改生产调度。
2. 只在测试钩子压力下复现，却准备引入生产专用 worker。
3. 失败实际来自 Dispatcher、磁盘、GC 或其他 Owner，却继续按 ThreadPool 根因修复。
4. 需要提高 timeout、自动 retry、恢复 `SetMinThreads` 或放宽业务断言才能 GREEN。
5. 无法定义 active cancel、pending replace、异常观察、CTS/线程 Owner 或退出语义。
6. 新增第二套 preview/index/page 查询算法，或删除仍有调用者的 async API。
7. 每请求创建线程、队列无界、Dispose 后仍接收任务或允许 late apply。
8. 受控 RED 不能稳定复现或 trace 无法归属；此时状态是 `BLOCKED`，不是“可能已修复”。
9. 现有分页性能预算、CPU、工作集或线程上限出现无法解释的回退。
10. 对抗审查提出能推翻方案的未解决反例。

---

## 6. 全项目推广边界

本次只在 WPF 测试系统试点“确定性反例门禁 + 判断型对抗审查记录”。不立即接入整个项目，原因是尚无数据证明收益高于维护成本。

只有满足以下条件才另立计划推广：

1. WPF 试点至少完成一个真实缺陷闭环；
2. 审查确实发现了普通测试未覆盖的阻断问题；
3. review receipt 没有显著增加误报、重复劳动或门禁时长；
4. 能按模块风险触发，而不是所有改动一刀切；
5. 仍保持单 Owner + 自动验证，不引入企业式双审批；
6. 新计划明确哪些模块需要、哪些不需要，不把 LITE 改动过度治理。

个人判断：推广方向值得保留，但现在宣布“整个项目都需要”证据不足。先用 WPF 试点测量缺陷发现率、误报率、审查耗时和逃逸缺陷，再决定推广范围，风险更低。

---

## 7. 回滚

- Task 8 回滚只移除 stability 编排和 receipt 门禁，不删除已有证据。
- Task 7 回滚恢复旧 cleanup，但必须同时把稳定性状态退回 `BLOCKED`。
- Task 6 可整体回滚到 Task 5；不得留下第二套 query core 或孤立 worker。
- Task 5 可整体回滚到 Task 3；诊断 probe 和测试隔离继续保留。
- Task 3 回滚会重新暴露测试污染，不得保留“根治”声明。
- Task 2/1 是诊断与契约基础；除非证据格式本身错误，不随生产实现回滚。
- 任一回滚后，10 次 Settings、3 次 full gate 和真实 UI 结论全部失效，必须重新验证。

---

## 8. 当前结论

| 分类 | 结论 |
|---|---|
| 已确认 | 旧 `SetMinThreads(128)` 是容量补丁；失败发生在 paged work item 未启动；测试进程存在阻塞型 worker 用例和不完整的模块级 quiescence。 |
| 合理推测 | 测试污染与嵌套 ThreadPool 调度共同放大随机首堵，但具体占用者仍需失败堆栈确认。 |
| 个人判断 | 最可能的首要修复是测试隔离和 Owner 级 cleanup，而不是立即增加生产专用线程。 |
| 未验证 | 生产等价负载是否也会饥饿；专用 worker 是否必要；最终连续 10/3 次门禁能否通过。 |

**下一执行入口：Task 1。** 在 Task 2 取得可归属 RED 前，不修改生产调度代码。
