# 日志必要缺口修复实施记录

计划：[原执行计划](2026-10-04-log-observability-repair.md)。本文件只记录新鲜实施证据，不替代架构契约。

## 起点与约束

- 开始日期：2026-10-04，Asia/Shanghai。
- 起点提交：`2c8d83136c70e631c0ac13b07dba2948133b96ba`。
- 实施分支：`fix/logging-observability`；当前工作区，顺序实施，不派发子 Agent。
- 起点受版本控制文件干净；已有未跟踪资料全部保留。
- 证据目录：`.work/logging-repair/20261004-implementation/`；不覆盖审计证据。
- 环境：Windows、.NET SDK `10.0.204`；项目目标仍为 .NET 8 Windows，不升级依赖。
- 影响矩阵、模块 Owner 与成本模型沿用原计划 §3–§4；按阶段更新架构契约和门禁。

## 进度

2026-10-05 追加真实桌面验收：先完成完整入口 1889 项版本回归，其后复现暂停期间重复 ExternalPlanChange 与“下轮纠偏”假承诺，按 TDD 补修。影响矩阵：正确性/可解释性受影响，修改既有 Runner 的外部变化观测日志与通知文案；不改变策略评估、暂停、执行基线、动作、通知投递/冷却、配置格式或 Owner。每完整轮次 O(1)，只增加一个可空 GUID 的观测去重状态，无 IO、集合、队列或新探测。补修后完整四层门禁及新版真实暂停恢复均通过，详细证据如下；真实休眠验收与冷启动性能目标仍独立登记。

| 阶段 | 状态 | 证据/结论 |
|---|---|---|
| T0 基线 | 已通过 | Configuration 45/45；Engine 27/27；当前源码隔离探针 13 场景复现原缺口 |
| T1 保存日志真实性 | 实现与目标回归通过 | 目标 38/38；架构 54/54；Configuration 全项目 420/420；真实 UI 留待 T9 |
| T2 真实写入与非阻塞 | 实现与回归通过，展示接入 T3 | Configuration 431/431；Engine 293/293；日志架构 54/54；三轮隔离锁竞争比较 |
| T3 日志健康 | 实现与目标验证通过 | Engine 40/40；Configuration 42/42；Tray 全项目 87/87；Settings 56/56；日志架构 55/55 |
| T4 规则观测 | 实现与目标/全项目回归通过 | 目标 116/116；Engine 全项目 308/308；日志架构 57/57；三轮 128 项轻路径比较 |
| T5 停工恢复与心跳 | 实现与全项目回归通过 | Engine 327/327；Configuration 435/435；日志架构 60/60；读取依赖提前于 T4 |
| T6 附加文件与回退可见 | 实现与阶段回归通过 | Configuration 446、Engine 327、最终 LogView 62、日志架构 61、Settings 13 通过 |
| T7 诊断容错 | 实现与阶段回归通过 | Configuration 455/455；补充诊断回归 34/34；架构 30/30 |
| T8 导出异步所有权 | 实现与目标回归通过 | 相关 WPF/取消/导航/可访问性 30 项；架构 10 项通过 |
| T9 集成与验收 | 完整四层门禁及主要桌面/真实休眠通过，性能验收未闭环 | 最新完整入口 Pester 156、.NET 1896、Tray 2、Installer 9，均零失败/跳过、exit 0；新版真实暂停恢复、诊断键盘及 DPI/对比度检查已记录；HibernateResume 六项通过，冷启动 P95 目标仍未关闭，旧 2h 隔离 Soak 单列 |

M1–M3 的实现及回归已落地；T9 的自动化、性能、兼容性、可见桌面、真实休眠与真实计时 2h 隔离运行证据如下。2026-10-05 追加验收后，真实休眠已通过，冷启动性能目标尚未关闭，M4 尚未达成；未执行的扩展可访问性路径仍单列。集成测试仅临时安装，未更新正式安装或发布。

## 2026-10-05 优先验收追加记录

