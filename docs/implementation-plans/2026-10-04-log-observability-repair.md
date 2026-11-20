# SmartGuard 日志必要缺口修复执行计划

> **执行说明：** 本文是待执行计划，不是实施完成记录。后续使用 `executing-plans` 技能按任务逐项执行；默认在当前会话顺序推进，每项完成后检查代码与证据。只有用户明确选择多 Agent 方式时，才使用 `subagent-driven-development`。复选框只在相应新鲜证据具备后勾选。

**目标：** 让个人用户能从系统日志判断程序规则何时命中、何时停止干预、计划是否实际应用成功，并使日志故障、配置保存结果和诊断导出可信、有界。

**架构：** 保留 `PolicyEngine`、`GuardianCycleCoordinator`、`GuardianIterationRunner` 的现有决策与执行责任，扩展已有日志事件、写入管线、状态发布和诊断导出。前台轻路径只提交有界内存观测，Engine 主循环负责非阻塞日志入队；文件 IO 留在消费侧。日志健康通过既有 `StatusPublisher` 发布，UI 复用共享 reader 和现有展示区域。

**技术栈：** C#、.NET 8 Windows、WPF/WinForms、System.Threading.Channels、System.Text.Json、xUnit/FluentAssertions、Pester；不增加第三方依赖。

**状态：** 实施中；2026-10-04 已完成 grill-me 自主决策审查与专业计划审查，记录见 §11；新鲜实施证据见[实施记录](2026-10-04-log-observability-repair-progress.md)。阶段测试不表示整套修复或桌面验收完成。编制日期：2026-10-04，Asia/Shanghai。

## 1. 全局约束与证据基线

- 当前编制基线为仓库 `main` / `8e875287`；执行开始时重新核验，不能把此提交当作未来实时状态。
- 审计依据：[日志缺口审计报告](../../.work/audit/logging-20261004/report.md)、[13 个隔离观测场景](../../.work/audit/logging-20261004/run-b2af64fb8cb24b09bb5a5304446895e7/evidence.json)。审计工具的“现状输出”不能直接当成修复后的通过断言。
- 上述 `.work` 证据目前是本机未跟踪资料；跨设备执行时另行携带脱敏副本。本计划已写出根因、正规测试入口与期望行为，不依赖把整套私有审计目录提交到仓库。
- 审计时本机安装为 `1.6.1+ff959f055054224657e2477d48fed172ab24c4d8`，与源码基线不同。后续源码、测试、打包、安装、真实桌面证据必须分别报告。
- 执行入口规则：[AGENTS.md](../../AGENTS.md)、[完整项目规则](../PROJECT-RULES.md)、[架构契约](../ARCHITECTURE-CONTRACT.md)。任何新增共享字段或稳定不变量先补架构门禁。
- 手工源码与文档修改使用 `apply_patch`；目标写入前重读或确认不存在；中文文件严格 UTF-8 无 BOM。不覆盖已有修改或未跟踪资料。
- 行为变化必须 RED → 最小 GREEN → 回归；RED 必须来自预期行为缺失，编译失败、沙箱拒绝和缺包不能冒充功能 RED。
- 不修改名单匹配规则、守护优先级、空闲阈值、电量策略、人工接管期限、安装根、计划任务或 UAC 语义。
- 不新增日志数据库、远程平台、后台日志服务、事件历史队列、第二套配置解析、第二套路径匹配、通用 Manager 或功能开关。
- 同一事实只有一个真源，同一能力只有一个规范语义入口，同一责任只有一个明确拥有者。
- 一轮有意义且可独立验证的改动完成后自动提交；禁止自动改写历史。只暂存本任务文件，使用 Conventional Commits；不自动推送、发布、安装或升级用户正在运行的套件。
- 本计划不采用历史资料目录 `docs/superpowers/plans/`，以免把待执行计划放入契约规定的只读历史区域。

## 2. 最终行为与范围

### 2.1 用户能够回答的问题

1. 程序限制是否启用，Engine 是否已观察到新规则？
2. 当前命中了哪个名单项，何时观察到命中或退出？
3. 当时是允许自动守护、暂停、人工接管还是故障降级？
4. 目标计划与实际计划是否一致；没有切换是因为已符合，还是因为不允许动作？
5. 操作是否成功保存；Engine 是否已经使用；实际动作是否验证成功？
6. 日志是否降级、是否丢失；资料是否完整；诊断包里哪些文件未读到或被截断？

### 2.2 必须明确区分的语义

| 概念 | 含义 | 不能据此声称 |
|---|---|---|
| 规则命中 | 当前观测到的前台满足名单条件 | 已经切换计划、一定高性能 |
| 自动守护允许 | 最终决策允许进入既有自动策略链 | 外部计划今后不会改变 |
| 配置已保存 | 原子提交成功、持久文件已更新 | Engine 已使用、计划已切换 |
| 配置已采用 | Engine 在有效读取后使用这组规则 | 操作系统动作已经成功 |
| 切换成功 | 既有最终观测验证实际 GUID 符合目标 | 后续持续一致、断电持久性 |
| 已入队 | 有界管线已接受事件 | 已落盘、永不丢失 |
| 回退成功 | startup/emergency 接受了记录 | 主日志恢复正常 |

日志时间表示“实际观察或评估时间”；系统仍会合并和节流前台信号，不承诺逐一捕获短暂切换。运行状态在恢复、未知或缺失时必须谨慎展示。

### 2.3 本轮纳入与排除

纳入 F1–F7：规则观测与停工恢复、保存日志顺序、真实落盘统计、非阻塞拥塞路径、诊断容错、异步导出、附加日志保留边界。

正常退出事件与 flush 超时观察纳入管线生命周期任务，因为它们直接影响关闭结果。会话投影重设计、所有历史 AppStarted 命名清理、通用跨轮错误聚合和其他领域重构不纳入；发现阻塞当前修复的情况才扩展，并先更新本计划。

### 2.4 已定位根因与修复优先级

以下是本轮源码与隔离探针证据，不代表已安装版本已经被修复。

| 编号/优先级 | 已定位根因 | 修复入口 |
|---|---|---|
| F1 / 必须 | Runner 仅围绕切换/空闲/心跳记录；轻路径 A→B 提前返回，退出提前更新动作基线 | 现有决策结果增加独立观测 |
| F2 / 必须 | 配置失败早退只发布状态；通用心跳文案缺少最终原因约束 | 详细加载结果、原因变化、最终事实心跳 |
| F3 / 必须 | 两个保存入口先日志后 Save，且修改加载缓存对象 | 先提交副本，再记录成功 |
| F4 / 必须 | 主写/恢复包装忽略 fallback 结果并返回 true；背压拒绝不计数；延迟固定为 0 | 消费结果、累计计数、状态健康 |
| F5 / 必须 | 队列拒绝后在业务线程同步走主写与 fallback | 拒绝即返回，消费侧降级 |
| F6 / 必须 | 配置脱敏解析异常中止整包；UI 同步 GetResult；默认漏应急资料 | 独立资料容错、完整请求、异步所有权 |
| F7 / 必须 | 附加 writer 没有保留策略；导出 ReadAllText 全读 | 统一预算、追加前容量检查、有界尾读 |

这些均属于个人项目也会直接影响判断、响应或磁盘占用的必要问题；没有证据表明需要远程可观测平台或完整审计流水系统。

## 3. 变更影响矩阵与关键模块检查

整体为高影响改动；风险来自共享日志基础设施、状态字段、故障恢复和多进程展示，不因个人项目而降低正确性标准。

| 影响域 | 本次影响 | 必要验证 |
|---|---|---|
| Function | 新增状态变化日志，修正假成功和“计划正常” | 命中/退出/暂停/接管/保存失败/最终计划观测 |
| Architecture | 扩展已登记 Owner；新增小型值类型及观测状态 | 无复制规则、无第二 writer、依赖方向门禁 |
| Data | 新增可选日志属性与健康字段，附加资料截断/保留 | 旧 JSONL/status/manifest 兼容、旧字段缺失、轮转 |
| Performance | 轻路径日志观测、消费侧降级、导出尾读 | 不新增 WMI/PowerCfg；业务线程无文件 IO；规模实测 |
| Resource | 单个观测槽、已有队列、单次导出、有界文件 | 事件突发、队列满、重复点击、关闭取消、磁盘上限 |
| Concurrency & Time | 消费线程计数、主循环投影、取消/退出 | 单调冷却、原子快照、无重复消费、晚到结果丢弃 |
| Reliability | 全部写入失败、配置失效、可选资料无法读取 | 核心守护继续运行、无伪成功、独立资料失败隔离 |
| Recovery | 主写恢复、读配置恢复、中断导出、回滚 | 恢复事件一次、旧 ZIP 保留、资源释放 |
| Compatibility | 老客户端未知健康字段；老日志事件无新属性 | 新 reader 缺失显示未知，老 reader 忽略新增字段 |
| Security | 程序名/路径、异常、诊断附加资料 | 不写窗口标题或命令行；脱敏失败不导出原文 |
| Observability | 明确区分守护资格、动作结果与日志健康 | 实际日志页、状态过期、全部磁盘失败说明 |
| Operations | 自动化与人工验收需要隔离运行根 | 现有测试入口、隔离任务、可恢复的人工验收 |

关键模块 Owner 与不变量：

| 模块 | Owner/入口 | 本次必须保持的不变量 |
|---|---|---|
| 配置 | GuardConfigRepository、ConfigMutationService、SettingsSaveCoordinator | 保存失败保留旧文件；读取失败不授权；保存成功才记录成功；日志失败不能逆转成功保存 |
| 日志 | SmartGuardLogWriter、LogPipeline、LogConsumerRecovery、LogFallbackChain | Engine 日常守护日志调用不等待磁盘；统计真实；批次/文件/队列有界；错误不递归记录自己 |
| 策略 | PolicyEngine、GuardianIterationRunner | 人工接管→暂停→前台准入→既有策略；切前最终复核；切换后最终验证；观察不触发新动作 |
| 状态 | StatusPublisher、StatusJsonReader、TrayDisplayState | Engine 唯一写者；字段可选；只变健康也能写；过期或未知不显示确定健康 |
| 诊断 | DiagnosticBundleExporter、DiagnosticRedactor | 单个可选资料失败不拖垮整包；取消保留旧目标；脱敏失败不泄露原文；UI 不同步等待 |

拟登记 `LOG-INV-NONBLOCKING`、`LOG-INV-TRUTHFUL-WRITE`、`LOG-INV-BOUNDED-SUPPLEMENTAL`、`GUARD-OBS-NO-ACTION`。执行时在 ARCHITECTURE-CONTRACT 中登记同一真源，不为本任务另建规则包。

## 4. 设计决策与成本模型

### 4.1 规则观测：与动作资格分开

`LastForegroundAdmitted` 继续只负责动作资格/基线，不能兼任日志“上次已记录状态”。轻路径会提前修改它，直接比较它会丢掉退出记录。

新增 Engine 私有 `GuardObservationTracker`，由主循环单线程持有，最多保存一个待提交复合观测与一份上次接受的复合观测。每份分别包含准入与完整执行结果及两个 dirty 位；更新某一部分不得覆盖另一部分。它不读取文件、不匹配名单、不查电源；输入完全来自现有策略结果。hook 仍只标 dirty；协调器轻路径计算完成后提交内存观测，主循环在周期处理后按准入→执行结果顺序排出最多两条变化事件并非阻塞入队。

`ForegroundAdmission` 增加可选命中名单索引，由 `PolicyEngine.EvaluateAdmission` 的原匹配循环产出；日志用“名单项 #N + exe 文件名”区分同名程序，不把完整路径做日志标识。索引只在本组规则内有意义，规则采用事件说明规则变化；不声称它是跨版本稳定程序 ID。

```csharp
// 计划新增的签名；默认参数保留现有二参数构造调用。
public readonly record struct ForegroundAdmission(
  bool Admitted,
  GuardDecisionReason Reason,
  int? MatchedRuleIndex = null);
```

完整轮次的观测包含最终复核后的决策、实际计划与动作结果。轻路径仅说明准入变化或命中程序变化，目标/实际计划未知时省略，禁止沿用旧值伪装为新观测。完整轮次后续补充执行结果，不把“允许”写成“已成功”。

同一稳定状态不反复输出；每一部分只有相应事件被接受后才推进 last-accepted。入队被拒绝，保留该部分最新待提交观测，下一既有周期再试，不创建重试 Task、不增加高频唤醒。被用户级别过滤的状态标为已观察但未输出，不无限重试；阈值修改后最多输出一次当前摘要。短暂中间态被合并可以丢弃，日志用最后观测的 UTC 时间，不伪造每个事件都记录了。

**接入位置与失效：** Loop 在 `RunGuardedCycleAsync` 返回后、`WaitForWake` 之前统一排出观测，轻路径的 A→B 即使没有完整轮次也能输出。轻路径只提交事实；不能依赖 Runner 下一轮才发现变化。复合观测携带进程内规则 generation；规则更换、关闭限制、配置失效或暂停/人工接管使旧 pending 的自动执行结果失效，不能随后重试旧“允许/已成功”。每部分独立接受/拒绝，先接受准入后执行入队失败时只重试执行部分。时间字段不参与稳态相等判定，否则每次采样都会制造变化。

### 4.2 日志事件与中文文案

