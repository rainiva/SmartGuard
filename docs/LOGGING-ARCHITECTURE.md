# 当前日志系统架构与运行流程

核对日期：2026-10-05。源码基线：1.6.3，提交 `5c360860`。本文描述当前实现；属于源码核对与架构说明，不代表本轮重新执行了运行验收。权威约束仍见 [架构契约](ARCHITECTURE-CONTRACT.md)。

阅读顺序：先看总架构，再按需要展开后面的运行流程。实线表示数据或调用，虚线表示状态观测、共享输入或辅助路径。图中的名称对应当前代码，不包含未来规划。

## 1. 完整架构总览

```mermaid
flowchart TB
  subgraph Producers[产生事实：各自进程内]
    GUARD[Engine：规则准入、最终执行结果\n生命周期、心跳、异常]
    CONFIG[GuardConfigRepository\n配置保存事件]
    START[CLI / Engine 启动信息]
    FAULT[全局异常 / WPF 异常回调]
  end
  subgraph EnginePath[Engine 日常日志：异步路径]
    OBS[GuardObservationTracker\n最新事实与独立确认]
    ADMIT[WriteLog / EnqueueLog\n级别过滤、重复聚合、背压]
    PIPE[LogPipeline\n普通队列 + 高优先级队列]
    CONSUMER[单消费者：高优先级先取\n批量处理、附加日志维护]
    RECOVERY[LogConsumerRecovery\n主写重试冷却 / 回退分区确认]
  end
  subgraph Storage[持久化：统一写入 Owner]
    SINK[LogStructuredSink + LogJsonlFormatter]
    WRITER[SmartGuardLogWriter\n命名互斥、追加、轮转、保留]
    MAIN[(配置指定的主日志\n当前文件 + 日期序号归档)]
    FALLBACK[LogFallbackChain]
    STARTLOG[(安装根 startup.log\n当前文件 + 最多 3 归档)]
    EMERGENCY[(临时目录 emergency.log)]
    CRASH[(临时目录 crash.log)]
    RINGFILE[(临时目录 crash-ring.log)]
    RING[CrashRingBuffer\n进程内高优先级快照]
  end
  subgraph Reading[读取、查询、展示：Settings]
    CAT[LogSourceCatalog / LogSourceSnapshot\n主源与启动回退源、身份与固定边界]
    READER[LogTailReader / ILogEventSource\n物理偏移读取 + LogEventDecoder]
    QUERY[LogQueryParser / LogQueryExpression\nLogQueryService / 内存索引]
    HOST[SettingsLogPageHost\nRefreshPipeline / PaginationCoordinator]
    UI[日志表格、详情、搜索、会话 / 操作关联\nPresenter 展示；ViewportController 滚动]
  end
  subgraph Observability[日志自身健康与诊断]
    HEALTH[SampleLogHealth\n已确认写入、丢失、拒绝、拥塞]
    STATUS[StatusPublisher → 状态文件\n共享 Reader / Store]
    HEALTHUI[Tray 提示 / Settings 健康提示]
    EXPORT[共享导出 Session + DiagnosticBundleExporter\n脱敏、字节限额、独立容错、原子提交]
    ZIP[(诊断 ZIP\n日志 JSONL / 文本、配置、状态、manifest)]
  end
  GUARD --> OBS --> ADMIT --> PIPE --> CONSUMER --> RECOVERY
  GUARD --> ADMIT
  ADMIT -->|Warn / Error / Critical，入队前| RING
  RECOVERY --> SINK --> WRITER --> MAIN
  RECOVERY -->|主写失败 / 不可用 / 冷却中| FALLBACK
  CONFIG --> SINK
  CONFIG -->|失败| FALLBACK
  FALLBACK --> WRITER
  WRITER --> STARTLOG
  WRITER --> EMERGENCY
  START --> WRITER
  FAULT -->|CrashLogWriter| WRITER
  FAULT -->|全局回调 FlushToSync| RING
  RING -->|紧急同步写入| WRITER
  WRITER --> CRASH
  WRITER --> RINGFILE
  MAIN --> CAT
  STARTLOG --> CAT
  CAT --> READER --> QUERY --> HOST --> UI
  READER --> HOST
  PIPE -.-> HEALTH --> STATUS --> HEALTHUI
  HEALTH -.->|每 5 分钟健康事件| PIPE
  STATUS -.->|复用后台刷新任务| HOST
  HOST -.->|范围、筛选、独立 reader| EXPORT
  READER -.->|共享接口；导出使用独立实例| EXPORT
  STARTLOG -.-> EXPORT
  EMERGENCY -.-> EXPORT
  CRASH -.-> EXPORT
  RINGFILE -.-> EXPORT
  STATUS -.-> EXPORT
  EXPORT --> ZIP
```