- 最新完整入口：`run-tests-20261005T000335459Z-20e03c4fb80246508a176b79ab2167c2.summary.json`，08:09:58 北京时间结束，命令及 summary exit 0。Pester 156/0/0；.NET 1896/0/0（Engine 334、Configuration 457、Cli 1、Architecture 338、Packaging 15、Tray 87、LogView 69、Settings 595）；Tray Integration 2/0/0；Installer Integration 9/0/0。四层均实际运行，没有取消、跳过或 NOT RUN。此前 `run-tests-20261004T233714892Z-06b1ea4a36c54e739cd55c80ff0d14d5.summary.json` 的 1889 项通过仅是补修前证据。
- 新证据目录：`.work/logging-repair/20261005-acceptance/`。`paused-external-red-complete.trx` 三项实际断言失败（0/3/0、exit 1），覆盖日志假承诺、通知假承诺及重复观测；`paused-external-green.trx` 3/0/0，最终 `paused-external-boundaries.trx` 7/0/0，均 exit 0。四项额外边界首次通过，只称回归。未改执行基线，也不新增通知机制。
- 新版真实 Engine PID 14684、Settings PID 40360，使用 published bin 与新隔离根 `live-root-3`，无注入动作。08:11:33 从设置页开启暂停并实际保存；08:11:49 观测外部高性能计划，随后到 08:12:50 仍 paused/high performance，外部事件累计一次，暂停后没有 PowerPlanSwitched。08:13:03 从 UI 关闭暂停；08:13:08 实际切到平衡并记录 verified；08:13:15 记录 already-satisfied。配置保存、准入、动作和时间均可由实际 JSONL/status 对照，不能将命中等同动作。
- 补修前真实桌面也验证 Settings #1 / Explorer #2 命中、退出命中与 PowerCfg 实际计划转换；暂停误导证据保存在 `live-root-2`，不覆盖。第一次使用 Chrome 切出时 Computer Use 因无法确认 URL 停止，该段不算完整未命中桌面验收；后续桌面检查使用设置与系统设置窗口。
- 诊断键盘：Ctrl+F 聚焦日志查询，输入“暂停”得到三条结果，Tab 按刷新→筛选→日志导出→诊断包移动，Space 激活，文件对话框输入目标后 Enter 保存。`keyboard-diagnostic.zip` 为 2969 字节，scope=CurrentQuery，JSONL 三条记录；逐项 SHA-256 与 manifest 一致。未把按钮 Enter 激活认作通过。
- DPI/对比度：实际 Windows 缩放由 200% 改为 100%，检查日志/诊断入口和取消，再恢复 200%；应用仍运行时启用 Windows“水生”对比度主题，在 100%/200% 检查诊断对话框及取消，最后恢复“无”。应用主体继续浅色配色，不宣称完整系统对比度适配；屏幕阅读器、完整高对比度键盘矩阵及减少动画路径为 NOT RUN。小窗口事件列需横向滚动仍是已记录的既有问题。
- 三轮 owned 会话均 exit 0，`live-session.result.json` / `live-session-2.result.json` / `live-session-3.result.json` 的 Restored=true、Error 为空，原高性能计划、20% 亮度、正式配置 SHA-256 与 Guardian/Tray 任务基线完全一致；第三轮于 08:22:35 北京时间完成恢复。种子通过原 GuardConfigRepository 建立独立配置，并预置 init marker，明确排除首次安装计划初始化；强制结束自有进程不算优雅 AppStopping 验收。
- 专业复审：固定大小确认状态仅作用于日志，写入接受和过滤才推进；拒绝继续原周期重试，外部判定解除清除确认。策略、执行基线、通知冷却与唯一写入入口保持原责任，契约与测试一致。现有完整门禁与实测已覆盖补修，不引入日志平台或框架。真实休眠使用既有 `Invoke-PowerLifecycleAcceptance.ps1` 核对 Windows 事件、Engine 生命周期及前后 PipelineHealthSampled，不以模拟 Soak 或状态健康代替。
- 真实休眠闭环：用户手动休眠并唤醒、解锁后确认“已唤醒”。`hibernate.result.json` / `hibernate.transcript.txt` 保留 canonical 验收结果，场景 HibernateResume 于 08:16:14 开始、08:22:19 结束，result=Passed，命令 exit 0；六项检查（生命周期事件、Windows 挂起/恢复、挂起期间无计划写入、实际休眠状态、状态 freshness、pipeline 健康增量）全部 Passed。脚本核对 Kernel-Power 42 的 TargetState/EffectiveState 均为 5，而非把屏幕关闭或普通待机认作休眠。
- 同一 Engine PID 14684 在 08:18:29.3427671 观测挂起、08:19:55.0017678 观测恢复；挂起区间没有 PowerPlanSwitched。健康样本 08:15:50 与 08:22:06 的 droppedNormal/droppedHigh/failedBatches/lostLines 均为 0，consumerFailed/stopFailed 均 false；累计 primaryWrittenLines 从 38 到 64，证明恢复后消费继续。该结论只覆盖本次真实休眠，不泛化为所有设备、S0 合盖或长期运行。冷启动 P95 30.0008ms / 20ms 预算差距仍待后续处理。

## T9 集成与验收记录（本轮实施收口，验收未闭环）

### 全量门禁及本轮补修