| 事件 | 通道/默认级别 | 主要内容 |
|---|---|---|
| ForegroundAdmissionChanged（新增） | Policy / Info；身份未知状态变化 Warn | 命中/退出/名单项变化、观察时间、程序文件名；不包含执行成功断言 |
| GuardStateChanged（新增） | Policy / Info；配置/探测失效 Warn | 最终来源/原因、目标与实际 GUID、动作结果 |
| ConfigReloaded（复用） | Policy / Info | Engine 已采用规则摘要：开关、名单数量；相同数量换名单也需识别 |
| ConfigMutationLogged（复用） | Policy / Info | 已成功保存的暂停/程序规则变更摘要 |
| ConfigMutationFailed（新增） | Policy / Warn | 保存失败、受控错误类别，不能记录“已开启/已关闭” |
| RecoveryDegraded/RecoveryRestored（复用） | Recovery / Warn、Info | 主日志降级/主日志恢复，带落盘去向和未确认写入计数 |
| LogBackpressureApplied（复用） | Diagnostics / Warn | 单次进入过载与累计丢弃摘要，不为每条丢失再写一条 |
| PowerPlanSwitched/PowerPlanApplyFailed（复用） | 原通道/级别 | 最终实际 GUID、目标 GUID、动作结果 |

事件新增必须进入 EventNameRegistry 与 LogEventChannels；结构化字段使用 properties；不扩大默认表格列。除 Heart 既有例外外，新 Info 事件尊重 LogMinimumLevel；Warn 也尊重用户阈值，健康状态不受日志级别过滤。用户主动设为 Warn 时，看不到普通命中 Info 属于预期。

新增属性采用固定名称：`observedAtUtc`、`matchedRuleIndex`（内部零起始，中文显示 +1）、`programFileName`、`guardSource`、`guardReason`、`targetPlanGuid`、`actualPlanGuid`、`actionResult`。`actionResult` 只允许 `not-attempted`、`already-satisfied`、`verified`、`failed`、`unknown`；无可靠事实的属性省略。既有 `outcome=handled` 登记不改，不把 succeeded/failed 塞入不支持这些值的 LogEventOutcome。

Runner 内部 writeLog 统一迁移为 `Func<GuardConfig?, LogLevel, string, string, IReadOnlyDictionary<string, JsonElement>?, DateTimeOffset?, GuardianLogWriteResult>`，普通事件后两参数显式传 null；配置失效诊断的首参数为 null。它不是公共兼容 API，不保留“生产走新回调、测试走旧回调”的两套语义。GuardianLoop.WriteLog 的配置输入同步可空，增加可选 properties/observedAtUtc；日志路由采用 T5 的有限快照，不用假 GuardConfig 补空。properties 传给 EngineLogEvents.Create；传入观测时间时将结果 `with { Timestamp = observedAtUtc.Value }`，仍进入唯一 WriteLog/EnqueueLog。同步迁移 Loop 与正式 Tests 的构造调用；先在旧接口上保存行为 RED，再改签名，不能以编译失败充当 RED。

重试待发观测保留最初该次观测的 UTC 时间，日志页现有 timestamp 列就能显示真正的观察时间；不能只把 observedAtUtc 放进隐藏属性，而让默认列显示较晚的重试入队时间。事件在稳定快照被新事实替换时才更新观察时间；排序/快照按现有事件时间与源位置规则，延后追加的旧 timestamp 需回归分页与 latest 行为。

普通新增事件内容最多 4 KiB（UTF-8），程序显示名最多 128 个字符，错误类别不带完整配置/路径/命令行；超限字段带截断标记。UTC 保存、UI 本地展示；冷却/超时用单调时间。

文案验收例子：

```text
前台命中名单项 #1：notepad.exe；已满足名单条件
自动守护：目标高性能，当前已符合，无需切换
自动守护：已切换至高性能，最终观测验证成功
前台已退出命中；自动守护停止干预，保留当前计划
配置读取失败：解析错误；自动守护暂停干预
配置读取恢复；已重新评估守护资格
暂停守护设置已保存；等待核心下一轮采用
日志已降级至启动日志；部分记录可能缺失
```

### 4.3 写入结果：按记录计数，按批次恢复

新增 Configuration 值类型 `LogBatchWriteResult`；所有管线消费调用点统一迁移到该结果，不保留另一套布尔成功真源。

```csharp
public readonly record struct LogBatchWriteResult(
  int PrimaryWritten,
  int FallbackWritten,
  int Lost,
  bool PrimaryAttemptFailed)
{
  public int Written => PrimaryWritten + FallbackWritten;
  public bool FullyPersisted => Lost == 0;
  public static LogBatchWriteResult Primary(int count) => new(count, 0, 0, false);
  public static LogBatchWriteResult Failed(int count) => new(0, 0, count, true);
}
```

计数必须非负，总和等于本批数量；参数不满足时测试失败。恢复状态判断主写是否恢复，不因回退成功恢复 Healthy。`WrittenLines` 表示已被某个 writer 成功接受，不宣称强制刷盘或断电不丢；`FailedBatches` 表示存在 Lost 的批次数，另增 `PrimaryFailedBatches`、`FallbackWrittenLines`、`LostLines`、`DroppedByBackpressure`。

这里 `Lost/LostLines` 指“最终仍未获得 writer 成功确认”的记录，UI 用“未确认写入”表述，不能声称磁盘上精确少了这些行。现有 bool writer 在中途 IO 异常时无法确认已追加前缀；按整次未确认处理，允许 fallback 重放。Engine 消费侧新增批次回退使用原 LogEvent 的合法 JSONL，保留 eventId/通道/属性，不给 JSONL 加文本前缀；既有文本调用与旧文件继续兼容。跨文件物理重复仍可能存在，日志页只在自身有界视图/查询快照范围内按 eventId 去重，不承诺跨任意历史文件的全局 exactly-once。本轮不增加 WAL 或逐条 fsync。只有 writer 明确确认的分组才分区计数，不能推测部分成功数。

普通入队失败只计数并返回拒绝；高优先级事件已有崩溃环形缓冲，满时仍不能把同步磁盘 IO 带回业务线程。Engine 日常异常收口也遵守该边界：删除 `RunGuardedCycleAsync` catch 中直写文件的旁路，格式化/级别读取再失败时只产生有界固定诊断事件或计数，不递归。回退仅在消费者中按批次执行，每层最多调用一次批量追加入口；writer 内部有限重试单独计入成本，不把一次入口调用等同一次 OS IO。既有恢复状态机只控制主 sink 重试冷却，改用可注入 TimeProvider 的单调恢复间隔。

`PrimaryFailedBatches` 只在真正尝试主写且失败时增长；冷却期跳过主写用 PrimaryAttemptFailed=false，不制造失败尝试。消费结果必须验证非负与总数等于 batch.Count；非法结果或未隔离 callback 异常按整批未确认计数、标记失败，不能让消费 Task 无健康信号地退出。

延迟指标当前未测量，改为可空/省略，不再填 0。丢弃统计是累计历史，不能把“存在历史丢弃”等同“目前仍处故障”。首次进入降级/恢复尝试写一次转换；全盘不可写时不会递归尝试生成无限失败事件。

### 4.4 健康投影：复用状态文件

新增可选 `StatusPayload.logHealth`，包含登记状态代码、主/回退/丢失/拥塞计数和自身 observedAtUtc。消费线程持有计数，Engine 主循环读取原子快照，在既有完整状态发布时附带；不新增健康文件、Timer、IPC 或独立写者。

拟新增值类型如下；现有 StatusPublisher 没有 camelCase naming policy，因此逐属性显式登记 JSON 名，不能只用 PascalCase record 然后假定自动转换。外层为 `StatusPayload.logHealth` 可空属性，内层计数为非负 long。code 仅登记 `healthy`、`degraded`、`failed`、`unknown`。

```csharp
public sealed record LogHealthStatus(
  [property: JsonPropertyName("code")] string Code,
  [property: JsonPropertyName("observedAtUtc")] DateTimeOffset ObservedAtUtc,
  [property: JsonPropertyName("primaryWrittenLines")] long PrimaryWrittenLines,
  [property: JsonPropertyName("fallbackWrittenLines")] long FallbackWrittenLines,
  [property: JsonPropertyName("lostLines")] long LostLines,
  [property: JsonPropertyName("droppedNormal")] long DroppedNormal,
  [property: JsonPropertyName("droppedHigh")] long DroppedHigh,
  [property: JsonPropertyName("droppedByBackpressure")] long DroppedByBackpressure);
```

新文件引入 `System.Text.Json.Serialization`，不改变全局序列化设置。健康投影优先级：未启动/没有有效采样为 unknown；消费者异常、停止失败或最近批次存在未确认记录为 failed；最近仅回退成功、主 sink 冷却或当前拥塞为 degraded；消费者运行、主写曾确认且当前没有故障/拥塞为 healthy。主写连续失败 3 次只是冷却阈值，全层失败第一次即可显示 failed；主状态机 Failed 但 fallback 确认成功时展示 degraded。

普通队列拒绝或高队列满置当前拥塞，下一既有完整采样只有在两队列均空、消费仍运行且期间没有新增拒绝时清除；未排空时保守保持降级，不另建 50% 阈值或 Timer。恢复仅靠实际主写成功和受控拥塞解除，不清零历史计数。消费侧写入分区以单个不可变快照原子发布，生产者丢弃计数用 Interlocked；不得把跨线程分别读取的字段声称为同一瞬间的强一致全局事务。

StatusPublisher 的健康“有意义变化”只比较 code、首次新增未确认/丢弃异常与消费终止，忽略仅 observedAtUtc 变化。同一健康代码下持续变化的计数随已有守护状态写入或 60 秒 freshness 上限刷新，不强制每 5 秒落盘。payload 写入时携带最新健康快照；字段变更不能推进旧守护观测 timestamp。测试用注入时间与 writer 调用次数验证该成本边界。

旧字段缺失/未知代码/过期均显示“日志状态未知”；异常只在现有日志页状态区域和托盘状态文字增加短提示。健康字段不得改变 guardSource/guardReason；日志健康不等于策略健康。单独健康更新时间不得让旧守护观测看起来更新鲜。状态传播时延受既有完整轮次间隔与外部探测上限约束，不承诺故障通知即时必达。

如果日志与 status.json 同时不可写，系统只能利用既有状态过期/读取失败展示未知，不能保证显示精确最新故障原因；本轮不为这个极端场景另建通道。

### 4.5 文件与导出预算（本计划选择的 V1 默认值，尚未实施）

| 对象 | 预算/规则 |
|---|---|
| 前台事件轻复核/完整评估 | 维持现有至少 1 秒/5 秒节流，不新增电源探测 |
| 观测状态 | 一个 pending + 一个 last-accepted；规则摘要指纹一份，固定长度；不留切换历史 |
| 普通/高优先级队列 | 维持现有 20,000 / 2,000 条；新增事件 ≤4 KiB，不扩大容量 |
| 消费批次/关闭 | 维持 200 条 / 100 ms 与受控关闭默认 3 秒；停止超时不得声称排空成功 |
| startup/emergency/crash/crash-ring 每种文件 | 活动文件 ≤1 MiB，最多 3 个归档，归档保留 7 天；只维护自身前缀文件 |
| 单次附加写批次 | ≤128 KiB，超过仅保留最近可容纳记录并计/标截断；不全量读旧文件 |
| 崩溃紧急写 | 非等待互斥获取、无循环重试的一次写事务；固定数量文件操作；锁不可用即失败；有界环形记录与字节总量；不承诺 OS 文件调用绝不阻塞 |
| 诊断附加日志 | 每种最近 ≤256 KiB，四种合计 ≤1 MiB；活动文件不足时补最近归档，数量仍受 3 限制 |
| 主日志诊断 | JSONL 与配套文本各 ≤512 KiB，合计 ≤1 MiB；同一记录集合同时满足两种序列化预算；RecentWindow 也执行同一预算 |
| 配置/状态诊断读取 | 配置 ≤8 MiB、状态 ≤256 KiB；超限记录 too-large，不全量读取或导出原文 |
| 导出并发 | 一个 Settings 窗口同时最多一个诊断导出；两个入口共享这一窗口内所有权 |
| 导出取消/临时文件 | 全链 CancellationToken；一个临时 ZIP；失败/取消清理临时，原目标保留 |

附加文件预算是本次明确设计值，不是微软推荐值或已有性能结果。轮转在追加前按“旧长度+本批字节数”判断，计入换行和截断标记。结构化事件先裁剪字段再序列化为合法 JSONL，不能剪断 JSON 字节串；超大旧文本单条按 UTF-8 边界处理。崩溃路径不执行目录扫描或日期清理。

日常追加或 Engine 消费侧既有首次启动/5 分钟健康事件触发四类附加文件维护，不新增 Timer；诊断准备也可尽力维护。该维护读取规范路径，不依赖某类文件再次写入，因此无新 crash 的旧文件也能过期清理。正常自维护时每类最多 3 个归档；首次遗留目录整理限定每类最多处理 32 个合法候选，超过报告 maintenance-incomplete，后续既有维护轮次再推进。目录枚举自身仍可能随目录项增多而变慢，必须在消费/导出工作线程执行，不能宣称首轮无论外部目录规模都 O(1)。

4 MiB 是每类维护成功后的活动+归档稳态上限；原子替换期间允许一个 ≤1 MiB 临时文件，即瞬时最多 5 MiB。路径无写权限、旧巨大文件迁移失败或候选超上限时报告未达预算并保留可恢复资料。崩溃线程只尽力限制活动文件，7 天清理不承诺在无人运行/无人导出期间自动发生。

规则摘要使用现有有效配置的规范化路径串流生成固定长度指纹，只有配置实际重新装载时计算，不在每个前台回调遍历/复制名单；`GuardConfigRepository` 仍是读取真源。指纹不进入普通日志，只用于判断同数量换名单，不能授权动作。