三条边界需要保留：**队列属于 Engine 日常路径，并非所有进程共用的日志服务**；设置页正常读取主日志与启动回退历史，不自动合并全部临时目录日志；日志健康通过独立的状态文件链路展示，因此主日志写不进去时仍有另一条观测路径，但状态文件自身也可能失败。

## 2. 程序规则命中与动作结果如何变成日志

```mermaid
flowchart TD
  A[既有周期读取配置、识别前台窗口] --> B[PolicyEngine.EvaluateAdmission\n在原匹配遍历返回名单索引]
  B --> C[Coordinator / Runner\n观察准入事实与最终执行事实]
  C --> D[GuardObservationTracker\n分别持有最新 pending / accepted]
  D --> E{规则内容变化 / 读取恢复？}
  E -->|是| F[ConfigReloaded\n程序规则已生效摘要]
  E -->|否| G{准入事实变化？}
  F --> G
  G -->|是| H[ForegroundAdmissionChanged\n命中 / 进入观察 / 返回 / 观察结束]
  G -->|否| I{最终执行事实变化？}
  H --> I
  I -->|是| J[GuardStateChanged\n未执行 / 无需切换 / 已复核成功 / 失败 / 未知]
  F --> W[唯一 WriteLog / EnqueueLog]
  H --> W
  J --> W
  W --> R{提交结果}
  R -->|入队成功或用户级别过滤| ACK[独立确认对应事实\n稳定事实不每轮刷屏]
  R -->|背压 / 队列拒绝| RETRY[保留最新事实与首次观测时间\n下个既有周期再尝试]
```

这些事件跳过普通“相同消息”去重，由事实跟踪器负责语义去重；准入和执行分别确认。规则或权限门发生变化、新准入覆盖旧准入时，会淘汰过时执行结果。跟踪器不决定电源动作，也不保存窗口历史。程序事件只记有界文件名和规则索引，不记完整程序路径。

观察期的日志表达：从名单程序切到名单外才进入观察；默认 180 秒，期间不自动执行；返回名单立即恢复检测；到期仍未命中则保留当前电源计划。**命中日志证明准入事实；动作是否执行、是否生效，要看最终结果事件。**

## 3. Engine 写入、背压与故障回退

