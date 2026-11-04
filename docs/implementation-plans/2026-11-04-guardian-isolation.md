# 守护探测隔离与异常记录 Implementation Plan

> 执行：本会话按 subagent-driven-development 分工实现、逐项审查；所有生产行为改动先保留 RED，再实现 GREEN。完整门禁后由主代理提交，不并行运行会争用真实计划任务/安装状态的测试。

**Goal:** WMI 连接/枚举/方法卡顿不得无限阻塞守护；守护子进程的时间、输出、并发和清理均有界，异常、降级、恢复有可解释日志。

**Architecture:** WMI 物理调用集中在 Engine 自身的短生命周期 `--wmi-probe` 内部子命令，父进程仅通过有截止的进程协议访问；沿用现有 PowerCfgProcessRunner 的通用进程执行入口，不建第二套启动/杀进程实现。GuardianIterationRunner 保持串行决策和唯一状态提交，不超时丢弃整个旧轮次。

**Tech Stack:** .NET 8 / Win32 / System.Management / xUnit / 既有 JSONL LogPipeline。

## Global Constraints

- 手工编辑用 apply_patch，UTF-8 无 BOM；写前读取或确认不存在；不覆盖无关改动。
- WMI 仅一个在途子进程、零排队；请求预算2秒、父端防御上限2.5秒，已确认清理的失败按操作冷却30秒，不让亮度不支持阻断健康电池补充；清理无法确认则禁止继续创建探测子进程。子命令自身在WMI前启动3秒退出守卫，覆盖父进程死亡后不再计时的缺口。
- 每个 stdout/stderr 最多 65536 字符；超限为失败，不把截断结果当成功。取消前不启动新进程；退出、读管道和回收都不得无限等待。
- 子命令先于安装根解析、配置、全局日志、单实例锁和 GuardianLoop 执行；只接受固定命令和 0–100 亮度参数。不得接受任意命令、脚本、路径或输出敏感内容。
- 原生 GetSystemPowerStatus 优先；WMI 失败保留原生字段；不引入旧前台事实或旧配置授权。原子电源切换保证不作扩张。
- 守护内已有进程调用传递取消；目录只用于名称展示，不允许多轮每项 15 秒重试阻塞核心轮次。
- 目录总预算2秒，未确认清理立即取消剩余补全；健康依据是完整语义结果，不能以某个子查询退出0提前宣布恢复。AC/DC每次写入前都重验生命周期。
- 亮度恢复前稳定等待限制0–2000ms；恢复本身最多5次、单次等待0–1000ms、工作预算3000ms，取消后仅给同一执行最多500ms清理证明余量，不接受迟到业务结果；未确认才关闭启动。未读回或跳过返回-1，读回未收敛仍保留实际值；日志/心跳/状态/通知消费同一最终亮度，不伪造目标值或输出-1%。
- 普通日志仍只入既有有界管线。记录操作、失败类型、耗时/预算、退出码、清理确认、取消、冷却、累计抑制次数和恢复；不记录完整路径/窗口标题/原始异常文本。
- 保留配置、名单、前台规则、暂停和人工接管语义；不在桌面验收中改写用户阈值。

## Change Impact Matrix / Critical Module Check

| 领域 | 影响与边界 |
| --- | --- |
| Function / Architecture | WMI 在子进程运行；BrightnessService/BatteryInfoProvider 仍为业务入口；GuardianLoop/Runner 仍拥有仲裁和提交。 |
| Data / Compatibility | 不改配置 schema；进程结果新增可选诊断属性、现有构造兼容；内部协议版本固定，不形成用户 API。 |
| Performance / Resource | 完整轮次通常一次亮度读取，最多一个 WMI 子进程；单次 2s、失败冷却30s；输出 O(65536)，电池原始枚举最多16项。实测正常和故障预算。 |
| Concurrency & Time | 单飞零排队、单调冷却；取消立即撤销后续启动资格；晚到结果不用于授权；已进驱动的动作不能承诺撤销。 |
| Reliability / Recovery | 超时终止并确认退出；未确认关闭探测能力，避免重生风暴；周期继续，原生供电和规则仍可工作；恢复单独记录。 |
| Security / Observability | 固定子命令，无 shell、无提权、无原始路径日志；所有失败分类和恢复进既有事件注册表/频道。 |
| Operations | 保留原安装备份；全量验证后更新本机，校验配置哈希；无远程发布。 |

命中 L0 后台进程/时间/日志模块。稳定不变量：一个执行 Owner、零无限队列；原异常不被日志失败覆盖；超时/取消不得产生成功事件；未确认清理不能继续积累工作。配置/状态本地文件 IO 和低延迟 Win32 前台调用仍是已识别系统边界，本轮不宣称硬实时或解决所有内核/文件系统挂死。