### 4.6 成本与性能验证口径

- 轻路径新增工作 O(1) 值比较与小型观测替换；原名单匹配 O(N)，N≤128，算法不复制。
- 完整轮次只补小型格式化与入队；不新增 Win32、WMI、PowerCfg 调用。
- 写入结果/健康计数 O(批次数量)，每批≤200；不扫描旧日志；消费者降级最长受既有单次 writer 边界约束，业务侧不等待它。
- 附加日志尾读 O(预算字节数)，不随文件总长度线性增长；主日志历史诊断仍需按现有查询范围扫描，不能把“输出 512 KiB”说成“不扫描历史”。
- 导出主日志用逐条字节计数和有界最近集合，替代每条重算全部序列化大小；两个 UI 入口都 await，不能 Task.Run 后 GetResult。
- 观测重试最多随既有周期进行一次；丢失只保存计数，不为每次失败创建重试任务或历史列表。

## 5. 文件责任图与新增接口

下列“新增”路径均为计划目标，执行前确认不存在；已有文件必须重新读取。所有路径相对 `D:/Project/SmartGuard/`。

| 任务 | 主要修改 | 允许新增的聚焦文件 | 测试落点 |
|---|---|---|---|
| T1 保存日志真实性 | Configuration/ConfigMutationService.cs、SettingsSaveCoordinator.cs、GuardConfigRepository.cs、EventNameRegistry.cs | 不新增保存服务 | Configuration.Tests/ConfigMutationServiceTests.cs、SettingsSaveCoordinatorTests.cs、GuardConfigRepositoryReadCacheTests.cs |
| T2 真实写入与非阻塞 | Configuration/LogPipeline.cs、LogPipelineDiagnostics.cs、LogPipelineHealth.cs、LogStructuredSink.cs、LogFallbackChain.cs；Engine/Infrastructure/LogConsumerRecovery.cs、LogRecoveryStateMachine.cs；Worker/GuardianLoop.Logging.cs、GuardianLoop.cs、GuardianLoop.Foreground.cs | src/SmartGuard.Configuration/LogBatchWriteResult.cs | 已有 Pipeline/Recovery/StructuredEngine/ForegroundGuard 测试，新增 Engine.Tests/GuardianLoggingFailureTests.cs |
| T3 日志健康展示 | Contracts/StatusModels.cs；Engine/Infrastructure/StatusPublisher.cs；Tray/TrayDisplayState.cs、TrayStatusFormatter.cs；Settings/StatusReader.cs、SettingsLogRefreshPipeline.cs、SettingsLogPageHost.Refresh.cs、LogViewStatusTextBuilder.cs | src/SmartGuard.Contracts/LogHealthStatus.cs | 已有 StatusPublisher/TrayStatus/LogViewStatus 测试；新增 LogHealthStatusCompatibilityTests.cs（Configuration.Tests） |
| T4 程序规则观测 | Engine/Domain/GuardDecision.cs、PolicyEngine.cs；Worker/GuardianCycleCoordinator.cs、GuardianIterationRunner.cs、GuardianLoop.cs、GuardianLoop.Logging.cs；Configuration/EventNameRegistry.cs、LogEventChannels.cs | src/SmartGuard.Engine/Worker/GuardObservationTracker.cs；src/SmartGuard.Engine/Worker/GuardianIterationRunner.Logging.cs | Engine.Tests/GuardRuleLoggingTests.cs（新增）、ForegroundGuardIntegrationTests.cs、PolicyEngineTests.cs |
| T5 停工恢复与心跳 | Configuration/GuardConfigRepository.cs；Engine/Worker/GuardConfigReadResult.cs、GuardianLogMessages.cs、GuardianIterationRunner.Logging.cs；事件登记 | src/SmartGuard.Configuration/GuardConfigLoadResult.cs | GuardConfigRepositoryReliabilityTests.cs、GuardStatusPublishingTests.cs、GuardianLogMessagesTests.cs、GuardRuleLoggingTests.cs |
| T6 附加文件有界与回退可见 | Configuration/SmartGuardLogWriter.cs、LogFallbackChain.cs、CrashLogWriter.cs、EmergencySyncWriter.cs、CrashRingBuffer.cs、GlobalExceptionHandlers.cs、Cli/InstallCommands.cs、LogSourceCatalog.cs；Settings/LogView/LogTailReader.cs；既有崩溃调用点 | src/SmartGuard.Configuration/SupplementalLogPolicy.cs | SmartGuardLogWriterTests.cs、Crash/Emergency/LogSource 测试；新增 SupplementalLogRetentionTests.cs、Settings.LogView.Tests/StartupFallbackVisibilityTests.cs |
| T7 诊断容错 | Configuration/DiagnosticBundleExporter.cs、DiagnosticBundleRequest.cs、DiagnosticRedactor.cs | src/SmartGuard.Configuration/DiagnosticLogTailReader.cs，仅附加日志尾读 | DiagnosticBundleExporterTests.cs；新增 DiagnosticLogTailReaderTests.cs |
| T8 导出异步所有权 | Settings/SettingsDiagnosticExportCoordinator.cs、SettingsLogExportCoordinator.cs、SettingsWindowController.cs、SettingsLogPageHost.cs、SettingsLogPageHost.Refresh.cs | src/SmartGuard.Settings/SettingsDiagnosticExportSession.cs，仅窗口内诊断任务所有权 | SettingsDiagnosticExportCoordinatorTests.cs、SettingsLogPageHostLifetimeTests.cs；新增 SettingsDiagnosticExportLifetimeTests.cs |
| T9 集成与验收 | docs/ARCHITECTURE-CONTRACT.md、现有架构门禁与性能测试 | Tests/SmartGuard.Architecture.Tests/LoggingRepairArchitectureTests.cs；本计划同目录的实施证据索引 | Run-Tests.ps1 全链、桌面路径、Soak、回滚 |

表中省略的项目前缀分别为 `src/SmartGuard.*` 与 `Tests/SmartGuard.*`，各任务下命令使用完整仓库相对路径；不以表格许可为由修改无关文件。

跨任务约定：

```csharp
// T2：全部 LogPipeline 调用点同一轮迁移，避免真假成功语义并存。
LogPipeline(Func<IReadOnlyList<LogEvent>, LogBatchWriteResult> writeBatch,
            LogPipelineOptions? options = null);
LogBatchWriteResult LogConsumerRecovery.ExecuteWithRecovery(
  Func<IReadOnlyList<LogEvent>, bool> tryPrimary,
  IReadOnlyList<LogEvent> batch, string fallbackLogPath,
  bool primaryAvailable = true);
Task<bool> LogPipeline.FlushAsync(CancellationToken cancellationToken = default);
Task<bool> LogPipeline.StopAsync();

// T4：仅 Engine 私有单线程值存储；输入不包含外部窗口标题/命令行。
void GuardObservationTracker.ObserveAdmission(
  ForegroundAdmission admission, string? programFileName, DateTimeOffset observedAtUtc);

// T5：唯一配置加载结果；TryLoad() 继续通过它投影 Config。
GuardConfigLoadResult GuardConfigRepository.TryLoadDetailed();

// T7/T8：维持已有导出规范入口，不新增另一个 exporter。
Task DiagnosticBundleExporter.ExportAsync(
  DiagnosticBundleRequest request,
  IProgress<DiagnosticBundleProgress>? progress = null,
  CancellationToken cancellationToken = default);
```

新增 `GuardConfigLoadResult` 包含 Config、固定枚举 FailureKind，结果不保存异常全文；Missing / ParseFailed / AccessDenied / IoFailed / InvalidData / Unknown 分类。`GuardConfigReadResult` 只投影它，不自行二次读文件。保留老 TryLoad/LoadOrDefault 对正常数据的行为；LoadOrDefault 仍不能授权守护。

## 6. 分阶段执行任务

每个涉及共享字段/入口/稳定不变量的任务，先在 `LoggingRepairArchitectureTests.cs`（首次在相应任务创建）及既有架构门禁写 RED，再进行行为 RED 与最小实现，同一任务提交同步 ARCHITECTURE-CONTRACT。T9 只汇总复跑，不允许把架构门禁与契约推迟到最后。反射/序列化门禁保护公开注册字段与边界，缺字段应形成预期断言失败而非编译失败。

### T0：执行前基线与门禁准备（不修改生产代码）

- [x] 核验当前 Git/工作区；保护本轮审计工具和其他未跟踪资料，记录实际起点 commit。
- [x] 重读上述 Owner、目标测试与架构契约；`.codegraph` 存在时先用 CodeGraph 定位，再窄搜。
- [x] 为本次执行新建 `.work/logging-repair/<run-id>/`，保存 command、exitCode、时间、TRX/summary、环境说明；不要覆盖审计时的证据。
- [x] 运行审计工具确认根因仍存在，随后将期望行为测试放到正规 Tests 项目；不能只修改审计工具输出以宣称修复。
- [x] 运行相关基线测试。若已有产品失败，先区分与本计划相关性；环境失败单独登记，不绕过生产原子写入。

```powershell
git status --short
git rev-parse HEAD
codegraph explore "GuardianIterationRunner GuardianCycleCoordinator LogConsumerRecovery DiagnosticBundleExporter"
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --filter "FullyQualifiedName~GuardStatusPublishingTests|FullyQualifiedName~PlanPublicationConsistencyTests|FullyQualifiedName~StructuredEngineLoggingTests"
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --filter "FullyQualifiedName~ConfigMutationServiceTests|FullyQualifiedName~DiagnosticBundleExporterTests|FullyQualifiedName~LogPipeline"
```

基线未确认前停止生产编辑。包已在本机缓存而网络受限时，仅对本次进程使用已有缓存源；不修改全局 NuGet/DOTNET 配置。若沙箱拒绝 File.Replace，记录环境原因，经授权在沙箱外重跑相同隔离用例，不能把该失败作为功能 RED。

### T1：先消除保存失败后的假成功（F3）

实施记录：本地提交 `51825f77`，保存/缓存/设置目标及回归通过；RED 的具体类型和真实 UI 未验收边界见[实施记录](2026-10-04-log-observability-repair-progress.md)。以下清单保留原验收约束，不把隔离接缝缺失误标为已观察到的系统任务故障。

**接口：** 输入既有 Save/SetPaused；输出成功后 ConfigMutationLogged，失败后 ConfigMutationFailed。保存异常仍向上层原样传播，日志异常不得覆盖它。

**文件：** `src/SmartGuard.Configuration/ConfigMutationService.cs`、`SettingsSaveCoordinator.cs`、`GuardConfigRepository.cs`、`EventNameRegistry.cs`、`LogEventChannels.cs`；对应三个 Configuration.Tests 测试文件；架构门禁与契约同步。

- [ ] RED：用既有 `GuardConfigAtomicFileWriter.BeforeMoveForTests` 注入提交失败，断言正式文件保持旧值、无成功事件、存在失败事件（writer 可用时）、原异常上抛。
- [ ] RED：同一路径中再次 TryLoad 返回旧有效配置；防止 SetPaused 先修改 repository 缓存对象，保存失败后缓存仍 Paused=true。
- [ ] RED：成功保存后日志主路径解析使用成功的新配置；日志写入失败不撤销保存；重复无变化保存不重复写成功日志。
- [ ] RED：Save 已成功而随后 AutoStartService 同步失败，持久化成功事件仍成立；不能进入“配置未提交”分支或回滚配置。反馈明确区分配置与后续系统任务阶段，不改 AutoStart 实际语义。
- [ ] GREEN：对配置副本应用暂停变更，先保存，再记录已保存；失败失效读缓存并记录固定错误类别。两个入口复用 GuardConfigRepository 日志能力，不新增另一套 save owner。
- [ ] GREEN：AppendInfoLog 的尽力边界覆盖配置读取/路径解析/事件构建/写入全部阶段，避免 Save 已提交后日志准备异常造成“保存失败”。失败日志只写“配置未提交，变更未保存”；没有旧文件时不声称保留了旧配置。
- [ ] GREEN：规则开关/名单变化保存成功才写摘要；比较规范化名单内容，而非只比较数量；UI 主题等无关修改不输出规则记录。
- [ ] 回归：未知配置键保留、暂停 UI 仍从 status 展示、人工接管不变、保存失败保留旧文件与缓存。
- [ ] 审查后提交 `fix(logging): record configuration success after persistence`。

顺序实现片段（放在既有方法内，不作为新服务）：

```csharp
try
{
  repository.Save(newConfig);
}
catch
{
  // 在既有日志入口尽力记录保存失败；此调用必须全异常隔离。
  repository.AppendMutationFailureLog(root, fallbackLogPath);
  throw;
}
if (pauseMsg is not null)
  repository.AppendInfoLog(pauseMsg, root, fallbackLogPath);
```