- 第一轮沙箱内 Pester 访问隔离注册表失败，不计产品 RED；正常权限重跑出现 4 项真实门禁失败（1882 项中 1878 passed / 4 failed）：两个既有主文件行数上限、主日志缓存追加及物理两端分页。Tray/Installer 在 .NET 失败后未运行。记录为 `run-tests-20261004T135414289Z-043b26515e1d4333a1e5f26973e1c78a.summary.json`，不覆盖失败证据。
- 行数问题仅把原配置读取方法移动到同一 GuardianLoop 的 Configuration partial、等待方法移动到已有 Foreground partial；忙碌 UI 连线归既有 SettingsDiagnosticExportCoordinator。没有放宽门禁、新增 Owner 或改变守护行为；`t9-size-green` 4/0/0。
- 读取回归根因：缺失 fallback 时不应进入合并历史；相同/倒退时间戳的显示排序不能决定物理游标；空边界页不能清除反方向游标。新增物理边界、旧时间戳追加与新建 fallback 的实际 RED 后修复。`t9-history-final` 65/0/0、`t9-controller-boundary-green` 37/0/0，exit 0。
- 复审再发现 HasNewer 使用显示末项，文件已到物理 EOF 仍报 true。`t9-newer-end-red.trx` 0/1/0 为预期断言失败；`t9-reader-boundary-green.trx` 全项目 66/0/0、exit 0。初次 logger 参数未引号保护造成命令解析错误，不计行为 RED。共享扫描器与 Controller 仍为原 Owner，契约同步 LOG-INV-PHYSICAL-PAGING。
- 首次全量 GREEN `run-tests-20261004T141246683Z-69fd5d59de67451a883cd26ece89fe60.summary.json`：Pester 156/0/0，.NET 1885/0/0，Tray Integration 2/0/0，Installer Integration 9/0/0，exit 0。该运行早于最后 HasNewer 补修，最后代码的全量结果另记，不把它当最终版本证据。
- HasNewer 补修后重跑（早于后续合并历史补修）`run-tests-20261004T145152257Z-f183da2315c94f70a9e20e9ffc6060eb.summary.json`：Pester 156/0/0、.NET 1886/0/0，含 Architecture 338、LogView 66、Settings 595。托盘集成提升被用户取消，结果契约 Failed/exit 1/Total 0，标 BLOCKED，不是零失败通过；安装器因前置层失败为 NOT RUN。总入口 exit 1。没有再请求或自动接受 UAC；此前集成通过保留为前一版证据，不能将两轮拼成最后版本“全绿”。
- 继续复审加入真实同秒合并历史：`t9-merged-equal-time-red.trx` 0/1/0，800 条只取到 97；合并 latest/连续 older 改按各源物理后缀选择。补修过程中 `t9-merged-overlap-red.trx` 0/1/0 确认重复副本不能提前推进另一来源（400 个唯一事件只取到 217）；只确认实际选中的物理范围后，`t9-reader-complete-green.trx` 全项目 68/0/0、exit 0，覆盖普通合并连续性、同秒主/回退、部分主写重复批不漏唯一记录、每页去重。去重没有扩展成全历史 ID 缓存。
- 合并修复后的完整 .NET 层第一次独立运行既有 `Invoke-SmartGuardDotNetTestSuite -Tier full`，exit 0；TRX 前缀 `10a9dc34bf85449db012341299f68b9d`，8 项目合计 1888/0/0。随后补充 `t9-merged-reverse-regression.trx`（1/0/0、exit 0），确认合并同秒历史到达首端后仍可回到末端，800 条无重复；该新增测试首次通过，仅记回归，不冒称 RED。
- 最后测试集合完整 .NET 层再次独立运行，exit 0；TRX 前缀 `13bce6896fb84a3d9fb22cf991a95295`，8 项目合计 1889/0/0：Engine 327、Configuration 457、Cli 1、Architecture 338、Packaging 15、Tray 87、LogView 69、Settings 595。逐一核对 TRX total/executed/passed/failed/error/timeout/aborted/inconclusive/notExecuted。两轮均不触发已取消的 UAC，只证明 .NET 层，不包含 Pester 或真实托盘/安装器集成。
- 受影响性能/生命周期回归：`t9-settings-performance` 62/0/0、`t9-engine-performance` 3/0/0，exit 0。完整测试均按业务链运行，新增行数拆分不改变架构门禁阈值。

### 13 场景、导出性能与回滚读取

- 当前源码探针 `current-audit/run-current/evidence.json` 13 个修复后断言通过（exit 0）；`run-desktop/evidence.json` 额外保存正规 formatter 输出用于可见桌面。与 T0 原 evidence 独立：未命中初始、已符合计划仍命中记录、A→B、退出、身份未知、配置停工、恢复、三层失败、背压计数、满高队列无同步等待、坏配置仍导出、默认 API 内容、保存失败无假成功。全部系统动作注入，OS actions=0；不能声称已完成真实 PowerCfg 操作。
- `export-performance/{before,current}/result.json` 来自同一探针、实际起点 archive 与新版程序集，分别三轮，各轮主日志和 startup 各 104,860,119 字节。旧版导出 28,726/26,907/27,767ms；新版 4,072/3,720/4,232ms。新版主 JSONL 518,318 字节、文本 493,619 字节，startup 259,974 字节；旧版 startup 全量进入 ZIP。主日志仍扫描完整历史，输出有界不代表读量有界；附加日志读取预算由 T7 的实际 ReadBytes 测试另证。三个样本只报告范围，不能当可靠 P95。
- 该实验进程全程分配旧版每轮约 91.6GB、新版约 3.8GB；导出后私有内存旧版约 890MB、新版约 259–263MB。这是分配总量/阶段进程值，不是保留集合大小，也不是所有运行环境的峰值承诺；没有把耗时改善归因于异步化。
- `compatibility/old-reader-new-status/result.json`：真实旧程序集读取新版 15 条事件、忽略新增健康字段，配置两条规则可保存再读；`new-reader-old-status/result.json`：真实新版读取旧程序集序列化的 status/config 与一条中文/emoji 传统日志。均 exit 0、OS actions=0，无配置格式迁移。
- 兼容探针首次误用不持久化配置的 Soak 根，之后切换输入源码却 `--no-restore` 导致测试宿主 deps 不含 Settings；两者均为夹具/构建失败，不冒充产品缺陷。明确输入根并分别 restore 新旧依赖图后通过，原失败不覆盖。