```mermaid
flowchart TD
  A[WriteLog：捕获不可变日志设置] --> B{达到最低日志级别？}
  B -->|否| FILTER[按用户设置过滤]
  B -->|是| D{允许记录 / 重复聚合放行？}
  D -->|否| SUPPRESS[抑制重复记录]
  D -->|是| EVENT[创建 LogEvent\n身份、观测时间、关联标识、属性]
  EVENT --> HIGH{Warn / Error / Critical？}
  HIGH -->|是| RING[先保存到有界 CrashRingBuffer]
  RING --> HQ[高优先级队列：2,000]
  HIGH -->|否| BP{普通队列达到 80%？}
  BP -->|是| DROP[拒绝并累计背压 / 丢弃计数]
  BP -->|否| NQ[普通队列：20,000]
  HQ --> ACCEPT{TryEnqueue 成功？}
  NQ --> ACCEPT
  ACCEPT -->|否：队列满或停止中| REJECT[累计对应拒绝 / 拥塞\n生产者不回退同步写盘]
  ACCEPT -->|是| READ[单消费者：高队列优先\n每批最多 200，间隔默认 100ms]
  READ --> MAINT[消费侧读取设置\n首批及每 5 分钟维护附加日志]
  MAINT --> AVAILABLE{主路径可用且允许本次重试？}
  AVAILABLE -->|是| PRIMARY[主日志批量 JSONL 写入]
  PRIMARY --> OK{写入确认成功？}
  OK -->|是| P[PrimaryWritten；恢复 Healthy]
  OK -->|否| FAIL[累计主写失败\n连续 3 次进入 Failed，冷却 30 秒]
  FAIL --> START[启动回退：选择有界 JSONL 批次]
  AVAILABLE -->|否| START
  START --> SOK{启动回退确认成功？}
  SOK -->|是| F[FallbackWritten\n预算省略的原事件计 Lost]
  SOK -->|否| EM[临时目录 emergency.log\n零等待紧急写入]
  EM --> EOK{确认成功？}
  EOK -->|是| F
  EOK -->|否| L[Lost：未确认写入任何 sink]
  P --> ACCOUNT[校验每批记录分区\n更新计数与已处理水位]
  F --> ACCOUNT
  L --> ACCOUNT
```

30 秒冷却不是一个额外重试线程：后续批次到来时才检查能否再次尝试主写。没有有效主配置时直接回退，不计为主写失败。高优先级只绕过普通队列的提前背压，仍可能因高队列已满而被拒绝；环形缓冲也不保证进程被强杀时能够落盘。

消费者回退保留规范 JSONL、事件身份与属性；同步的单事件辅助回退可能写带 `LOG-FALLBACK` / `LOG-EMERGENCY` 标记的文本。读端通过兼容解码统一为事件。

## 4. 文件追加、轮转与保留

```mermaid
flowchart TD
  A[SmartGuardLogWriter 接收行 / 批次] --> KIND{写入种类}
  KIND -->|配置主日志| LOCK[命名 Mutex：最多等待 2 秒]
  LOCK --> GOT{取得互斥？}
  GOT -->|否| FAIL[返回失败，交给调用方处理]
  GOT -->|是| ROTATE{现有文件跨日期\n或已超过大小阈值？}
  ROTATE -->|是| ARCH[移动为 日期 + 序号 .bak]
  ROTATE -->|否| PRUNE[清理过期归档\n约束归档数量与目录预算]
  ARCH --> PRUNE
  PRUNE --> APPEND[UTF-8 无 BOM，共享读取追加\nIOException 最多 3 次，每次等待 20ms]
  APPEND --> RESULT[返回确认成功 / 失败，释放互斥]
  KIND -->|正常附加日志| SUP[固定预算与轮转\n最多扫描 32 个候选归档]
  SUP --> SA[单次追加后清理合法归档；返回成功或失败]
  KIND -->|紧急附加日志| EM[零等待、无重试、无目录扫描]
  EM --> CAP{容量够？}
  CAP -->|是| EA[尽力追加]
  CAP -->|否| REPLACE[原子替换为摘要 + 最新有界批次\n失败保留原文件]
  KIND -->|崩溃写主日志| CR[只用启动时已捕获的路径与预算\n零等待、不轮转，容量不足就放弃主写]
  KIND -->|旧无保留策略入口| LEGACY[最佳努力互斥与追加\n不执行配置主日志保留流程]
```

主日志的大小判断发生在追加前，判断的是现有文件大小；因此 1MiB 默认值是轮转阈值，不是每次写完都保证不超过的硬上限。主日志目录保留会枚举对应归档，不能把附加日志“最多 32 候选”的限制套到主日志上。

## 5. 查询、分页、筛选与界面刷新