计划新增的 `AppendMutationFailureLog(string root, string? fallbackLogPath)` 归 GuardConfigRepository，只写固定“配置未提交，变更未保存”，内部用已登记 Warn 事件和现有 writer，保证不抛出；该事件采用固定失败类别 save-failed，不承诺未传入的异常分类。更细错误留在现有保存异常反馈中，不能使用配置正文。

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --filter "FullyQualifiedName~ConfigMutationServiceTests|FullyQualifiedName~SettingsSaveCoordinatorTests|FullyQualifiedName~GuardConfigRepositoryReadCacheTests|FullyQualifiedName~GuardConfigRepositoryAtomicWriteTests"
```

**阶段交付：** 保存失败无成功记录；成功保存与动作实际生效用语分开；无新配置写入入口。

### T2：合并修复真实写入结果与非阻塞拥塞（F4/F5）

实施记录：本地提交 `d4e74ab7`，Configuration 431、Engine 293、日志架构 54 项回归通过。三轮预热锁竞争业务 P95 为 0.057–0.887ms；未预热首轮 P95 30ms 超过拟定 20ms，证据保留，性能总体关闭仍待 T9。新增交错 RED 查出水位/丢失发布竞态并修复，详见[实施记录](2026-10-04-log-observability-repair-progress.md)。

**接口：** 消费侧返回 §4.3 的 LogBatchWriteResult；业务侧仅 TryEnqueue，失败返回拒绝并计数。恢复状态与计数均可注入时间/故障，不运行真实守护。

**文件：** §5 T2 列出的既有日志文件、新值类型与正规故障测试；同步迁移仓库内所有 LogPipeline 构造回调和测试。

- [ ] RED：主写失败、startup 成功，PrimaryWritten=0、FallbackWritten=本批条数、Lost=0，状态仍 Degraded，PrimaryFailedBatches 增长。
- [ ] RED：三层全失败，WrittenLines=0、LostLines=本批条数、FailedBatches=1；不能 Healthy。明确确认的分组按记录分区；中途 IO 异常的未知前缀按未确认处理，fallback 复用 eventId，不编造部分成功数。
- [ ] RED：80% 背压提前拒绝也计 DroppedByBackpressure 与 DroppedNormal，恰好一次；高队列满计 DroppedHigh，业务调用不调用 writer。
- [ ] RED：给消费 callback 设置可控阻塞信号，填满高队列；业务 WriteLog 必须在 callback 被释放之前返回。用事件顺序验证无等待，不只依赖易抖动的毫秒断言。
- [ ] RED：关闭超时返回 false、AppStopping 表示正在停止而不是已成功排空；超时信息不再入已停止管线，不被忽略；消费者停止/异常健康可观测。
- [ ] RED：Stop 超时后，仍在执行的消费 Task 继续拥有其锁/信号/CTS；不能提前 Dispose 导致晚到 callback 使用已释放资源；结束后恰好释放一次。主退出流程不得无期限等待该 Task。
- [ ] RED：周期异常收口本身再失败时不调用 SmartGuardLogWriter；普通日志 sink 锁被占用仍不能阻塞该路径。批次回退 JSONL 保留原 eventId/属性，旧文本文件仍可读。
- [ ] RED：重复 1,000 次 Flush 请求不创建 1,000 个内部 waiter；调用取消只取消自身等待。消费者故障的 Task 即使完成，Stop 仍 false；未启动同步 flush 失败不得返回 true。
- [ ] RED：冷却期跳过主写不增加 PrimaryFailedBatches；非法分区结果被计为未确认并健康失败，不静默停消费。
- [ ] GREEN：GuardianLoop.WriteBatchToDisk 只尝试主写，降级与恢复唯一归 LogConsumerRecovery；回退批次由 LogFallbackChain 经 SmartGuardLogWriter 每层一次追加。
- [ ] GREEN：EnqueueLog 删除入队失败时同步主写/回退，保留 crash ring 与计数；所有重试留在消费侧。生命周期事件也受相同入队边界。
- [ ] GREEN：恢复冷却使用 TimeProvider.GetTimestamp/GetElapsedTime；首次降级/恢复汇总至现有事件，避免日志递归、反复重置故障时间或每条回退重复转换。
- [ ] GREEN：消费 Task 完成/故障状态进入健康快照，Flush/Stop 不把消费者异常当排空成功；断言 callback 不能让核心守护线程退出。
- [ ] GREEN：FlushAsync 返回本次范围的确认结果；内部最多保留 active/next 两个合并 generation，每代冻结两优先级队列已接受记录的水位。请求期间的新入队不要求该代追到无限流末尾；后到请求归 next；按每队列 FIFO 处理到其水位后完成。取消等待不取消消费，每个调用以既有 3 秒预算返回 false 或取消，不在管线保存每个调用者的 waiter。
- [ ] GREEN：Stop 关闭新入队后冻结最终水位；只有消费正常完成、本次待处理记录全部确认且未超时才 true。历史 Lost 保留在统计，不伪装清零；消费异常/未确认/超时均 false。FlushOnce 仅作为未启动/已无消费任务时的同步测试或冷路径入口，运行时 Flush 统一交给消费者，禁止与 SingleReader 消费并发。
- [ ] 回归：主写恢复探测、并发快照、重复 Stop/Flush、FIFO 在单优先级内保持、取消与计数总和；不存在无界 flush 等待积压的新增调用。
- [ ] 性能：重复故障锁竞争实验，至少 3 次原版与修改版；记录业务侧 P50/P95/Max、队列/线程/内存及 writer 调用次数。目标为无 IO 等待，故障场景业务日志 P95≤20 ms、Max≤100 ms；这些是拟定门禁，不是已测结果。
- [ ] 审查后提交 `fix(logging): preserve truthful outcomes without blocking producers`。

核心生产顺序：

```text
业务调用 → 级别/去重 → 高级事件入 crash ring → 背压计数或 TryEnqueue → 返回
消费者 → 一次主批写 → 若主失败：一次 startup 批写 → 再一次 emergency 批写
       → 返回真实分区计数 → 更新恢复状态/健康快照
```

首先写下面可在旧签名上运行的 RED（放入 LogConsumerRecoveryTests，沿用其 Batch helper；涉及全局测试 override 的夹具串行运行）：

```csharp
[Fact]
public void All_failed_sinks_must_not_be_counted_as_written()
{
  var root = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  var oldEmergency = LogFallbackChain.EmergencyLogFileOverrideForTests;
  try
  {
    // 文件路径指向已有目录，隔离失败，不触碰真实应急日志。
    LogFallbackChain.EmergencyLogFileOverrideForTests = root;
    var recovery = new LogConsumerRecovery(new LogRecoveryStateMachine());
    var pipeline = new LogPipeline(batch =>
      recovery.ExecuteWithRecovery(_ => false, batch, root));
    pipeline.TryEnqueue(LogPriority.High, Batch("all-failed")[0]).Should().BeTrue();
    pipeline.FlushOnce().Should().BeFalse();
    pipeline.Diagnostics.WrittenLines.Should().Be(0);
    pipeline.Diagnostics.FailedBatches.Should().Be(1);
  }
  finally
  {
    LogFallbackChain.EmergencyLogFileOverrideForTests = oldEmergency;
    Directory.Delete(root, recursive: true);
  }
}
```

预期旧实现 RED：FlushOnce 返回 true，WrittenLines=1。保存此证据后再迁移 callback 返回类型。迁移后的额外计数测试如下；不能把它第一次编译不过当作 RED：

```csharp
[Fact]
public void All_failed_batch_is_not_counted_as_written()
{
  var pipeline = new LogPipeline(batch => LogBatchWriteResult.Failed(batch.Count));
  var item = LogEventFactory.Create(LogSeverity.Warn,
    LogComponentRegistry.Engine, LogChannelRegistry.Diagnostics,
    EventNameRegistry.IterationError, "隔离写入失败");
  pipeline.TryEnqueue(LogPriority.High, item).Should().BeTrue();
  pipeline.FlushOnce().Should().BeFalse();
  pipeline.Diagnostics.WrittenLines.Should().Be(0);
  pipeline.Diagnostics.FailedBatches.Should().Be(1);
  pipeline.Diagnostics.LostLines.Should().Be(1);
}
```

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --filter "FullyQualifiedName~LogPipeline|FullyQualifiedName~StructuredLogPipeline|FullyQualifiedName~LogFallbackChain|FullyQualifiedName~LogBackpressure"
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --filter "FullyQualifiedName~LogConsumerRecovery|FullyQualifiedName~LogRecoveryStateMachine|FullyQualifiedName~StructuredEngineLogging|FullyQualifiedName~GuardianLoggingFailure"
```

**阶段交付：** 拥塞与磁盘故障不造成日常业务侧同步 IO；计数可信；回退成功与主写恢复不混淆。不得先提交删除回退但没有计数/健康证据的中间版本。

### T3：把日志故障送到现有状态与展示链（F4）

实施记录：状态独立时间、异常分区投影、两端短提示及既有单 flight 后台采样已通过目标验证；真实桌面与整个计划尚未验收。缺失健康字段的旧状态现在明确显示未知；守护展示测试提供独立健康夹具，避免混淆两种新鲜度。完整 RED/GREEN 数字见[实施记录](2026-10-04-log-observability-repair-progress.md)。

**接口：** StatusPayload 增加可选 LogHealthStatus；Engine 主循环投影；共享 reader 原样读取。状态来源唯一，UI 不根据日志尾部猜健康。

- [ ] RED：只有日志健康字段变化，StatusPublisher 仍写入；clone/equality 不丢该字段。
- [ ] RED：旧 JSON 缺字段、未知代码、过期/未来不可信时间显示未知；healthy、degraded、failed 不改变守护状态代码。
- [ ] RED：计数更新时健康可刷新，不能刷新旧守护 timestamp 伪造新鲜；暂停、配置不可用与探测失败发布路径也携带日志健康。
- [ ] RED：三层日志全失败但状态路径可写，用户仍能读到失败状态；两者都不可写时 UI 最终显示过期/未知。
- [ ] RED：实际 StatusPublisher JSON 精确包含 logHealth.code/observedAtUtc 等 camelCase 字段，旧 reader 忽略新字段；不能只用另配 naming policy 的测试 serializer。
- [ ] RED：每 5 秒采样仅时间变化不反复写 status；稳定健康下计数更新最多随既有守护变化/60 秒 freshness 发布。第一次全层失败即 failed、仅 fallback 成功为 degraded；拥塞解除后历史丢弃计数保留而状态可恢复。
- [ ] GREEN：新增 Contracts 健康字段与代码登记，消费计数用 Interlocked/不可变快照；主线程组装 status。LogPipelineHealth 可空延迟，不再写假 0 ms。
- [ ] GREEN：TrayDisplayState 相等/克隆、Settings 后台刷新结果、日志页现有状态文字接入；健康异常只增加短提示，不新增面板/常驻列/Timer。
- [ ] 回归：新老 reader 兼容、状态文件大小、状态刷新和托盘文字长度、状态 stale 语义、UIA live region 不因稳定健康反复播报。
- [ ] 审查后提交 `feat(logging): expose logging health through shared status`。

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --filter "FullyQualifiedName~StatusPublisher|FullyQualifiedName~GuardStatusPublishing|FullyQualifiedName~GuardEventsHealthPublishing"
dotnet test Tests/SmartGuard.Tray.Tests/SmartGuard.Tray.Tests.csproj --filter "FullyQualifiedName~TrayStatus|FullyQualifiedName~TrayGuardStatus|FullyQualifiedName~StatusStore"
dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~LogViewStatusTextBuilder|FullyQualifiedName~StatusReader|FullyQualifiedName~SettingsLogNavResponsiveness"
```

**阶段交付：** 写不进主日志时仍可经既有状态链判断降级；极端全盘故障不伪装健康。

### T4：记录程序命中、程序变化与退出（F1）

实施状态：规则观测、独立去重/拒绝、原 UTC timestamp 与统一回调已落地；目标 116/116、Engine 全项目 308/308、日志架构 57/57 通过。三轮 128 项轻路径比较已保留原始证据。T5 的详细加载/缓存指纹依赖已提前落实；真实桌面与 Soak 仍待 T9。详见[实施记录](2026-10-04-log-observability-repair-progress.md)。

**接口：** 输入 PolicyEngine 产出的 ForegroundAdmission/GuardDecision；GuardObservationTracker 只保留有界观测；主循环输出既有结构化事件。完整执行日志提取到 Runner.Logging partial，避免继续扩大 524 行 Runner。

- [ ] RED：进入命中时计划原本正确，仍有一次准入日志与“已符合，无需切换”的最终评估记录，apply 调用为 0。
- [ ] RED：名单 A→B 两者都 admitted，文件名/名单项改变也有观测，不新增 PowerCfg/WMI，不请求额外完整评估。
- [ ] RED：命中→未命中在轻路径修改 LastForegroundAdmitted 后，仍记录退出；保持原计划，不发纠偏通知。
- [ ] RED：同名不同路径 exe 通过领域产出的不同索引区分；大小写等价不产生重复事件；名单顺序或内容变化后新索引只用于新规则。
- [ ] RED：复核期间切走、关闭限制、暂停或失效配置，不能记录过时的“已切换成功”；复用既有最终计划观测测试。
- [ ] RED：重复 1,000 次稳定采样不刷新日志；突发信号合并、入队拒绝保留最新 pending、恢复后下一既有周期可记录最终态，无历史队列增长。
- [ ] RED：同一复合观测的准入已接受、执行被拒绝，只重试执行部分；时间变化不刷屏；generation 更新撤销旧 pending。A→B 的轻路径结束后即经 Loop 排出，无需等待完整 Runner；延迟重试默认 timestamp 列仍显示原观测时间。
- [ ] GREEN：领域匹配循环输出索引；协调器提交内存观测；主循环排出观测再入队，hook 和 light 方法均无直接文件日志；完整轮次提交最终事实。
- [ ] GREEN：首次有效观测记录限制状态，未知→已识别记录恢复；LogMinimumLevel=Warn 时 Info 可抑制但守护行为不变。
- [ ] 回归：人工接管/暂停优先级、空/非法名单跳过查询、待补完整评估、不饿死周期、恢复不追补外部误报。
- [ ] 测量：128 项名单、持续前台事件的事件轻路径 CPU/内存/完整评估次数与基线对比；新事件入队后无新增 WMI/PowerCfg 调用是硬断言。
- [ ] 审查后提交 `feat(logging): record foreground admission transitions`。

```powershell
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --filter "FullyQualifiedName~GuardRuleLogging|FullyQualifiedName~ForegroundGuardIntegration|FullyQualifiedName~GuardRulesAcceptanceRegression|FullyQualifiedName~PolicyEngine|FullyQualifiedName~PlanPublicationConsistency"
```

**阶段交付：** 用户可以查到哪项规则何时被观察到命中/退出；允许守护与切换成功分开记录，稳态不刷屏。

### T5：补停工恢复原因并纠正心跳文案（F2）

实施状态：详细读取、固定错误类别、规则采用/恢复摘要、有限日志快照、首次初始化失败收敛及最终事实心跳已落地。Engine 全项目 327/327、Configuration 435/435、日志架构 60/60 通过；真实 ACL/首次启动、全套集成和 Soak 尚未验收。详见[实施记录](2026-10-04-log-observability-repair-progress.md)。

**接口：** GuardConfigRepository.TryLoadDetailed 为读取结果唯一真源；Runner 消费受控错误类别；GuardianLogMessages 消费最终原因/目标/实际事实，不另推策略。

- [ ] RED：缺文件/非法 JSON/访问拒绝/IO 失败均不授权动作；首次失效 Warn 一次，重复相同失效不刷屏；恢复 Info 一次。
- [ ] RED：身份未知、名单空/非法、未命中、暂停、人工接管有效与到期、电源生命周期阻止分别有可解释变化；正常未命中不是 Warn 风暴。
- [ ] RED：未命中/暂停/配置不可用/探测失败/最终切换失败，心跳不得出现“计划正常”；仅当前 GUID 等于已知目标且最终观测有效时表达“符合目标”。
- [ ] RED：同数量更换名单成功装载，也产生 ConfigReloaded；失败配置不写“已采用”；只改主题不写规则采用摘要。
- [ ] RED：Engine 首次启动就配置不可读，仍有诊断事件/健康状态（路径可写时），没有默认配置授权；最后有效日志设置不可用时使用固定 Warn 诊断策略与规范 startup/emergency 路径。
- [ ] GREEN：原 TryLoad 通过详细结果投影；解析仍在 repository，Engine 不二次读文件；配置指纹只在实际重新装载时计算。
- [ ] GREEN：Tracker 分开处理准入与完整守护结果，心跳使用最终可解释事实；健康/配置/规则错误中文消息简短，异常正文不会进入普通日志。
- [ ] GREEN：保存最后有效日志设置的有界不可变快照，仅用于日志级别/目的地，不保存一份可授权的旧 GuardConfig。配置无效的业务结果始终 unavailable；在未授权状态下输出 Warn 诊断不等同恢复业务。该事件仍非阻塞入既有管线，消费侧尝试规范回退。
- [ ] GREEN：该快照为 Loop 私有值类型，字段固定为 minimumLevel、mainPath、mainRetention、startupPath；所有字段从最后有效配置一次性投影。首次没有有效配置时 mainPath/mainRetention 为 null，minimumLevel=Warn，startupPath 来自 SmartGuardPaths。消费者 primaryAvailable=false 时直接批次回退，不调用主写、不增加主写失败尝试或改变其冷却；结果以实际 fallback/lost 投影健康。原恢复签名的可选参数默认为 true，正常批次行为不变。
- [ ] 回归：HeartbeatSchedule 入队拒绝不推进窗口、恢复只立即尝试一次、时间字段 UTC、本地人工期限不改变、没有默认配置授权。
- [ ] 审查后提交 `fix(logging): explain guard interruption and recovery`。

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --filter "FullyQualifiedName~GuardConfigRepositoryReliability|FullyQualifiedName~GuardConfigRepositoryReadCache|FullyQualifiedName~GuardForegroundRestrictionConfig"
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --filter "FullyQualifiedName~GuardRuleLogging|FullyQualifiedName~GuardianLogMessages|FullyQualifiedName~GuardStatusPublishing|FullyQualifiedName~Heartbeat|FullyQualifiedName~GuardianPowerLifecycle"
```