### 可见桌面及待关闭项

- 使用本轮本地 Release 输出与独立 `--root`，经 computer-use 实际操作设置窗口；未运行安装/升级、未修改真实策略。About 在日志页从未初始化时成功导出 `run-desktop/about-before-logs.zip`，实际 ZIP 含 config/status/main；四类附加日志的缺失状态均入 manifest，界面给出提交后成功提示。
- 实际日志表格可读命中 #1/#2、退出、配置失效/恢复、“已符合目标，无需切换”；使用真实结构化事件，未把人工夹具事件当系统动作证据。日志状态缺健康字段明确未知。导出对话框 Esc 取消后 busy/取消控件收敛，按钮恢复；窗口正常关闭。
- `current-audit/run-health-ui-long` 通过原 StatusPublisher 发布人工 failed→healthy，主日志始终 7,005 字节且最后写入 23:03:01 不变；23:03:50 的真实桌面显示“日志可能缺失”，23:09:28 提示已消失。该证据只证明无新日志时健康展示更新，不冒充真实磁盘失败；三层失败由 T2/T3 故障测试另证。Tab 进入查询框与控件 UIA 名称已观察，完整诊断键盘路径、DPI/高对比度仍未验收。
- 两组实际 100MiB 输入（长行、更多短行）均可导出；导航点击发生在提交完成后，该两次尝试不能据此认定运行中导航取消通过。对话框取消是真实桌面 PASS，自动化的可控延迟/取消测试单独通过。
- 补充 `export-performance/desktop-cancel-large/round-0`：主日志 1,073,742,102 字节、startup 104,857,686 字节，扩大主输入以延长真实 IO 观察窗口。23:30–23:34 三次均先观察到保存对话框已关闭、诊断按钮禁用与“正在导出”，再分别点击取消、导航至常规、Alt+F4 关闭窗口。取消出现“导出已取消”并恢复按钮；导航页面立即切换，回到 About 按钮可用且无晚到成功提示；关闭后窗口/进程退出，重新打开没有残留忙状态。三项目标 `cancel-io.zip`、`navigation-io.zip`、`close-io.zip` 均不存在，根中仅保留原配置与两个输入日志，无临时文件。该记录是可见桌面真实 IO 取消/导航/关闭 PASS，不以大小变化后的夹具替换原 100MiB 性能样本；全程未启动 Engine 或执行系统计划动作。
- `current-audit/run-save-ui` 补充真实文件占用故障：90 秒只读共享锁允许读取旧配置、禁止原子替换；23:38:58 点击暂停，界面显示“暂停设置失败”，只追加 ConfigMutationFailed/“变更未保存”，SHA-256 保持 `9E1267EE243D7AB620B301B9E07249B735C635258BCE77A79C46F423500210A7`，重新打开仍为原暂停值。锁正常释放后 23:41:09 成功保存并记录“已保存”；23:41:27 恢复夹具原值，哈希回到上述原值。没有运行 Engine，成功日志不声称核心已采用；未修改真实策略或计划任务。该路径 PASS，不等同真实 ACL/全盘故障验收。
- 100/150/200% DPI、高对比度、完整诊断键盘路径及真实休眠唤醒仍 NOT RUN，不以源码/STA 替代。
- 既有窄窗口下表格固定列使“事件”位于可视区外，最大化后可读；原版 XAML 同样使用 120/190/180/240 最小列宽。本轮新增诊断按钮在初始窗口可见；该既有表格适配问题记录为范围外 P2，没有顺手重设计。
- `soak/run-120min-final/result.json`：实际持续 7,200.021 秒，进程 exit 0、stopped=true；从 2026-10-04 22:05 至 2026-10-05 00:05 跨本地午夜。65,704 个周期、241 次完整评估、24 次注入主写故障时段，2 次模拟计划动作、真实 OS actions=0；稳定前台、两程序切换、周期主写失败/回退/恢复、配置不可用/恢复均注入。累计 Enqueued/Written=65,858，Primary=59,203、Fallback=6,655、Lost=0；PrimaryFailedBatches=72，所有丢弃=0、ConsumerFailed/StopFailed=false。这是确认写入总数，不表示预算轮转后仍保存所有历史记录。
- `samples.jsonl` 为起点及每 5 分钟的 24 个样本（最后为 115 分钟）；120 分钟由最终停止结果确认。每次 flush 后队列均 0/0、维护 Succeeded/Completed=true；10 分钟后私有内存 19.34–20.09MiB，最初/末尾五个稳定样本均值 19.72/19.55MiB，句柄 265–302、线程 8–13，无连续上升趋势。最近 3,000 个周期的样本 P95 0.0535–0.0627ms、最大 34.6293ms；这些是注入周期成本，不替代实际 Engine 系统探测性能。
- 全部样本的 startup 活动+归档 ≤4 个，每文件 ≤1MiB、总量 ≤4MiB；最终 startup 活动 78,886 字节，两归档 1,048,472/768,946 字节，跨日维护已观察。主档归档始终 ≤3 份；既有主写轮转语义允许单批越过阈值，样本出现 1,049,140 字节归档，本轮未宣称主档严格 ≤1MiB。最终保留日志全部可解析 JSONL。7 天过期边界由 T6 受控时间测试另证。
- 前两次夹具运行分别因沙箱原子替换被拒与假 apply 触发而中断，失败证据保留；后者改为注入目标返回值，完整两小时才记 PASS。真实休眠唤醒 NOT RUN，所有本轮隔离设置窗口与夹具进程已结束，不以隔离 Soak 替代真实系统生命周期。