```mermaid
flowchart TD
  FILE[主日志 + 主归档\nstartup 当前 + 启动归档] --> CAT[LogSourceCatalog\n来源分类与身份]
  CAT --> SOURCE[LogTailReader\nILogEventSource / 物理行 / Snapshot 接口]
  SOURCE --> DECODE[LogEventDecoder\nJSONL → 旧文本 → raw 兼容事件]
  USER[搜索、级别、时间、属性筛选] --> AST[LogFilterBuilder / LogQueryParser\n编译同一 LogQueryExpression]
  SESSION[LogSessionCoordinator\n懒加载会话元数据与临时 scope] --> AST
  AST --> INTENT[PaginationCoordinator\n页号、页大小、排序、当前 snapshot / index]
  TIMER[页面活动时的既有刷新 Timer\n手动刷新 / 翻页命令] --> FLIGHT[SettingsLogRefreshPipeline\n单个后台任务，合并待办、取消与代际管理]
  INTENT --> FLIGHT
  FLIGHT --> SNAP[捕获或复用固定 LogSourceSnapshot]
  DECODE --> QUERY[LogQueryService\n唯一查询执行与索引入口]
  SNAP --> QUERY
  QUERY --> PREVIEW[先给出有限预览\n总数尚未精确]
  PREVIEW --> INDEX[建立或复用稀疏 LogQueryPageIndex\n读取指定页与精确总数]
  INDEX --> DISPATCH[Dispatcher 回到 UI\n销毁 / 非活动 / 过时代际不应用]
  PREVIEW --> DISPATCH
  DISPATCH --> TABLE[LogTablePresenter / 列表与详情 Presenter\n只展示当前结果]
  TABLE --> VIEW[LogViewportController\n统一滚动、跟随最新、选择定位]
  SOURCE --> TAIL[Latest / Older / Newer 有界窗口读取\n物理来源顺序 + 每源偏移推进]
  TAIL --> CONTROLLER[LogViewController\n有界事件窗口、筛选与增量结果]
  CONTROLLER --> DISPATCH
  SNAP -.-> INVALID[轮转 / 截断使 snapshot 失效]
  INVALID --> RETRY[重捕获并重试一次\n再次失效显示失败状态]
  RETRY --> SNAP
  CORR[操作关联选择] --> GRAPH[LogOperationGraph / Projector\n复用事件源与查询，按关联字段投影]
  QUERY --> GRAPH --> TABLE
```

当前表格分页以固定 snapshot 和稀疏页索引为主；控制器仍提供有限窗口与尾部读取能力，二者共用事件源和查询契约，不是两套文件解析器。筛选、会话与操作范围组合到既有表达式，不复制完整事件历史。

顺序尾读的游标按物理来源及偏移推进，展示按事件时间；时间回拨或同秒日志不意味着“没有新行”。主源和启动回退中的同一 `eventId` 在当前页 / 有界窗口内去重，不建立全历史身份集合。表格页排序与物理扫描游标是不同职责。

## 6. 日志自身健康与故障可见性

```mermaid
flowchart LR
  COUNTS[队列深度、入队拒绝\nPrimary / Fallback / Lost 确认计数\n消费者失败 / 停止超时] --> SAMPLE[LogPipeline.SampleLogHealth\n纯内存投影]
  SAMPLE --> CODE[Healthy / Degraded / Failed / Unknown]
  CODE --> LOOP[Engine 既有周期\nTryPublishLogHealth]
  LOOP --> PUB[StatusPublisher.PublishLogHealth\n变化 / 首次损失立即触发\n稳定值复用发布或 60s 新鲜度]
  PUB --> FILE[(状态文件\n健康时间独立于守护时间)]
  FILE --> READER[共享状态 Reader / Store]
  READER --> TRAY[Tray 健康提示]
  READER --> SETTINGS[Settings 后台单任务采样\n忙碌时不新增状态排队]
  SETTINGS --> UI[日志页健康提示]
  SAMPLE --> TIMER[既有每 5 分钟健康事件采样]
  TIMER --> HIGH[高优先级日志队列\nPipelineHealthSampled]
```