## Task 1: 有界子进程执行

**Files:** `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs`、`PowerCfgCommandResult.cs`、`Tests/SmartGuard.Configuration.Tests/PowerCfgProcessRunnerTests.cs` 及需要的同目录专项测试。

**Interfaces:** 保留 `RunProcessAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken=default)`；结果新增 init 属性 `FailureKind`（none/start-failed/timed-out/cancelled/output-limit/io-failed/cleanup-unconfirmed）、`TerminationConfirmed`、`ElapsedMilliseconds`，旧构造默认保持兼容。

- [x] RED：预取消不得启动；输出洪泛/有限超大输出失败且有界；超时、取消及持有管道的子进程必须在预算内返回并有清理证据。
- [x] GREEN：在既有 Owner 实现有限输出读取、覆盖退出/管道等待的总截止、有界回收、读任务故障观察；结果不得把未确认终止表为成功。
- [x] 运行 `dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter FullyQualifiedName~PowerCfgProcessRunner`，保存 RED/GREEN TRX，交给独立审查。

## Task 2: WMI 进程边界

**Files:** Engine `Program.cs`、Infrastructure 下 `WmiProbeCommand.cs`、`WmiProbeProcess.cs`、`BrightnessService.cs`、`BatteryInfoProvider.cs`；专项 Engine/架构测试。事件注册和 Loop/Runner 集成由 Task 3 拥有。

**Interfaces:** `WmiProbeProcess` 提供 `ReadBrightness(CancellationToken)`、`SetBrightness(int,CancellationToken)`、`ReadBattery(CancellationToken)`；默认共享实例，构造可注入进程执行委托、TimeProvider 和 `Action<WmiProbeObservation>`。后者包含 Operation/FailureKind/ElapsedMilliseconds/BudgetMilliseconds/TerminationConfirmed/ExitCode/SuppressedCount/Recovered；不携带原始命令或路径。

- [x] RED：注入可控的挂住子进程验证截止；busy不创建第二项；冷却抑制；恢复；清理未确认后不再创建；非法/超大协议拒绝；取消时无亮度调用。真实测试子进程不得改写系统亮度。
- [x] GREEN：内部固定 `brightness-read`、`brightness-write <level>`、`battery-read` 协议；WMI 源码只留在子命令；父端通过 Task 1 的统一入口、单飞与冷却控制。取消前后检查，不恢复旧值授权。
- [x] BrightnessService 保留无 token API，增加 token 重载；RestoreWithRetryAsync 在每个副作用前检查 token；BatteryInfoProvider 保留原生优先和旧 API，增加 token/探测实例内部重载。
- [x] 运行专项测试及真实只读 WMI 子命令，保存 RED/GREEN TRX，交给独立审查。

## Task 3: 守护调用与异常日志

**Files:** Runner/Loop、PowerCfgExecutor、PowerPlanCatalogProvider、EventNameRegistry、LogEventChannels、ARCHITECTURE-CONTRACT.md、相关行为/架构测试。