## T8 证据与自审

- `t8-ui-red` 0/1/0、exit 1：可控 300ms exporter 使原调用长期占用 Dispatcher；`t8-architecture-red` 为缺少窗口 session 的契约 RED。编译错误不计行为 RED。
- 修复后 `t8-core-green` 3/0/0；`t8-lifetime-final` 30/0/0（exit 0），覆盖两个实际 coordinator 交错、取消保持 busy、来源匹配、其他窗口隔离、关闭后忽略结果、About 不初始化日志页、独立 reader 在后台构建；保留既有导航/生命周期/可访问性回归。新增 session 检查属于契约落地后的回归，不冒称旧实现已复现。
- `t8-architecture-green` 10/0/0、exit 0。UI 显示短状态与取消按钮，没有新 Timer。文件对话框仍在 UI，固定路径准备/压缩在工作线程；取消在 finally 后释放资源，不阻塞窗口关闭。
- 核对 [WPF 官方线程模型](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)，采用 await Task.Run 与 UI continuation；测试宿主的 headless WPF 证据不等于可见桌面/DPI/焦点验收。

## T7 证据与自审

- `t7-behavior-red.trx` 四项真实行为失败：坏配置中止导出、RecentWindow JSONL 达 3,283,999 字节、嵌套属性/异常/状态漏脱敏、提交前取消仍可覆盖旧目标。架构 RED 是缺少 ring 标志的契约断言，不是行为复现。
- 新实现第一次 `t7-core-green.trx` 16/1/0；`t7-nested-parent-red.trx` 证实 JsonArray 重复赋值现有子节点触发 parent 所有权异常，修正为只替换新节点，最终核心 22/0/0。
- `t7-read-red` 0/1/0：主源读到一项后抛 IO 会中止全包；修复后保留已读项并继续其他资料，manifest 标明 unreadable/truncated。Configuration 全项目 `t7-configuration-final` 455/0/0，exit 0。
- `t7-tail-budget-regression.trx` 4/0/0；100MiB 输入实际读取不超过 260KiB，中文/emoji 完整、无半行；活动和三归档共用预算。它们是实现后的回归，未冒称 RED。
- 补充提交竞态/锁定目标/实际 hash/附加 JSONL 脱敏回归 `t7-final-regression` 34/0/0，exit 0。读量不包括后续独立维护的最大 1MiB 遗留修复，两段成本分别有界。
- 架构原检查将 partial 文件数当 Owner 数，且绑定私有 AddJsonlLogEntry 方法名；保留唯一目录、命名空间、规范主文件、partial 声明和正式 formatter/ZIP 写入边界，最终 `t7-architecture-green` 30/0/0，exit 0。没有新增第二套 exporter/reader。
- 自动化不覆盖真实 ACL、磁盘故障、真实 UI 或长时间运行，这些证据留在 T9。

## T0 / T1 证据

- T0 命令为原计划 T0 的两个 `dotnet test` 过滤器，添加 `--no-restore`、TRX logger 与上述独立结果目录；退出码均 0：`t0-configuration-baseline.trx`（45 passed / 0 failed / 0 skipped）、`t0-engine-baseline.trx`（27 / 0 / 0）。既有编译警告 CS1717/CS8604/xUnit1031 未由本轮引入，不把输出称为无警告。
- 审计工具沙箱构建失败无错误诊断，既有二进制运行被沙箱拒绝隔离 status 的 File.Replace；重新正常构建当前源码（0 warning / 0 error，exit 0）并在沙箱外运行同一隔离探针，exit 0。新证据为 `run-3d2e76927e0b4fb4a9170023cd4659dc/evidence.json`，13 个场景。该工具不操作真实电源计划或任务。
- T1 架构 RED：`dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --filter FullyQualifiedName~LoggingRepairArchitectureTests`，exit 1，`t1-architecture-red.trx`；失败原因是未登记 ConfigMutationFailed。
- T1 行为 RED：Configuration 过滤 `ConfigMutationServiceTests|SettingsSaveCoordinatorTests`，exit 1，`t1-behavior-red.trx`（3 passed / 4 failed / 0 skipped）；确认缓存被未提交 Paused 污染、成功记录缺已保存语义、规则替换无记录、保存失败留假成功。
- 后续系统任务隔离测试初次 RED 为缺少隔离注入点；不会触碰真实任务。坏日志路径测试在旧代码也能通过（writer 已隔离），`t1-path-red.trx` 实际为 PASS，只作为回归，不计作功能 RED。
- T1 GREEN：目标过滤增加 `GuardForegroundRestrictionConfigTests`，exit 0，`t1-green.trx`（38 / 0 / 0）；架构过滤 `LoggingRepairArchitectureTests|ArchitectureContractLogging|ConfigRepository|ForegroundGuardArchitecture`，exit 0，`t1-architecture-green.trx`（54 / 0 / 0）。Configuration 全项目回归 exit 0，`t1-configuration-regression.trx`（420 / 0 / 0）。
- T1 自审：原子配置写入 Owner 未变、未知键保留；缓存只在提交后或失败恢复时失效；日志使用已发布配置；AutoStart 失败反馈仅区分阶段，未改变系统任务行为；内置测试 seam 仅隔离真实任务。全套 Run-Tests 与真实桌面证据留在 T9，未宣称整个计划完成。