**阶段交付：** 日志能解释停工及恢复；“计划正常”不再被当作任何轮次通用成功文案。

### T6：约束 startup、emergency、crash 与 crash-ring（F7）

实施状态：四类容量、非等待崩溃写入、独立 32 候选维护与 startup 活动/归档可见已落实。Configuration 446/446、Engine 327/327、最终 LogView 62/62、日志架构 61/61、Settings 13/13 通过。只保存两种当前分页边界，修复有界窗口跨源漏记录与截断首行重复。完整证据见[实施记录](2026-10-04-log-observability-repair-progress.md)，真实磁盘故障和整体性能仍归 T9。

**接口：** `SupplementalLogPolicy` 提供 §4.5 的固定预算；所有追加仍由 SmartGuardLogWriter 承担。CrashLogWriter/EmergencySyncWriter 只负责生成内容和选择已登记路径，不直接拥有第二套追加实现。

- [ ] RED：分别对四种文件跨越 1 MiB 边界，追加后活动文件不超预算，归档不超过 3 份；7 天清理只作用于自身合法归档名。
- [ ] RED：单条超大中文/emoji/异常链、批次超过 128 KiB，按 UTF-8 字节截断且不切开字符；明确标记截断，保留最近资料，不全量读取旧文件。
- [ ] RED：fallback 因字节预算仅保留本批部分事件，FallbackWritten 只计被选择且得到 writer 确认的记录，其余计 Lost；截断摘要本身不算原业务事件成功。不得返回整批成功掩盖容量丢失。
- [ ] RED：崩溃路径遇到互斥占用立即失败，不能等待日常 2 秒锁；不进入 fallback 递归，不在崩溃线程扫描目录。用可控锁与调用顺序验证。
- [ ] RED：已有巨大附加文件首次维护时不读入内存；不能把巨大旧文件移进归档后声称目录已经受控；输出只保留预算尾部，并清除超预算的本类型旧文件。
- [ ] RED：环形缓冲保持既有 500 条容量，同时约束单条 4 KiB、总计 2 MiB；Drain 不制造无界副本，重复崩溃导出仍符合单批预算。
- [ ] RED：startup 轮转后，LogSourceCatalog 能发现合法有界归档；主日志历史查询范围、源身份与偏移去重保持不变。emergency/crash 不被误纳入普通主日志搜索。
- [ ] RED：主日志存在较旧记录，而 startup 有更新的规则事件时，ReadLatest/ReadNewer/查询快照均可见新回退；分页与来源过滤不重复。不能只测“主日志为空”或“startup 比主日志早”。
- [ ] RED：main/startup 含同 eventId 的 JSONL，在当前有界视图中只显示一次；同文本但不同事件仍保留。startup 活动+3归档进入既有源身份/轮转恢复链，旧文本 ID 不冒充相同事件。
- [ ] RED：只有历史 crash/crash-ring 且没有新崩溃，消费侧既有维护/诊断准备仍执行过期清理；首次 >32 候选报告未完成，不进行无限扫描/读取后宣称固定成本。
- [ ] GREEN：新增 SupplementalLogPolicy，只定义固定预算与类型映射；文件名/路径继续来自 SmartGuardPaths 与现有 fallback 真源，禁止复制路径字符串。
- [ ] GREEN：LogFallbackChain 在消费侧选择有界记录集合后写入，以已确认数量构造 T2 的 LogBatchWriteResult；不为统计重新读取文件。
- [ ] GREEN：SmartGuardLogWriter 内部共用编码、互斥与追加；日常附加写执行追加前轮转和自身归档清理；紧急模式尝试非等待锁，只做固定数量文件操作，失败返回 false。
- [ ] GREEN：紧急模式容量不足时，在锁内替换为“截断标记 + 有界最新批次”；不得日常扫描或用非锁定覆盖破坏并发追加。临时文件与原文件同目录，失败保留旧文件并返回失败。
- [ ] GREEN：LogSourceCatalog 增加只读 StartupHistoryOldestFirst（最多活动+3归档），原 StartupFallback 只作为活动文件的计算投影；既有 LogTailReader 的 latest/newer/snapshot/paging 遍历该集合，去除“仅合并早于主日志首条”的过滤。不把 startup 混入 MainHistory，不新增 reader；读取成本维持原分页字节/事件预算。
- [ ] GREEN：CrashLogWriter 的可选主日志追加也用非等待、无循环重试模式；仅在调用者已有 mainRetention 快照且无须轮转时尝试，未知预算/需轮转则跳过主文件，仍尽力写独立 crash。新增可选 `LogRetentionPolicy? mainRetention = null` 参数由既有初始化上下文传递；禁止在崩溃回调重新读配置或截断用户主日志。旧调用可只产出 crash，正式测试明确该行为。
- [ ] 回归：中文无 BOM、归档命名、路径带空格、跨进程锁、只读目录、截断计数、相关测试 override 在 finally 恢复。
- [ ] 审查后提交 `fix(logging): bound supplemental and crash log storage`。

聚焦值类型实现：

```csharp
internal static class SupplementalLogPolicy
{
  internal const int BatchMaxBytes = 128 * 1024;
  internal const int RingEntryMaxBytes = 4 * 1024;
  internal const int RingTotalMaxBytes = 2 * 1024 * 1024;
  internal static readonly LogRetentionPolicy Retention = new(
    MaxBytes: 1024 * 1024,
    RetainDays: 7,
    MaxArchiveFiles: 3,
    MaxDirectoryBytes: 4 * 1024 * 1024);
}
```

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --filter "FullyQualifiedName~SupplementalLogRetention|FullyQualifiedName~SmartGuardLogWriter|FullyQualifiedName~CrashLogWriter|FullyQualifiedName~EmergencySyncWriter|FullyQualifiedName~CrashRingBuffer|FullyQualifiedName~LogSourceCatalog|FullyQualifiedName~LogFallbackChain"
dotnet test Tests/SmartGuard.Settings.LogView.Tests/SmartGuard.Settings.LogView.Tests.csproj --filter "FullyQualifiedName~StartupFallbackVisibility|FullyQualifiedName~LogTailReaderSnapshot"
dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~LogTailReader|FullyQualifiedName~LogWorkbenchPerformance"
```

**阶段交付：** 正常维护完成后每类附加日志不超过 4 MiB；磁盘无法修改时报告失败，不声称容量已经修复。已有巨大文件的迁移仅在日常路径执行，崩溃路径保持固定工作量。

### T7：诊断包容错、资料完整性与有界尾读（F6/F7）

**接口：** 保留 DiagnosticBundleExporter.ExportAsync；DiagnosticBundleRequest 新增 `IncludeCrashRingLog`（默认 false，UI 诊断入口明确 true）。DiagnosticLogTailReader 仅返回附加文本尾部与读取状态，不另建主日志查询入口。

计划的尾读结果和入口：

```csharp
internal enum DiagnosticReadStatus
{
  Included, Missing, Unreadable, TooLarge, RedactionFailed
}
internal sealed record DiagnosticTailReadResult(
  string? Text, DiagnosticReadStatus Status,
  bool Truncated, long? SourceBytes, int IncludedBytes);
internal static DiagnosticTailReadResult ReadTail(
  string path, int maxBytes, CancellationToken cancellationToken);