健康计数不因采样清零。“Failed”可能来自丢失、消费者故障或关闭超时；“Degraded”包含回退或当前拥塞；未确认写入、缺失或不可信的状态不能显示为健康。主写恢复状态机和界面健康代码是两个不同概念：主写还处于冷却时，回退已写入仍是降级。

## 7. 关闭、Flush 与崩溃路径

```mermaid
flowchart TD
  FLUSH[FlushAsync] --> MARK[捕获当前已接受水位\n合并到最多两代 flush 请求]
  MARK --> CONSUMER[既有单消费者继续处理]
  CONSUMER --> WATER{达到请求水位？}
  WATER -->|是| CONFIRM[完成 flush 等待\n是否持久化仍看批次确认分区]
  WATER -->|未到且超时 / 消费故障| FALSE[返回未完成]
  STOP[StopAsync / 正常退出] --> CLOSE[停止健康 Timer\n关闭队列入场，捕获停止水位]
  CLOSE --> CONSUMER
  CONSUMER --> DONE{停止水位已处理？}
  DONE -->|是| END[停止消费者并释放资源]
  DONE -->|3 秒等待超时| STOPFAIL[标记 stopFailed / Stopped\n晚到回调负责剩余资源收尾]
  CRASH[UnhandledException / UnobservedTaskException\nWPF 异常边界] -->|仅全局异常回调| RING[尽力同步输出高优先级环形快照]
  RING --> RINGFILE[(临时目录 crash-ring.log)]
  CRASH --> CW[CrashLogWriter\n不在故障回调读取配置]
  CW --> MAIN[已捕获主路径：容量允许才尽力追加]
  CW --> CF[(独立 crash.log 紧急写入)]
```

正常关闭尽力排空已接受事件，不保证因底层写入失败而丢失的事件重新落盘。崩溃路径绕开日常队列；全局回调会输出环形快照，WPF 回调另有直接崩溃记录。强制终止、断电或回调未被执行不能保证留下崩溃资料。`FlushOnce` 是冷态 / 测试用同步路径，不是运行期第二个消费者。

## 8. 诊断包导出运行流程

```mermaid
flowchart TD
  CLICK[About 或 Logs 发起导出] --> SESSION[同一窗口共享 SettingsDiagnosticExportSession\n保存对话框前取得 busy 所有权]
  SESSION --> CAPTURE[UI 捕获路径、版本、筛选\n时间 / 操作范围 / 有限当前窗口]
  CAPTURE --> BACKGROUND[后台创建独立 LogTailReader 与 Catalog\n不共用刷新中的可变 reader]
  BACKGROUND --> REQ[DiagnosticBundleRequest\n注入 ILogEventSource 与独立附加资料开关]
  REQ --> TEMP[目标同目录创建临时 ZIP]
  TEMP --> CONFIG[配置 / 状态分别限量读取并脱敏]
  TEMP --> SCOPE{导出范围}
  SCOPE -->|最近窗口| WINDOW[有限窗口事件]
  SCOPE -->|历史 / 当前操作| STREAM[共享事件源流式读取\n范围 + 查询表达式 + 时间筛选]
  WINDOW --> REDACT[DiagnosticRedactor\n每事件脱敏，失败不输出原文]
  STREAM --> REDACT
  REDACT --> BUDGET[JSONL 与可读文本各最多 512KiB\n有界保留队列与去重集合]
  BUDGET --> LOGS[写入两份日志\n截断 / 省略原因显式记录]
  TEMP --> OPTIONAL[按开关读取 startup / emergency\ncrash / crash-ring 有界尾部与归档]
  OPTIONAL --> CLEAN[分别脱敏、分别容错\n缺失 / 不可读写入 manifest]
  CONFIG --> MANIFEST[manifest.json\n范围、实际字节、hash、缺失与省略状态]
  LOGS --> MANIFEST
  CLEAN --> MANIFEST
  MANIFEST --> CANCEL{提交前取消？}
  CANCEL -->|是 / 出错| ABORT[清理临时 ZIP\n保留既有目标文件]
  CANCEL -->|否| COMMIT[新目标移动；覆盖使用 File.Replace + 备份\n创建竞态失败关闭]
  COMMIT --> FINISH[提交成功后才报告 complete\n任务结束释放 busy]
```