## T2 证据与自审

- RED：`t2-engine-red.trx`（4/4/0）复现三层失败假成功、拒绝后写盘、背压漏计、回退丢结构；`t2-flush-red.trx`（0/4/0）复现关闭故障假成功、Flush 无确认和竞争读取。各 TRX 数字顺序为 passed/failed/skipped，失败运行 exit 1。
- 补充 RED：`t2-health-red.trx` 延迟假 0；`t2-stop-event-red.trx` 无停止事件；`t2-cold-flush-red.trx` 冷异步调用被 writer 阻塞；`t2-ownership-red.trx` 冷 reader 未退出即返回；`t2-publication-race-red.trx` 可控交错下把待处理 Lost 当历史，Flush 错误返回 true。后二者按同一短锁原子交接及提交水位/结果修复。
- `t2-clock-red.trx` / `t2-transitions-red.trx` / `t2-consumer-health-red.trx` 是缺失契约或观测入口 RED，不冒充已经观察到的 OS 时间故障。冻结范围、取消局部性、非法分区继续消费等首次已通过的新增测试只算回归。
- GREEN：`t2-final-config-target.trx` 48/0/0；`t2-transitions-green.trx` 23/0/0；`t2-architecture-final.trx` 54/0/0（各 exit 0）。旧架构门禁要求业务线程同步回退，与新批准契约冲突；更新为消费侧独占批回退，保留三层与结构化 sink 门禁。
- 全项目回归：`t2-configuration-regression.trx` 431/0/0、`t2-engine-regression.trx` 293/0/0，exit 0。回退不恢复主写 Healthy；冷却为可注入单调时间；累计写入分区与 ConsumerFailed/StopFailed 可观测。状态和 UI 的健康投影尚待 T3。
- 性能工具为新建 `benchmark/`，基线来自起点提交的只读 Git archive 展开副本；不运行 Engine，不观察/切换真实计划。每版三轮、每轮 10 个高队列满业务调用，主 sink 命名锁由专用线程持有。初次未预热：原版 P95 2013.80–2052.77ms；修改版首轮冷启动 P95/Max 30.0008ms（超过拟定 P95 20ms，低于 Max 100ms），后两轮 0.2639/0.0729ms；原始结果完整保留，不删除失败预算样本。
- 等量一次解锁预热后重测，原版 P95 2013.71–2016.27ms；修改版三轮 P95/Max 分别 0.8874、0.0565、0.0820ms，P50 为 0.0036–0.0045ms。原始结果见 `performance-after-warm/results-f973a58b9c5b4f04a458a8c9d5e4391e.json`；独立记录线程/私有内存、队列深度、拒绝计数与回退行数。预热会额外产生 1 次拒绝，累计 DroppedHigh=11，正式样本为 10。原版三轮各写 10 条回退，修改版均 0；源码边界与拒绝测试确认修改版业务侧 writer 入口 0 次，探针未注入 OS 写入计数器，不能把行数称为系统调用数。此实验只证明满队列业务侧无写盘等待，不证明整个 Engine 的性能或长期内存趋势。
- 自审边界：消费 IO 在 gate 外，信号容量 1，内部 flush 代数 ≤2，双优先级各自 FIFO；异常批次继续消费但健康失败保留。Stop 超时不提前销毁仍被消费任务持有的信号；AppStopping 只表示停止意图。主线程不会尝试同步回退。附加日志保留预算仍待 T6。

## T3 证据与自审