```

`ReadTail` 为 DiagnosticLogTailReader 的静态方法。每类最多读取 256 KiB 加一个 4 KiB 边界块，活动/归档共用预算，不逐文件重新获得预算。结果只包含完整 UTF-8 行；首尾半行、超过预算的单条和破损字符丢弃并标截断，不能从任意字节开始产生伪造 JSONL 行。IncludedBytes 明确为当前读出文本字节数；manifest 中 includedBytes 另按脱敏后实际 ZIP 内容计算，不混用两个阶段。

- [ ] RED：无效配置 JSON 不再中止整个 ZIP；manifest 记录 config 为 redaction-failed，ZIP 中不存在原配置正文；主日志、有效状态与其他可读文件继续导出。
- [ ] RED：配置缺失/拒绝读取/超过 8 MiB、状态缺失/超过 256 KiB 分别记录原因；异常信息不含完整敏感路径或原正文。
- [ ] RED：四种附加文件默认被 UI 诊断入口请求；缺失不报整包失败；非 UI 调用仍能选择不包含。crash-ring 受相同尾读、脱敏与预算约束。
- [ ] RED：构造 100 MiB 附加文件，每类实际读取量不超过 260 KiB，脱敏后输出不超过 256 KiB；活动文件不足时按最新合法归档补齐，总量不翻倍。特别覆盖超长单行与缺换行文件。
- [ ] RED：RecentWindow、CurrentOperation、主日志尾部三种范围中 JSONL/文本各不超过 512 KiB；换行、脱敏扩张和超大单条均计入，同一记录集合同时满足两个预算；每种最终格式每条只序列化/计字节一次。
- [ ] RED：结构化异常的 Message/StackTrace/Inner、properties 内嵌对象与数组、status 中嵌套 secret/password/token 与用户路径均脱敏；新 fallback JSONL 不能只当普通文本过 RedactText。任一资料脱敏失败不回退原文。
- [ ] RED：追加/轮转同时发生时按快照尽力导出，读取失败在 manifest 单独登记；不要无限重试或锁住生产 writer。
- [ ] RED：脱敏异常、压缩异常、取消及最终替换失败保留原目标 ZIP；临时 ZIP 清理；已存在目标且 OverwriteExisting=false 不改文件。
- [ ] GREEN：每份可选资料独立读取、独立脱敏、独立登记；仅成功脱敏的资料进入 ZIP；不把解析失败变成原文导出。
- [ ] GREEN：扩展既有 DiagnosticRedactor 递归处理 JsonObject/JsonArray 与 LogExceptionInfo，深度受 JSON 既有 64 层约束；配置/状态走结构化脱敏，新 JSONL 先解码为事件再 RedactEvent，旧已识别文本沿既有 RedactText。超深/损坏 JSONL 行省略并登记；不新建通用 Secret 服务，也不承诺识别未知格式中的任意机密。
- [ ] GREEN：manifest 保留 schema v2 既有字段，新增可选 readStatus/truncated/sourceBytes/includedBytes/omittedReason；旧消费者可忽略新字段，不重定义 hash 含义。
- [ ] GREEN：manifest 的 includedBytes/hash 对 ZIP 实际脱敏后内容计算；没有内容时不填伪造 hash；省略原因使用固定代码。
- [ ] GREEN：尾读只消费规范路径和受限归档集合；主日志仍通过 ILogEventSource；最终 ZIP 提交使用同目录原子替换/移动，禁止先删旧 ZIP 再移动新 ZIP。
- [ ] GREEN：progress complete 只在最终提交成功之后报告；提交前检查取消及 OverwriteExisting。目标在检查后被他方创建且未允许覆盖时，Move 不覆盖并失败；允许覆盖的本机同卷目标用 Replace 和可恢复备份，失败保留/恢复旧文件，无法恢复则保留备份并明确报错。提交已成功后清理备份失败不冒充导出失败；网络共享/不支持替换的文件系统失败关闭，不切换为先删再写。
- [ ] 回归：已有 manifest/hash、范围过滤、日志关联、敏感路径脱敏、空包、取消、归档源丢失测试。
- [ ] 审查后提交 `fix(diagnostics): export bounded evidence despite damaged inputs`。

正规 RED 示例（加入 DiagnosticBundleExporterTests，复用该类已存在的 CreateTempDir/WriteMinimalInstallTree/BuildRequest/FakeLogEventSource/ReadEntryText）：

```csharp
[Fact]
public async Task Invalid_config_does_not_abort_redacted_diagnostic_export()
{
  var root = CreateTempDir();
  try
  {
    WriteMinimalInstallTree(root);
    const string damaged = "{ invalid-config-fixture }";
    File.WriteAllText(SmartGuardPaths.ConfigFile(root), damaged, new UTF8Encoding(false));
    var request = BuildRequest(root, Path.Combine(root, "bundle.zip"),
      new FakeLogEventSource(CreateSampleEvents()), DiagnosticBundleScope.FullAvailableHistory);
    await DiagnosticBundleExporter.ExportAsync(request);
    using var zip = ZipFile.OpenRead(request.DestinationZipPath);
    zip.GetEntry("config/SmartGuard.config.json").Should().BeNull();
    using var manifest = JsonDocument.Parse(ReadEntryText(zip, "manifest.json"));
    var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
    var config = files.Single(file =>
      file.GetProperty("path").GetString() == "config/SmartGuard.config.json");
    config.GetProperty("readStatus").GetString().Should().Be("redaction-failed");
    files.Should().Contain(file => file.GetProperty("path").GetString()!.StartsWith("logs/"));
    foreach (var entry in zip.Entries)
      ReadEntryText(zip, entry.FullName).Should().NotContain(damaged);
  }
  finally
  {
    Directory.Delete(root, recursive: true);
  }
}
```

已核对配置规范条目为 `config/SmartGuard.config.json`，状态为 `status/SmartGuard.status.json`；保持现有名称，不为示例修改产品格式。
预期现状 RED 为非法 JSON 抛出并中止 ExportAsync；GREEN 后再扩展 hash 一致、拒绝读取与超限用例。以上测试文件写入只针对新建隔离运行夹具，不是手工源码整文件回写。

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --filter "FullyQualifiedName~DiagnosticBundleExporter|FullyQualifiedName~DiagnosticLogTailReader|FullyQualifiedName~DiagnosticRedactor"
```

**阶段交付：** 坏配置仍能获取脱敏诊断资料；manifest 能解释缺失和截断；主日志与附加日志的读写预算可验证。

### T8：两个诊断入口异步化并明确取消所有权（F6）

T7 实施记录：坏配置/状态独立省略，递归脱敏、主源中途失败降级、512KiB 双格式与 260KiB/256KiB 附加尾读已实现；取消/创建竞态/锁定目标保留旧 ZIP，提交后报告 complete。Configuration 455/455、补充诊断 34/34、架构 30/30 通过；详细 RED 与成本边界见[实施记录](2026-10-04-log-observability-repair-progress.md)。

**接口：** 两个 UI coordinator 改为 `Task` 返回的异步入口。窗口持有一个 SettingsDiagnosticExportSession；日志页和 Settings 入口共享该实例。Session 只管理一项诊断导出、CTS 与关闭状态，不拥有文件解析、压缩或脱敏。

SettingsLogPageHost 新增纯值 getter `internal (string LogPath, string StartupPath) GetDiagnosticPaths()`，返回既有已解析字段；尚无有效日志路径时使用 SmartGuardPaths 的默认诊断路径，不启动日志页、不读配置、不据此授权。about coordinator 消费 `Func<(string LogPath, string StartupPath)> getDiagnosticPaths`，替换当前会初始化页面的源 getter。日志页入口仍捕获原筛选/关联/窗口快照，传给后台独立 reader 的既有查询实现。

计划的所有权接口：

```csharp
internal sealed class SettingsDiagnosticExportSession : IDisposable
{
  public bool IsRunning { get; }
  public DiagnosticExportOrigin? ActiveOrigin { get; }
  public Task RunAsync(DiagnosticExportOrigin origin, Func<CancellationToken, Task> export);
  public void Cancel(DiagnosticExportOrigin? expectedOrigin = null);
  public void Dispose();
}
internal enum DiagnosticExportOrigin { About, Logs }
```

`RunAsync` 在打开保存对话框前取得 busy 所有权，忙时立即拒绝第二次任务；UI 禁用两个导出入口并展示已有进度区。Cancel(expectedOrigin) 仅取消匹配来源的当前任务，窗口 Dispose 取消所有来源；CTS 在任务 finally 后释放。退出 busy 前必须等本次后台工作结束，取消发出后不能立即放行第二次任务。Session 不保存 Window/Dispatcher 对象，由 UI 生命周期判断晚到结果。

- [ ] RED：注入未完成的可控 exporter，点击导出后 Dispatcher 仍可执行导航、取消和关闭；不能依靠真实磁盘速度碰巧快速通过。
- [ ] RED：从未打开日志页时点击“关于→导出”，不调用 EnsureLogViewInitialized、不扫描文件/构造 LogSourceCatalog 于 UI 线程；准备源与导出均在工作线程。取消尚未结束时再点击仍只保留一项任务。
- [ ] RED：两个入口交错点击最多调用一次 exporter；成功、异常、取消都解除 busy；窗口关闭后不能更新控件或重新启用旧按钮。
- [ ] RED：页面切换取消该页面发起的导出；窗口关闭取消任何入口发起的导出；取消只作用本次任务，不取消其他窗口。
- [ ] RED：取消传递到日志查询、尾读、压缩前后与最终提交前；ZIP 尚未提交时保留旧目标；资源最终释放。最终原子提交成功之后报告完成，不能再报“已取消且未改目标”。
- [ ] GREEN：文件选择与控件值捕获在 UI 线程，仅复制有限的路径/版本/筛选 AST/关联键/当前窗口记录；列表复制后不随页面变化。about 入口不再取会初始化日志页的 GetDiagnosticEventSource；在工作线程用既有 LogTailReader 建立本次独立源和 catalog，再 await exporter。
- [ ] GREEN：日志页导出也使用本次独立的既有 reader，不能与刷新共用会更新 catalog/半行缓存的 reader。它仍是同一 LogTailReader 实现；只分离任务资源所有权，没有第二套扫描算法。首次初始化或已销毁页面均不影响 about 导出。
- [ ] GREEN：两个 coordinator 消除 GetAwaiter().GetResult/Wait；仅 UI 事件边界使用 async void，并捕获完成/取消/失败；其他方法返回可测试 Task。
- [ ] GREEN：导出异步期间显示短状态“正在导出…”及取消；异常不弹重复对话框；页面销毁先取消，晚到 progress 与结果均忽略。
- [ ] 回归：键盘操作、焦点返回、可访问名称、高对比度、100%/150%/200% DPI；只检查本次触及的按钮与提示，不重新设计日志页。
- [ ] 审查后提交 `fix(settings): make diagnostic export responsive and cancellable`。

about 入口的完整工作边界（位于 SettingsDiagnosticExportCoordinator 内，session/progress 为窗口注入对象）：

```csharp
await session.RunAsync(DiagnosticExportOrigin.About, async token =>
{
  var destinationPath = _pickDestinationPath(window);
  if (string.IsNullOrWhiteSpace(destinationPath)) return;
  var paths = _getDiagnosticPaths();
  var version = _getAppVersion();
  await Task.Run(async () =>
  {
    token.ThrowIfCancellationRequested();
    var source = new LogTailReader(paths.LogPath, paths.StartupPath);
    var request = new DiagnosticBundleRequest
    {
      Root = _root,
      DestinationZipPath = destinationPath,
      AppVersion = version,
      EventSource = source,
      CatalogSnapshot = source.Catalog,
      Scope = DiagnosticBundleScope.FullAvailableHistory,
      OverwriteExisting = true,
      IncludeStartupLog = true,
      IncludeEmergencyLog = true,
      IncludeCrashLog = true,
      IncludeCrashRingLog = true,
    };
    await DiagnosticBundleExporter.ExportAsync(request, progress, token);
  }, token);
});
```

该 UI 示例以用户通过保存对话框选择目标写入（目标存在时由对话框确认覆盖）为授权，OverwriteExisting=true 仅限该选定目标。直接测试/API 调用沿传入参数，不能统一允许覆盖；未允许覆盖的目标创建竞态仍按 T7 失败关闭。日志页入口采用同一 session/工作边界，origin=Logs，保留点击时复制的筛选/关联/窗口记录。不存在 UI 线程先创建 LogTailReader/catalog 再只搬 ExportAsync 的步骤。

调用结束后的 UI 更新必须同时验证窗口/页面仍存活。`progress` 的 UI 回调也验证该次任务仍为当前任务；窗口关闭时取消但不在 Dispatcher 等工作任务，任务观察异常并自行释放资源。Session 不创建额外 Timer，不因 Task.Run 自动获得 IO 取消能力；同步文件调用只在前后检查 token，不能承诺毫秒级中断 OS 调用。

```powershell
dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~SettingsDiagnosticExport|FullyQualifiedName~SettingsLogPageHostLifetime|FullyQualifiedName~SettingsLogNavResponsiveness|FullyQualifiedName~LogWorkbenchAccessibility"
```

**阶段交付：** 导出期间界面可操作，两入口共享一项任务，关闭不会遗留任务或更新已销毁页面。

### T9：全链集成、真实用户路径与长期运行

T8 实施记录：两个 Task 入口共享窗口 session，纯路径捕获、后台独立 reader、导航/关闭取消和晚到结果隔离已实现。目标 30/30、架构 10/10 通过；headless 测试不代替真实桌面。详见[实施记录](2026-10-04-log-observability-repair-progress.md)。

T9 实施记录：已复跑 13 场景、全量首次门禁、受影响性能、100MiB 等量导出和真实新旧程序集兼容读取；集成复审发现的物理分页边界已按实际 RED 修复。最后代码与测试集合完整 .NET 层 1889/0/0、exit 0；真实计时 2h 隔离 Soak exit 0、停止确认成功、Lost=0，跨日轮转和资源趋势已核对。最近全量入口因 UAC 被取消而 exit 1，Tray BLOCKED、Installer NOT RUN，不能拼接前后结果宣称全绿。2026-10-05 本轮源码与证据已收口；必要桌面/休眠与冷启动性能目标尚未全部关闭，M4 未达成；详见同目录[实施记录](2026-10-04-log-observability-repair-progress.md)，不以自动化替代系统动作或 DPI 验收。

- [x] 汇总复跑各任务已先行补齐的 LoggingRepairArchitectureTests：唯一配置读取/追加/导出入口，前台 hook/light 不直接日志 IO，状态健康只有 Engine 写入；门禁检查依赖边界，不依赖私有方法名或源文本偶然布局。
- [x] 核对各任务已同步的 ARCHITECTURE-CONTRACT：新增字段、事件、所有权、预算与不变量完整登记；只更新受影响章节，PROJECT-RULES 不复制默认数值。
- [x] 复跑 13 个审计场景，附带新正规测试证据；与原证据逐项对比，不覆盖原 evidence.json。
- [x] 执行受影响性能测试与全量测试入口，读取 summary JSON；零用例、异常退出、跳过不能当作通过。
- [ ] 按 §8 执行桌面验收与恢复验证；自动测试通过不能替代本机新版实际日志页证据。
- [x] 进行至少 2 小时隔离运行：稳定前台、周期性切换、间歇写入失败、恢复和一次休眠唤醒；每 5 分钟记录队列、消费任务、内存、句柄、线程与文件容量。无法执行休眠时单独标 NOT RUN。
- [x] 对 2 小时样本检查稳定区间无持续积压、文件符合预算、资源无连续上升趋势；发现趋势先定位，不能仅靠阈值放宽。历史日志保留日期边界用受控时间测试，不等待 7 天。
- [x] 在隔离根验证旧 status/日志与新版 reader、旧 reader 对新增字段的兼容，以及回滚后读取旧配置；不修改真实用户配置结构。
- [x] 提交 `test(logging): verify repair invariants and acceptance evidence`；证据索引只引用可重复检查的文件，不提交敏感诊断内容、机器私有完整路径或大型二进制。