Request 默认仅开启启动日志附带；另外三个开关默认关闭。**About 的当前入口会默认附带四类资料**，不能把 Request 类型默认值误当成所有 UI 的默认选择。导航离开来源页或关闭窗口可取消，晚到结果不更新已销毁 / 非活动页面；取消不能保证立即中断底层同步 OS 调用。

## 9. 数据模型与默认资源边界

| 项目 | 当前事实 / 默认值 | 说明 |
|---|---|---|
| 规范记录 | `LogEvent`，schemaVersion 1 | eventId、时间、级别、组件、通道、事件名、消息、异常、PID/TID、关联标识、properties |
| 关联字段 | OperationId / TraceId / SpanId / UserActionId | 用于关联；不是物理分页游标，且不是每条事件都有 |
| 主格式 | JSONL，时间使用 UTC round-trip | UTF-8 无 BOM；旧文本通过兼容解码读取 |
| 普通 / 高队列 | 20,000 / 2,000 条 | 非阻塞 TryEnqueue；满或停止中可以拒绝 |
| 普通背压 | 普通容量的 80% | 提前拒绝普通事件；高队列仍有容量上限 |
| 消费批量 / 等待 | 200 条 / 100ms | 高优先级先取；不是每条 100ms 落盘保证 |
| Flush / Stop 等待 | 默认 3,000ms | 等待边界，不等于所有 OS IO 必定 3 秒内结束 |
| 主写恢复 | 连续失败 3 次；冷却 30 秒 | 后续批次触发探测，无额外重试线程 |
| 主日志配置默认 | 1MiB 阈值、7 天、30 归档、100MiB 目录预算 | 来自 GuardConfig；可配置，非当前用户配置实测值 |
| 附加日志固定预算 | 当前 1MiB、3 归档、7 天、单批 128KiB | 正常维护完成每类 4MiB；目录候选扫描至多 32 |
| 崩溃内存环 | 默认 500 条，单条 4KiB，总计 2MiB | 覆盖最旧记录；只保留高优先级近期快照 |
| 尾读默认请求 | 200 事件 / 256KiB | 不等于表格页大小或完整历史上限 |
| 查询表达式 | 最长 4,096 字符、AST 深度 32 | 查询复用唯一解析 / 执行入口 |
| 主日志诊断输出 | JSONL / 文本每份最多 512KiB | 按脱敏后的 UTF-8 字节与换行计算 |
| 附加诊断尾读 | 260KiB 读取 / 256KiB 输出 | 活动文件与最多 3 归档共用预算 |
| 配置 / 状态诊断读取 | 8MiB / 256KiB | 独立容错；脱敏失败不回退原文 |

主日志路径由配置及 `SmartGuardPaths.ResolveLogFile` 决定；启动日志由 `SmartGuardPaths.StartupLogFile` 决定；三类 emergency / crash / crash-ring 文件使用当前进程 `%TEMP%\SmartGuard`。不同运行账户的临时目录可能不同，不能假定它们总在同一物理目录。

## 10. 源码与责任对照