- 契约 RED：`t3-architecture-red.trx`、`t3-status-red.trx`、`t3-pipeline-red.trx` 证明缺失类型/序列化/投影入口。先增加 DTO 后再运行 `t3-status-behavior-red.trx`（0/2/0，exit 1），实际确认健康变为 failed 仍被旧发布去重忽略。新字段逐属性 JsonPropertyName 登记，测试读取真实 StatusPublisher 输出 JSON，不依赖额外 naming policy。
- UI RED：`t3-tray-red.trx`（0/1/0）健康代码变更被 Clone/equality 忽略；`t3-settings-red.trx`（0/3/0）缺少健康输入契约。`t3-background-red.trx`（0/1/0）实际观察到 UI Dispatcher 上状态读取 1 次；改为既有刷新 Owner 后 `t3-background-green.trx`（1/0/0）确认非 UI 读取且无新日志时 failed→healthy 更新。
- GREEN（均 exit 0）：`t3-engine-target.trx` 40/0/0；`t3-configuration-target.trx` 42/0/0；`t3-tray-green.trx` 31/0/0，Tray 全项目 `t3-tray-regression.trx` 87/0/0；`t3-settings-target.trx` 56/0/0（含 WPF STA 生命周期、分页和导航）；`t3-architecture-green.trx` 55/0/0。TRX 计数将与完成前全量 summary 独立核对。
- 旧 Tray 精确文案测试因缺失健康字段新增未知提示而失败（`t3-tray-green-attempt.trx` 13/18/0），更新旧格式预期；专门守护字段测试只增加健康夹具，仍验证原守护语义，未放宽来源/原因断言。新健康测试独立覆盖缺失/未知、过期/超前时间、计数与时间变化不重复应用。
- 全层日志失败而状态可写的隔离测试可读取 failed；状态 IO 再失败局部收敛，不结束 Engine。健康单独更新不推进守护 timestamp；稳定代码的样本时间/持续计数不每 5 秒写入，60 秒 freshness 或既有守护变化才带最新计数。
- 自审：无新文件、Timer、IPC、展示面板或日志推断状态路径；健康值不可变、累计分区原子发布，当前拥塞可解除而历史损失保留。Settings 状态独占原后台 flight/取消所有权，状态-only 请求忙时不排队；UI apply 不读取状态文件。WPF 线程边界核对[微软线程模型](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)。真实托盘、DPI、UIA 与 Soak 仍为 NOT RUN，T9 统一验收。

## T5 读取依赖（提前于 T4）

- T4 需要规则 generation，故提前实现 T5 的唯一详细加载结果与缓存指纹；其余停工/恢复/心跳仍按后续阶段执行。避免临时在每次轻路径计算指纹或保留旧 GuardConfig。
- `t5-load-red.trx` 0/2/0、`t5-load-architecture-red.trx` 0/1/0，exit 1，是缺失契约的断言 RED；不冒充实际访问故障复现。GREEN `t5-load-green.trx` 28/0/0、`t5-load-architecture-green.trx` 5/0/0，exit 0。测试确认缺失、解析、无效根节点与目录拒绝类别、无旧配置、缓存只读一次、同数量换名单变化、主题/路径大小写不变。目录拒绝用于隔离访问失败，不声称已完成真实 ACL 验收。

## T4 证据与自审

- `t4-behavior-red.trx` 0/2/0、exit 1：旧回调上复现计划已正确却只有 IdleDetected，无命中/执行记录；索引缺失是契约 RED。`t4-architecture-red.trx` 0/1/0 确认事件未登记。
- `t4-retry-red.trx` 11/1/0、exit 1：实际复现稳定未命中每轮清除 pending 执行，导致重试改写观测时间；按“仅新准入事实失效旧执行”修复。同 basename 的 A→B 仍立即淘汰旧待发执行结论。
- `t4-target-final.trx` 116/0/0、`t4-engine-regression.trx` 308/0/0、`t4-architecture-green.trx` 57/0/0，均 exit 0。覆盖独立接受/拒绝、规则/gate 失效、1000 轮稳态、级别过滤、A→B/退出无额外完整轮次、真实 JSONL 保留原 timestamp，以及最终撤销不记录切换成功。
- 中间 `t4-target-green.trx` 115/1/0 是 timestamp 夹具没有先登记真实规则 generation；补齐与正式流程相同的 context 后通过。签名迁移中的编译错误不计功能 RED。Runner 和正式 Tests 全部迁移同一个六参数回调，不留兼容路径。
- 性能：新隔离 `rule-benchmark/` 对起点 archive 与修改版各三轮、每轮 1000 次稳定轻路径，名单 128 项；包含修改版既有周期 drain，假前台/假时钟，无 PowerCfg/WMI 或真实动作。JSON 分别在 `rule-performance-before/results-d6d03281a0a74ee4bc409e77ee9cc7b0.json`、`rule-performance-after/results-7d86a80307d645949f51f5def0116501.json`。CPU/分配/私有内存/线程与调用数均保留；不以亚毫秒样本代替真实桌面或 Soak。
- 自审：匹配算法单源，索引来自原 FindIndex；完整决策携带该索引，不再匹配。唯一 Loop 排出点在 Wait 前，不依赖完整 Runner 才发现 A→B；无新 wake/Task/历史队列。事件固定字段+GUID+短文件名保持 4KiB 内，时间不参加稳定相等；旧待发结果在规则和门禁变化后不能跨代重试。
- 三轮基线 P95 为 0.0380/0.0327/0.0428ms，修改版为 0.0449/0.0386/0.0400ms；最大值分别 0.1939 / 0.2900ms。每轮进程 CPU 均 31.25ms（粗粒度），完整轮次均 1、身份查询均 1001（含预热）、稳态事件均 0、线程均 8。修改版每 1000 次分配增加约 80KB，来自固定大小观测值；私有内存按轮仍增长，短采样没有证明 GC 后长期平台，留给 Soak。不能把相近的 P95 数值表述为性能提升。

## T5 证据与自审