```powershell
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj
dotnet test Tests/SmartGuard.Engine.PerformanceTests/SmartGuard.Engine.PerformanceTests.csproj
dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~LogWorkbenchPerformance|FullyQualifiedName~SettingsLogNavResponsiveness"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1
git diff --check
```

**阶段交付：** 源码/自动化/性能/桌面/长期运行证据分开报告，必要缺口闭环，未运行项保持可见。

## 7. 顺序、里程碑与停止条件

默认顺序：`T0 → T1 → T2 → T3 → T4 → T5 → T6 → T7 → T8 → T9`。T2 的真实性与非阻塞修复一起落地；T6 的预算是 T7 尾读的前置条件；T7 的取消与目标保留是 T8 UI 异步化的前置条件。

| 里程碑 | 达成条件 | 用户可见结果 |
|---|---|---|
| M1：日志可信基础 | T1–T3 正规测试通过，契约同步 | 保存不再假成功，日志故障有独立健康信号 |
| M2：核心诉求 | T4–T5 正规测试通过，性能无回退 | 可查命中/退出/采用与动作结果，停工和恢复可解释 |
| M3：诊断与资源 | T6–T8 正规测试通过 | 附加文件有界，坏配置可导出，界面不被导出阻塞 |
| M4：验收闭环 | T9 必要自动化、桌面与 Soak 门禁通过；任一必要项未运行则 M4 未达成 | 能区分已验证和仍未验证的结果 |

不承诺未经实测的小时/天工期。T2 与 T8 风险最高，应先保留独立可控故障夹具再实现；任何阶段发现未知 writer、配置缓存所有权不清、停工仍静默、字节预算无法解释、或测试被迫迁就实现，停止生产编辑，回到调查并更新计划。

## 8. 验收矩阵

### 8.1 自动化必须闭环的缺口

| 缺口 | 关键场景 | 必须满足的结果 | 对应任务 |
|---|---|---|---|
| F1 程序命中不可见 | 未命中→A→B→退出，原计划已正确 | 每个已观察到的稳定变化有事件；不新增系统探测；稳态无刷屏 | T4 |
| F2 停工与心跳失真 | 配置失败/恢复、身份未知/恢复、暂停/接管 | 原因准确；不授权动作；无泛化“计划正常” | T5 |
| F3 保存失败假成功 | 原子提交故障、缓存再次读取 | 旧配置保留，无成功日志，失败不污染缓存 | T1 |
| F4 写入假成功 | 主成功、回退成功、全失败、消费者异常 | 分区计数可信；降级不等于主写恢复；健康不受日志过滤 | T2/T3 |
| F5 拥塞阻塞主链 | 普通80%阈值、高队列满、消费阻塞 | 拒绝计数一次；业务线程无同步文件 IO | T2 |
| F6 诊断失效/界面阻塞 | 坏配置、可选文件不可读、导出期间关闭 | ZIP 可解释；无原文泄露；UI 可操作；旧目标保留 | T7/T8 |
| F7 附加日志无上限 | 大文件、轮转、超大单条、巨大尾读 | 文件/批次/内存/读取量有界；崩溃线程固定工作量 | T6/T7 |

### 8.2 真实桌面路径（后续执行，当前全部 NOT RUN）

在确认用户同意运行新版及系统动作范围后，用隔离根/测试名单验证；开始前记录实际计划、暂停与人工接管状态，结束恢复。不得自动替换现有安装或用运行旧安装作为新版验收。

1. Info 级别，名单含两项：依次切到 A、B、未命中程序，系统日志显示观察时间、名单项与退出；允许现有节流合并短暂切换，不要求每个按键都产生记录。
2. 命中时计划已经符合目标：记录命中和已符合，不伪造 PowerPlanSwitched；若授权真实切换，核对最终 OS GUID 与日志。
3. 暂停、人工接管、关闭限制，再恢复：日志与托盘/Settings 状态一致，优先级与原行为相同。
4. 保存失败：界面报告失败，日志无“已开启”，重开设置仍旧值；恢复保存后区分“已保存”与“核心已采用”。
5. 隔离主写故障但 status 可写：显示日志降级/失败，守护仍继续；解除故障显示恢复。全盘不可写用隔离故障测试，不对真实安装根改 ACL。
6. 坏配置时导出诊断：ZIP 含有效日志及 manifest 缺失原因，默认补充应急/崩溃资料并脱敏；无无效配置原文。
7. 导出 100 MiB 测试资料时点击导航/取消/关闭，再重开：界面不冻结，无多任务堆积，原 ZIP 与临时资源行为符合 T7/T8。
8. 仅检查本次受影响控件的键盘、焦点、可访问名称、DPI 与高对比度；日志健康稳定时不会反复播报。

每项保存运行版本/根、步骤、观察值、截图或日志摘录与恢复结果；无权限、被取消或机器缺条件分别记 BLOCKED/NOT RUN，不能记 PASS。

## 9. 证据、回滚与完成声明

每个任务记录：测试名、RED 预期/实际失败、GREEN 命令和退出码、通过/失败/跳过数、相关 TRX/summary、修改 diff、审查发现及解决结果。测试 filter 若选择 0 个用例，补正后重跑。

全量汇总必须分别列出 Pester、.NET、Tray Integration、Installer Integration 四部分的通过/失败/跳过数和未运行原因，并对照 `.work/test-results/` 的本次 summary JSON 时间与 run-id。不可用旧 summary 支撑新结果。

回滚单位是上表独立提交；存在后续依赖时逆序回滚。实施前保留当前根与配置证据；共享字段均采用可选增量，不做配置迁移。本轮不删除用户资料，不自动 reset/clean，不改写历史。确需恢复已提交行为时先核验工作区，再生成聚焦 revert 并测试；真实安装回滚另需部署授权。

完成标准：

- [ ] F1–F7 的正规行为测试与受影响回归通过，RED/GREEN 证据可查。
- [ ] Owner 与稳定不变量已登记，新增字段旧数据兼容，只有一套匹配/配置/追加/导出真源。
- [ ] 真实规模基线与修改后结果具备，现有性能门禁不回退；新故障路径没有业务线程 IO 等待。
- [ ] 全量入口退出码与四部分 summary 已核对；真实桌面与 Soak 结果独立列示。
- [ ] 所有 NOT RUN/BLOCKED、观察合并、落盘确认与极端全盘失败的限制明确报告。
- [ ] 提交只含本任务文件；中文 UTF-8 无 BOM、Markdown 链接、git diff --check 已验证。

若只完成 M2，应报告“规则可观测主链已通过相应测试，诊断与资源阶段尚未完成”，不能宣称整轮修复完成。

## 10. 已核对的官方依据

- [.NET Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)：有界队列的 TryWrite 可以立即拒绝；本计划要求业务侧显式处理拒绝，不能以同步磁盘回退抹掉背压。
- [WPF 线程模型](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)：UI Dispatcher 不执行长任务或同步等待；本计划 T8 采用工作任务与 await，并明确 UI 生命周期。
- [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)：前台事件遵循现有 hook 所在线程与回调约束；本计划保持回调只标 dirty。
- [JsonPropertyNameAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonpropertynameattribute)：属性名可显式固定；本计划不改变全局序列化配置。
- [File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace)：采用同目录替换与备份；保留实际失败/恢复证据，不把方法名当作任意文件系统上的断电持久性保证。
- [Task.Run](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.run)：工作任务可承载准备与导出；token 仍须传给实际工作，不把外层 Task.Run 的取消误认成中断正在执行的同步文件调用。

上述链接在编制时核对；实施涉及新增 Windows、文件提交或线程 API 时，执行者仍须核对相应当前官方契约。本文预算、性能目标与文案是项目选择，不冒充官方推荐或已经实现的事实。

## 11. 两轮自主审查与修订记录（2026-10-04）

**审查对象：** 文档初版提交 `a2958a6d`；生产代码仍为本计划编制时的代码，当前修改只有本文。先按 grill-me 逐分支核验决策，再按 code-review-excellence 从正确性、架构、并发、性能、资源、诊断隐私与验收角度复审；两轮均由当前 Agent 自主进行，未派发子 Agent，未要求用户重复确认已明确的偏好。

### 11.1 第一轮：grill-me 决策树审查

原则是“代码能回答的问题先查代码，只向用户抛出必须由用户决定的阻塞项”。下表保留问题与推荐结论，结论已写回正文，不把普通实现选择变成用户问卷。

| 编号 | 自主追问 | 依据与结论 | 回写位置 |
|---|---|---|---|
| G01 | 命中是否就意味着开始自动守护或已切换成功？ | PolicyEngine 保留人工接管/暂停优先级，Runner 保留最终复核；只记录名单事实，最终资格与动作结果分开。轻路径文案不写“允许自动守护” | §2.2、§4.1/4.2、T4 |
| G02 | A→B 不触发完整轮次时，日志在哪里输出？ | Coordinator 同 admitted 会提前返回；在 Loop 的 RunGuardedCycleAsync 后统一排出，不等待 Runner；无额外 PowerCfg/WMI | §4.1、T4 |
| G03 | 配置损坏时没有 config，Warn 怎么写而不授权？ | GuardConfigReadResult 必须 unavailable；只保留有限日志设置，或固定 Warn+规范回退，绝不使用默认/旧配置授权 | T5 |
| G04 | 被拒绝的旧结果能否在规则改变后补发？ | generation 使旧待发执行结论失效；准入/执行独立接受；不留历史事件列表，不补发过时成功 | §4.1、T4 |
| G05 | 健康是否要再建文件、Timer 或日志平台？ | 现有 StatusPublisher/reader 足够；极端日志+status 同时失败显示过期/未知；不追加新通道 | §4.4、T3 |
| G06 | 1 MiB/7天是否是必须由用户拍板的产品决策？ | 是个人项目的可回滚固定预算，不是配置迁移；维持队列与节流，明确稳态/临时/遗留故障边界。性能门禁仍由实测决定 | §4.5/4.6、T6/T9 |
| G07 | 异步导出是否需要第二套扫描器、能否取消所有窗口？ | 使用既有 reader 的单次任务实例，窗口拥有一项 session；来源匹配取消，窗口关闭取消自身；没有全局任务管理器 | T8 |

**第一轮结论：** 没有需要用户拍板的未决阻塞项。程序规则语义、个人项目规模、授权范围与待执行状态均已明确；运行新版/真实系统动作的授权在实施验收时按已授权范围判断，本轮不执行这些动作。

### 11.2 第二轮：专业计划审查

以下是会影响方案正确性或验收可信度的实质问题，已直接修订原章节及对应 RED/GREEN 项。P1/P2 表示实施优先级，不表示这些生产缺陷已经修好；“已解决”仅指计划不再依赖该错误前提。

| 编号/级别 | 审查发现与代码证据 | 已采纳修订 | 任务/状态 |
|---|---|---|---|
| P01 / P1 | StatusPublisher 没有 naming policy；PascalCase record 会违背计划写的 camelCase。全 record equality 又会让每轮 observedAt 触发 IO | 显式 JsonPropertyName；只按健康实质变化立即发布，稳定计数依既有状态/60秒刷新；新增实际 serializer 与 writer 次数测试 | T3；计划已解决 |
| P02 / P1 | 主 sink 的 Failed 重试状态不等同日志整体 failed；历史丢弃也不能永久降级。原方案缺少第一批全失败与拥塞解除定义 | 明确健康优先级、当期拥塞清除条件、主写真实尝试计数、历史计数不清零；分区快照原子发布 | T2/T3；计划已解决 |
| P03 / P1 | 旧 FormatTextFallback 丢失 eventId/通道/属性；LogTailReader.MergeStartupFallback 过滤比主日志新的记录；Catalog 只有单个 startup | Engine 批次回退用合法 JSONL；扩展既有 catalog/reader 覆盖活动+3归档和最新回退；有界去重与新旧文件兼容，不承诺全局 exactly-once | T2/T6；计划已解决 |
| P04 / P1 | RunGuardedCycleAsync catch 仍直写文件；AppendInfoLog 的配置/路径准备在异常隔离之外；两个 Runner 回调会产生测试/生产分叉；默认 Event.Timestamp 取生成时刻会误显示重试时间 | 清除日常异常写入旁路；保存后日志准备也隔离；Runner 统一一个内部结构化回调，观测事件 Timestamp 使用原观测 UTC，不建兼容适配层 | T1/T2/T4；计划已解决 |
| P05 / P1 | LogPipeline 的 flush waiter 为无界队列；Stop 仅检查 Task 是否完成，不能区分 fault 与成功；公开 FlushOnce 可竞争 SingleReader | active/next 两代合并 flush、水位与有限等待；Stop 检查正常完成和本次范围确认；运行时只有消费者读取队列 | T2；计划已解决 |
| P06 / P1 | GetDiagnosticEventSource 会初始化日志页；LogTailReader 构造会建 catalog；共用 reader 有可变目录/半行状态。仅 await Export 仍留 UI IO/竞态 | 纯路径捕获；后台准备独立的既有 reader；busy 包含对话框到任务结束；Cancel 按来源，取消后不立即释放并发槽 | T8；计划已解决 |
| P07 / P1 | DiagnosticRedactor.RedactEvent 没处理 Exception，属性嵌套 JSON 与数组没递归；status 当前原样导出。默认补充 JSONL 只按文本脱敏会漏属性 | 同一 Redactor 处理结构化异常/嵌套/状态，新 JSONL 解码后脱敏；损坏或超深省略，旧已识别文本兼容；限定已知脱敏边界 | T7；计划已解决 |
| P08 / P2 | 直接剪 JSON 字节会造坏行；预算未计换行、文本副本和脱敏扩张；读量与最终 ZIP 大小混为同一值 | 字段级裁剪，完整行尾读；260KiB读量/256KiB输出；JSONL与文本各512KiB；两个阶段计量，最终 hash 对 ZIP 内容 | T6/T7；计划已解决 |
| P09 / P2 | crash 文件只在崩溃写入，不能保证无人写时7天清理；“一次IO”也掩盖事务多步及 OS 可能阻塞；旧归档数量不一定已受控 | 复用现有消费/诊断维护；首轮32候选、未完成显式报告；固定操作无重试；稳态4MiB/瞬时5MiB；未知主日志预算时不截断主文件 | T6；计划已解决 |
| P10 / P1 | 原 exporter 在提交前报告 complete，先删旧ZIP；计划 M4 把“登记阻塞”也当闭环；配置提交与后续 AutoStart 失败可能混为一个结果；T9措辞可能把架构门禁拖到最后 | 提交后才 complete，Replace/备份/覆盖竞态与取消语义明确；AutoStart阶段分开；每阶段先架构RED且契约随提交同步；必要验收未运行则 M4未达成 | T1/T7/T9；计划已解决 |