| 责任 | 当前实现 |
|---|---|
| 路径与配置 | [SmartGuardPaths](../src/SmartGuard.Configuration/SmartGuardPaths.cs)、[GuardConfig](../src/SmartGuard.Configuration/GuardConfig.cs)、[GuardConfigRepository](../src/SmartGuard.Configuration/GuardConfigRepository.cs) |
| 规则事实与确认 | [GuardObservationTracker](../src/SmartGuard.Engine/Worker/GuardObservationTracker.cs) |
| Engine 日志入口与消费集成 | [GuardianLoop.Logging](../src/SmartGuard.Engine/Worker/GuardianLoop.Logging.cs)、[GuardianLoop](../src/SmartGuard.Engine/Worker/GuardianLoop.cs) |
| 队列、生命周期、确认计数 | [LogPipeline](../src/SmartGuard.Configuration/LogPipeline.cs)、[LogPipelineOptions](../src/SmartGuard.Configuration/LogPipelineOptions.cs)、[LogBackpressureController](../src/SmartGuard.Configuration/LogBackpressureController.cs) |
| 主写恢复与回退 | [LogConsumerRecovery](../src/SmartGuard.Engine/Infrastructure/LogConsumerRecovery.cs)、[LogRecoveryStateMachine](../src/SmartGuard.Engine/Infrastructure/LogRecoveryStateMachine.cs)、[LogFallbackChain](../src/SmartGuard.Configuration/LogFallbackChain.cs) |
| 结构化记录与格式 | [LogEvent](../src/SmartGuard.Configuration/LogEvent.cs)、[LogStructuredSink](../src/SmartGuard.Configuration/LogStructuredSink.cs)、[LogJsonlFormatter](../src/SmartGuard.Configuration/LogJsonlFormatter.cs) |
| 物理写入与保留 | [SmartGuardLogWriter](../src/SmartGuard.Configuration/SmartGuardLogWriter.cs)、[Supplemental partial](../src/SmartGuard.Configuration/SmartGuardLogWriter.Supplemental.cs)、[SupplementalLogPolicy](../src/SmartGuard.Configuration/SupplementalLogPolicy.cs)、[LogArchivePlanner](../src/SmartGuard.Configuration/LogArchivePlanner.cs) |
| 崩溃路径 | [GlobalExceptionHandlers](../src/SmartGuard.Configuration/GlobalExceptionHandlers.cs)、[CrashLogWriter](../src/SmartGuard.Configuration/CrashLogWriter.cs)、[CrashRingBuffer](../src/SmartGuard.Configuration/CrashRingBuffer.cs) |
| 来源 / 解码 / 尾读 | [LogSourceCatalog](../src/SmartGuard.Configuration/LogSourceCatalog.cs)、[LogSourceSnapshot](../src/SmartGuard.Configuration/LogSourceSnapshot.cs)、[LogEventDecoder](../src/SmartGuard.Configuration/LogEventDecoder.cs)、[LogTailReader](../src/SmartGuard.Settings/LogView/LogTailReader.cs) |
| 查询与索引 | [LogQueryService](../src/SmartGuard.Configuration/LogQueryService.cs)、[LogQueryParser](../src/SmartGuard.Configuration/LogQueryParser.cs)、[LogIndexBuilder](../src/SmartGuard.Configuration/LogIndexBuilder.cs) |
| 页面任务 / 分页 / 滚动 | [SettingsLogPageHost](../src/SmartGuard.Settings/SettingsLogPageHost.cs)、[Refresh partial](../src/SmartGuard.Settings/SettingsLogPageHost.Refresh.cs)、[SettingsLogRefreshPipeline](../src/SmartGuard.Settings/SettingsLogRefreshPipeline.cs)、[SettingsLogPaginationCoordinator](../src/SmartGuard.Settings/SettingsLogPaginationCoordinator.cs)、[LogViewportController](../src/SmartGuard.Settings/LogViewportController.cs) |
| 诊断导出 | [DiagnosticBundleRequest](../src/SmartGuard.Configuration/DiagnosticBundleRequest.cs)、[DiagnosticBundleExporter](../src/SmartGuard.Configuration/DiagnosticBundleExporter.cs)、[Files partial](../src/SmartGuard.Configuration/DiagnosticBundleExporter.Files.cs)、[DiagnosticLogTailReader](../src/SmartGuard.Configuration/DiagnosticLogTailReader.cs)、[DiagnosticRedactor](../src/SmartGuard.Configuration/DiagnosticRedactor.cs) |

已退役的统计聚合服务、统计面板与错误趋势图不在当前链路中。本图不把入队接受当作磁盘确认，不把高优先级当作绝不丢失，也不把诊断脱敏描述成运行日志写入前的统一脱敏。