- `t5-behavior-red.trx` 0/5/0、exit 1，实际确认首次坏配置被默认主日志当成功、旧级别丢失、无规则采用摘要、解析类别缺失，以及暂停仍称计划正常。`t5-snapshot-architecture-red.trx` 0/1/0 是缺失有限快照的契约 RED；编译期间错误计数名称修正不计行为 RED。
- `t5-light-failure-red.trx` 0/1/0、exit 1：轻路径发现配置失效后无日志；现只提交 failureKind/未知计划事实，不新增系统查询。`t5-initialization-red.trx` 0/1/0、`t5-initialization-boundary-red.trx` 0/2/0 复现目录占位被当作缺失写入及根路径阻塞使循环提前退出；改为仅 confirmed missing 初始化，失败进入 unavailable；确认缺失时仍能首次生成配置。
- 中间 `t5-core-green.trx` 19/2/0 发现采用摘要的相同数量文案被通用文本去重抑制；这些已有语义确认的三个状态事件改由 Tracker 独占去重，不经过文本聚合。timestamp 测试改按明确事件取行，仍精确核对原 timestamp；不把新增 ConfigReloaded 当作失败。`t5-engine-regression.trx` 323/2/0 是两个旧结构化夹具没有有效配置；为其建立真实已提交配置，保留原结构化字段/handled/观测时间断言，首次无效回退另由新测试证明。
- GREEN（均 exit 0）：`t5-target-green.trx` 65/0/0；`t5-reasons-green.trx` 15/0/0；`t5-load-io-green.trx` 3/0/0；最终 `t5-engine-complete.trx` 327/0/0、`t5-configuration-regression.trx` 435/0/0、`t5-architecture-final.trx` 60/0/0。受控独占文件锁确认 IO 类别不复用缓存；真实 ACL、真实首次启动留给桌面验收。
- 自审：快照四个字段、值类型、短锁且无旧 GuardConfig；主不可用直接回退，主失败尝试计数不增加。有效规则读取按缓存指纹识别同数量更换，主题不刷摘要，恢复只一次；所有事件仍走唯一 WriteLog/EnqueueLog。原因回归覆盖身份未知/空名单/非法/未命中/暂停/人工有效及到期/生命周期阻止/最终失败/探测失败；心跳不推策略、不额外探测，失败/未知不声称成功，拒绝不推进既有心跳窗口。完整观测与配置摘要最多各既有周期尝试一次，不建立历史或重试任务。

## T6 证据与自审

- `t6-storage-red.trx` 0/7/0、exit 1，实际确认四类活动越界、整批假确认、环形字节无界、应急绕过互斥；初次断言错误输出过大，证据保留，随后断言只输出字节计数。`t6-crash-budget-red.trx` 0/1/0 复现未知主日志预算仍追加；`t6-startup-history-red.trx` 0/2/0 复现较新回退不可见、回退页无游标。`t6-architecture-red.trx` / `t6-maintenance-red.trx` 是缺失统一入口的契约 RED。
- `t6-tail-boundary-red.trx` 0/1/0、exit 1，真实输出完整尾行之外的两份截断中文首行；修复共享 scanner，不把片段冒充事件。`t6-history-window-red.trx` 首次单源场景实际 PASS；扩大为主/回退交错后 `t6-history-window-mixed-red.trx` 0/1/0，50 条仅返回 40，保存两份当前边界/源偏移后恢复连续分页。中间 fresh catalog 使小文件追加后的 prefix identity 改变，`t6-history-window-green.trx` 20/1/0；恢复既有 reader 固定身份生命周期，不新增 identity 算法，之后 `t6-history-window-final.trx` 21/0/0。编译错误不计行为 RED。
- GREEN（exit 0）：`t6-storage-green.trx` 20/0/0、`t6-maintenance-green.trx` 9/0/0、`t6-architecture-green.trx` 8/0/0；首次全项目 `t6-configuration-regression.trx` 446/0/0、`t6-engine-regression.trx` 327/0/0、`t6-logview-regression.trx` 61/0/0。新增分页及其最终回归将另记；不复用旧 TRX 声称新代码通过。
- 自审：附加目录稳定预算 4MiB、替换瞬时最多多 1MiB；无法修改或候选到顶报告未完成。日常巨大尾读不超过 1MiB、只留完整 UTF-8 行；紧急只零等待互斥及固定文件操作，无扫描/轮转/递归。统计不读取文件、不把截断 marker 算原记录；原 eventId 跨回退保持。正式 Engine 公共构造维护四种权威路径，内部隔离 harness 默认仅本地 startup，避免维护用户真实 crash 文件。主来源与回退来源集合仍独立，普通阅读未纳入 emergency/crash；查询快照复用 catalog。真实跨进程磁盘故障、总体性能与桌面验收尚待 T9。
- 最终阶段回归（exit 0）：`t6-logview-final.trx` 62/0/0、`t6-architecture-final.trx` 61/0/0、`t6-settings-final.trx` 13/0/0（含既有 LogWorkbenchPerformance）；不同换行使用真实行末偏移，每源最多一次补充行末读取，不按事件重复打开文件。StartupFallback 改为活动源的计算投影；阶段 UTF-8/无 BOM 与 diff 检查完成后提交。