### 11.3 核验来源与专业结论

本轮先用 CodeGraph 定位，再读当前符号源码。核心核验来源：

- [决策与轻路径协调器](../../src/SmartGuard.Engine/Worker/GuardianCycleCoordinator.cs)、[循环及排出位置](../../src/SmartGuard.Engine/Worker/GuardianLoop.cs)、[异常收口旁路](../../src/SmartGuard.Engine/Worker/GuardianLoop.Foreground.cs)。
- [状态发布](../../src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs)、[状态字段](../../src/SmartGuard.Contracts/StatusModels.cs)、[恢复状态机](../../src/SmartGuard.Engine/Infrastructure/LogRecoveryStateMachine.cs)。
- [消费与关闭](../../src/SmartGuard.Configuration/LogPipeline.cs)、[回退格式](../../src/SmartGuard.Configuration/LogStructuredSink.cs)、[源目录](../../src/SmartGuard.Configuration/LogSourceCatalog.cs)、[分页与回退合并](../../src/SmartGuard.Settings/LogView/LogTailReader.cs)。
- [诊断导出](../../src/SmartGuard.Configuration/DiagnosticBundleExporter.cs)、[脱敏边界](../../src/SmartGuard.Configuration/DiagnosticRedactor.cs)、[about导出准备](../../src/SmartGuard.Settings/SettingsDiagnosticExportCoordinator.cs)、[日志页初始化入口](../../src/SmartGuard.Settings/SettingsLogPageHost.cs)。
- [保存与日志](../../src/SmartGuard.Configuration/GuardConfigRepository.cs)、[保存后系统任务](../../src/SmartGuard.Configuration/SettingsSaveCoordinator.cs)、[紧急同步写](../../src/SmartGuard.Configuration/EmergencySyncWriter.cs)、[崩溃写入](../../src/SmartGuard.Configuration/CrashLogWriter.cs)。

**专业结论：** 原版存在上述实施前必须消除的方案遗漏；正文及任务清单已修订，可进入 T0/TDD 执行。没有仍需用户决定的阻塞项，也没有新增远程平台、持久数据库、独立日志服务或通用任务 Manager。新增读取侧、取消、脱敏与生命周期检查均用于修复现有链路，不能据此扩张到其他业务重构。

**证据等级与仍待验证：** 两轮是源码/官方契约支持的静态计划审查。本轮没有运行生产行为测试、性能实验、新版桌面或 Soak；所有实施复选框保持未勾选。现有示例的 RED 结果仍为后续执行预期，不是本轮测试结果。文档的 UTF-8、链接、结构、接口一致性与 diff 检查在本次提交前执行；自动化/桌面/长期运行以 T0–T9 的新鲜证据为准。

## 12. 实施后的自主复审（T9，2026-10-04 至 2026-10-05）

本节记录实施结果，§11 保留实施前审查的历史语境。重新追问“命中是否冒充动作、故障是否冒充成功、异步是否冒充性能改善、测试是否冒充桌面验收”，结论均以新鲜证据约束：准入与动作结果分开；主写/回退/丢失分区确认；CPU/IO/输出预算分开计量；源码、自动化、可见桌面及真实系统动作分开报告。无需新增用户产品决策，个人项目不引入日志平台或通用任务框架。

专业复审已处理物理分页与显示排序混淆、空页清除另一方向游标、EOF 标志错误、同秒合并漏行及跨来源重复副本越过未读行；均保留实际 RED，并由最后 .NET 1889/0/0 回归验证。合并历史首尾往返的补充测试首次通过，只记回归。日志读取仍使用既有 Owner、扫描器与有界页面/显示窗口，不建立全历史 ID 缓存；其他职责拆到原 Owner 的 partial/已有协调器，没有放宽架构门禁。

首次收口时保留下列验收阻塞；后续进展见 §13，不把已通过的局部证据拼成 M4：

- 最近全量入口的托盘提升被用户取消：Tray BLOCKED、Installer NOT RUN、总 exit 1；最后代码仅独立完整 .NET 层通过，不重复触发 UAC。
- 冷启动满队列首轮 P95/Max 30.0008ms，未达到 P95 20ms 目标；预热后的三轮通过单独列示，不删除冷样本或修改预算。轻路径及导出改善不替代这个未关闭项。
- 完整诊断键盘路径、DPI/高对比度、真实系统动作与休眠唤醒未全部验收。真实 IO 取消/导航/关闭已补充 PASS（1GiB 主输入延长观察窗口），原 100MiB 性能样本保留；隔离文件占用下的真实保存失败/重开/恢复路径已补充 PASS，真实策略未改动。

真实计时 2h 隔离 Soak 已通过（exit 0、停止确认成功、Lost=0），最终计数、24 个样本、资源趋势及预算检查已回填[实施记录](2026-10-04-log-observability-repair-progress.md)。未执行真实休眠唤醒，上述验收阻塞仍保留，M4 未达成。

## 13. 优先验收与追加补修（2026-10-05）

按用户确认优先完成完整集成门禁及真实桌面验收。暂停期间实际切到高性能计划后，旧构建每轮重复 ExternalPlanChange，并声称“下轮纠偏”；真实状态已明确 paused，且没有实际切换。根因是执行基线在暂停时保留，日志却把持续差异当成每轮新变化，并提前承诺动作。执行基线应保持原语义，因此补修只在原 Runner 保存一个可空 GUID 的观测确认状态，日志接受或级别过滤后抑制连续相同观测，拒绝后仍可重试；新 GUID 或外部判定解除后重新记录。通知只描述观测，实际动作由既有执行结果说明。

三项行为断言先实际失败（`paused-external-red-complete.trx`：0/3/0，exit 1），最小实现后转绿；四项边界回归覆盖新变化、回到执行基线、拒绝重试及级别过滤。最终 `paused-external-boundaries.trx` 为 7/0/0、exit 0，不把首次通过的回归称为 RED。每完整轮次 O(1)，新增状态固定为一个 GUID，没有增加探测、IO、队列、历史集合或 Owner；架构契约同步 LOG-INV-EXTERNAL-OBSERVATION。

补修后的完整入口 summary 为 `run-tests-20261005T000335459Z-20e03c4fb80246508a176b79ab2167c2.summary.json`，2026-10-05 08:09:58（北京时间）结束，exit 0：Pester 156/0/0、.NET 1896/0/0、Tray Integration 2/0/0、Installer Integration 9/0/0，无跳过。已核对入口退出码与 summary；此前 1889 项版本的通过及取消记录只保留为历史证据。

真实桌面已观察名单 #1 Settings、#2 Explorer、退出命中，以及真实 PowerCfg 切换后的 verified / 已符合目标后的 already-satisfied。键盘 Ctrl+F、中文筛选、Tab 顺序、Space 激活诊断包、文件对话框 Enter 保存已验收；实际 ZIP 为 2969 字节，含三条当前查询记录，全部文件哈希吻合。100% / 200% DPI 与 Windows 水生对比度主题下已检查诊断入口及取消，随后恢复 200% 和无对比度主题。应用内容仍使用浅色主题，不据此声称完整系统高对比度适配或屏幕阅读器验收通过；小窗口事件列需要横向滚动的既有问题继续单列，不扩大本轮日志补修。

专业复审确认观测去重没有改写执行基线、暂停资格、实际动作或通知投递/冷却，失败准入仍可重新尝试。完整自动化门禁已经关闭；新构建真实暂停复核、真实休眠唤醒及冷启动 P95 目标的最终状态在实施记录中独立登记。集成测试使用临时安装目录，未更新正式安装或发布。M4 不能仅凭完整自动化通过判定达成。

**08:22 追加结论：** 新构建真实暂停恢复已通过。用户手动休眠、唤醒及解锁后，既有生命周期验收脚本 HibernateResume 六项全部 Passed、exit 0，结果见 `.work/logging-repair/20261005-acceptance/hibernate.result.json`。Windows 实际休眠状态已核对，同一 Engine 记录挂起/恢复，挂起期间无计划写入，唤醒后状态及时更新，前后周期健康样本无新增丢弃、失败或丢失。三轮验收会话均已结束，原电源计划、亮度、正式配置及任务基线恢复一致；当前优先的完整门禁及主要真实桌面路径已补齐。冷启动 P95 目标仍未关闭，扩展可访问性未执行路径继续单列，M4 保持未达成；详见[实施记录](2026-10-04-log-observability-repair-progress.md)。

## 14. 冷日志调用预算调查与补修（2026-10-05）

复核后确认原 30.0008ms 是满队列业务日志的首调用样本，不是整机或整个 Engine 的启动耗时。新调查使用同样 2000 项满高队列、实际主日志互斥锁、每轮十条消息，并在独立新进程运行；没有运行 Engine 或真实系统动作。三个 direct 冷首调用为 28.9924–31.0440ms，反射调用对照相近，单独 formatter 为 25.2671–25.9238ms；只提前 formatter 的定位对照首调用为 3.4807–3.7156ms。主要成本在 JSONL 首次反射序列化，而非磁盘等待；定位对照不算冷预算通过。

实际冷预算 RED 为 30.2600ms、exit 1，架构 RED 确认反射序列化仍在首次业务路径。最小补修在原 LogJsonlFormatter 以 Utf8JsonWriter 输出已有固定 schema，不建新 Owner、序列化模型、后台预热、缓存池或队列。兼容测试保持字段顺序、UTC O 时间文本、可选字段省略、中文及控制字符、嵌套异常与 JsonElement 属性；另以实际 RED 保留深度 64 的 JsonException 失败契约。最终格式及并发/恢复边界 9 项通过，架构新旧契约两项通过，均 exit 0。

最终代码的六个无预热新进程，各十条业务日志的首调用/P95/Max 为 7.3819、7.6644、10.4885、8.2928、9.0071、7.7116ms，全部满足原 P95≤20ms、Max≤100ms；满队列拒绝十条、Written/Lost=0、主/回退文件不存在，预算断言六轮 exit 0。保留原失败、定位对照及早一版样本，不排除首调用、不改预算。十条样本的 nearest-rank P95 等于最大值；本机六轮不能泛化为所有设备的总体 P95，也不代表全部启动/CPU/内存性能改善。

第一次完整门禁出现旧测试把纯 JSON 契约绑定到 JsonSerializer.Serialize 名称；Pester 156/0/0、.NET 1899/1/0，Tray/Installer 因上游失败 NOT RUN，exit 1，失败 summary 原样保留。旧门禁改为解析真实输出并核对独立 JSON 对象、原 eventId/message 及无文本前缀/物理换行，不放宽纯 JSON 要求。随后 UAC 取消记录单独保留；用户继续授权后完成同一次四层门禁通过，详细 summary 与退出码已回填[实施记录](2026-10-04-log-observability-repair-progress.md)。共享 Formatter 无 IO 或跨调用可变状态，决策/计划任务/UI 没有改动；此前桌面与休眠仍仅是对应版本证据，不冒称这次再次执行。150% DPI 和已单列的扩展可访问性未运行项保留，M4 不自动改为达成。

**当前回归状态（09:09 追加）：** 最终 summary `run-tests-20261005T005415982Z-1e7399c528d84842b17fd8a560997ab7.summary.json` 于北京时间 09:09:23 返回，命令与 summary 均 exit 0：Pester 156/0/0、.NET 八项目 1900/0/0、Tray Integration 2/0/0、Installer Integration 9/0/0。四层同次通过，无跳过；补修及专业复审记录已写回。前一轮暴露的前台监听测试夹具竞争仅修正就绪同步：从注册委托内部发信号改为消息泵入口，使用异步续体；保留全部健康、回调和线程断言，生产监听逻辑不变，31 项相关回归通过。托盘控制台曾处于“选择”模式暂停输出，解除后正常完成，不记为产品故障；完整转录及生命周期结束已核对。原 UAC 取消和失败证据不覆盖。冷日志预算与本轮完整集成已闭环，150% DPI 及扩展可访问性未运行项仍独立保留。