- [x] RED：目录总预算和停止取消；每次亮度恢复前撤销资格；超时/取消/清理失败/恢复各有结构化证据；持续故障合并计数且无逐周期刷屏。
- [x] GREEN：Runner 使用 token-aware 探测/亮度同步；目录经既有异步 Owner 在一个总预算内尝试一次，不执行完整重试链；耗尽预算只降级名称，不能猜动作资格。Loop 注入同一 WMI 实例和日志回调。
- [x] 日志登记 `NativeProbeFailed`、`NativeProbeRecovered`（Recovery 频道），正常降级保留细分类别与不确定副作用；操作取消单独可区分，管线失败按现有回退，不同步写日志。
- [x] 窄回归转绿后整合审查，再运行规定的 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1`；核对四层 summary 与退出码。

## Task 4: 实际验收与提交

- [x] Release构建和安装包生成；故障注入验证过载/重复/取消/恢复；受控资源基线及父进程退出证据已保存。
- [x] 2026-11-05用户重新授权并确认UAC后安装成功：配置哈希未变，候选二进制一致；WPS真实前台匹配、后台180秒观察期到期、返回恢复检测和异常日志落盘/页面展示通过，短时资源采样已保存。见[安装与验收记录](2026-11-05-guardian-install-acceptance.md)。真实5/15分钟物理计划切换、长期/休眠/实际亮度写入仍NOT RUN；前次取消记录作为历史证据保留。
- [x] UTF-8无BOM、文档结构和diff检查；仅本任务文件准备Conventional Commit；不声称绝对不会阻塞。

## 审查修正与阶段证据

独立审查发现并补入同一链路：管道句柄释放、父进程死亡后的子进程期限、按操作冷却/计数、低电量补充不被亮度故障阻断、恢复总预算、AC/DC之间撤销资格、目录补全清理闭锁、语义解析前错误恢复、固定子协议错误码保留、旧告警旁路防刷和未观测亮度展示。均在相应责任Owner修复，不建立第二套解析或日志写入。

- 原始RED：`isolation-architecture-red.trx`、`process-hardening-red.trx`、`wmi-isolation-red-behavior.trx`、`guardian-probes-logging-red.trx`。
- 元数据/清理关闭RED→GREEN：`guardian-probe-metadata-red.trx`→`guardian-probe-metadata-green.trx`，`guardian-cleanup-latch-red.trx`；目录不能伪造退出确认，日志回调失败不改变探测结果。
- 审查RED→GREEN：`guardian-review-red.trx` 4项预期失败→`guardian-review-green.trx` 16项通过；`guardian-warning-rate-red.trx`（12条重复告警）→`guardian-warning-rate-green.trx` 29项通过。
- WMI审查：`wmi-review-fixes-red.trx`、`wmi-review-restore-red.trx`、`wmi-review-child-red.trx`、`wmi-review-budget-red.trx` 保留缺口；最终 `wmi-review-fixes-green.trx` 38/38，均未写实际亮度。
- 管道资源：32次快速命令、禁止GC时原实现增长103个句柄，显式reader所有权后满足48个以内边界（允许运行时等待句柄，排除64个遗留管道句柄）；`.work/guardian-isolation/pipe-review-red-final.trx`→`pipe-review-green-final.trx` 10/10。
- 第一轮全量在Pester156通过后因审查发现真实缺口主动取消，不作为最终完成证据；修正后必须重新运行完整门禁。
- v2审查修正：`wmi-review-v2-red.trx` 3项预期失败→`wmi-review-v2-green.trx` 40/40；取消后的清理证明不再被丢弃，恢复失败不再丢弃真实读数；`final-brightness-red.trx`→`final-brightness-green.trx` 20/20；`tray-brightness-red.trx`→`tray-brightness-green.trx` 4/4。`plan-apply`恢复明确限定为命令执行恢复，真正计划切换成功仍须最终GUID验证。
- 父进程崩溃受控夹具已执行：阻塞线程上的生产退出守卫在父进程被终止后约3116ms使孩子以124退出；`.work/guardian-isolation-acceptance-20261104/crash-guard-v2.result.json`。首轮夹具未提前保留退出查询句柄，虽观察孩子退出但退出码未知，未计通过，v2使用原生只读句柄补足证明。夹具未触碰物理WMI/电源计划；实际驱动挂死组合、长期、休眠、实际亮度恢复仍NOT RUN。
- 收尾失败证据保留：`run-tests-20261104T142613143Z-8c6ff1edb3604a558bded318dcb7591d.summary.json` 有3项旧架构检查失败及1项WPF输入法组合异常（测试宿主中止），后二层未运行；架构修正后 `guardian-final-architecture-green.trx` 344/344，WPF失败项独立复核通过。`run-tests-20261104T143024128Z-ab9f70ba25924c2488afd41dd0c589ce.summary.json` 仅预算日志断言失败，设置619/619；取消与预算耗尽可以各有一条记录，测试改为要求唯一恢复预算事件、最多一条对应亮度操作取消事件，不删除生产日志。
- 最终完整门禁：`run-tests-20261104T143509492Z-e612e5123e65459ab616dfd6811fb435.summary.json`，命令退出0，Pester156、.NET2015、Tray Integration2、Installer Integration9，全部Passed，失败0、跳过0。最后仅加强取消事件数量/归属的测试断言，之后 `guardian-budget-log-race-final-green.trx` 10/10；生产代码未再变更。NU1900（NuGet漏洞数据源无法访问）及既有xUnit1031为构建警告，未当作安全扫描通过证据。
- 本机安装尝试：`.work/guardian-isolation-acceptance-20261104/install.ps1` 在Start-Process提权处被取消（退出1），未启动安装器；`original-install`保留原bin和配置备份。配置SHA256仍为`AB173E29F11E80FF4569CD8BDB22DB9EEE2618B0C0B8768A84B993E1F5374FAF`；已安装Engine DLL为`E013B58CFE253BC697905CB935B0B96A1104F401A45B9EE34D899A316F776850`，候选为`99D1B5EBBE6285DD8176296BC68A06CFDAE3B95AE968C52D3404C804297EB3FB`。未以自动化通过替代新版本真实桌面验收。
