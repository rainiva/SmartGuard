# ADR 0001：日志工作台架构与唯一真源

**状态：** 已接受（P0-01 登记，阶段未关闭）  
**日期：** 2026-08-02  
**关联：** [ARCHITECTURE-CONTRACT.md](../ARCHITECTURE-CONTRACT.md) §23；计划 `docs/superpowers/plans/2026-08-02-log-workbench-p0-p2.md`

---

## 背景

SmartGuard 日志页已具备 `SmartGuardLogWriter`、`LogTailReader`、`LogQueryService`、`GuardConfigRepository`、`DiagnosticBundleExporter` 等真源，但存在以下架构风险：

1. **视口多所有者：** `SettingsLogViewRefresher`、`SettingsLogFollowTailCoordinator`、`SettingsLogScrollCoordinator` 与列表呈现层共同改变同一 `ScrollViewer` offset，刷新、跟随与用户拖拽互相覆盖。
2. **历史读取为固定预算快照：** `LogTailReader.ReadInitialView` 仅载入最近约 256KB，无法稳定表达“还有更早记录”，也无法增量向旧归档翻页。
3. **结构化能力未成为页面数据真源：** JSONL、索引与查询服务已存在，但页面仍以字符串 `Contains` 为主；诊断 JSONL 多为导出时反解析。
4. **第二实现诱惑：** 为解决上述问题，常见冲动是新建 V2 页面、并行 reader、sidecar JSONL 文件或 UI 线程直读磁盘——均会违反 AGENTS.md 架构真源规则。

本 ADR 在 P0 开始前冻结：**扩展现有真源职责**，而不是引入并行通道。

---

## 决定

1. **历史读取：** `LogTailReader`（Settings）是唯一 configured 主日志与归档历史读取实现；后续 cursor 分页在其实现 `ILogEventSource`，禁止新增第二套主日志 `FileStream` reader。
2. **查询与索引：** `LogIndexBuilder` + `LogQueryService`（Configuration）是唯一日志查询真源；UI 与诊断导出准备均消费同一事件/索引模型，不得另建查询服务。
3. **配置持久化：** 日志筛选器、视图偏好与相关 config 字段只经 `GuardConfigRepository` 读写，不得新增旁路 Save API。
4. **诊断导出：** `DiagnosticBundleExporter` 是唯一诊断包 zip 真源；`logs/SmartGuard.jsonl` 为导出视图而非第二实时日志源。
5. **物理追加（后续切片）：** configured 主日志物理追加只经 `SmartGuardLogWriter`；本 ADR 登记所有权，具体 typed-event 与 writer 收口在 P0-03 同切片 RED→GREEN。
6. **视口（后续切片）：** `LogViewportController` 将成为日志 `ScrollViewer` 唯一滚动命令所有者；本 ADR 仅登记方向，门禁在 P0-06 启用。
7. **规范事件格式：** versioned JSONL 为结构化事件规范格式；旧纯文本经 `LogEventDecoder` 兼容解析，至少保留一个发布周期的混合读取能力。
8. **单向数据流：** component → factory/pipeline → writer → tail reader → query → bounded window → presenter → viewport controller。
9. **不引入：** 第三方日志/虚拟列表库；日志列表自定义惯性滚动；实时 sidecar `SmartGuard.jsonl` 双写。

架构契约登记见 [ARCHITECTURE-CONTRACT.md](../ARCHITECTURE-CONTRACT.md) §23；门禁测试见 `LoggingWorkbenchArchitectureTests`。

---

## 备选方案

### 方案 A：一次性加载全部历史到内存

- **描述：** 进入日志页时将当前日志与全部 `.bak` 归档读入 `ObservableCollection`。
- **否决原因：** 归档总量可达数百 MB；内存与 UI 线程成本不可控；违背有界窗口与增量分页目标；与 WPF 容器复用策略冲突。

### 方案 B：新建 SmartGuard.LogViewer V2 页面与并行 reader

- **描述：** 保留旧日志页，新建独立模块/入口承载工作台能力。
- **否决原因：** 违反“不恢复 LogViewer 产品入口”与单页演进约束；必然出现第二 reader、第二刷新管线与重复 config 写入路径；增加维护与测试分裂。

### 方案 C：主日志旁路实时 JSONL sidecar 双写

- **描述：** 继续写纯文本主日志，同时实时追加 `logs/SmartGuard.jsonl`。
- **否决原因：** 非原子双写；轮转与截断时两文件易失同步；诊断 JSONL 与运行态真源混淆；违背单一物理日志裁决（计划 §2.2.1）。

**选用方案：** 在现有 Settings 日志页与既有 Configuration/Settings 真源上增量演进（本 ADR「决定」节）。

---

## 后果

### 正面

- 架构门禁可阻止第二 reader/query/exporter/config 通道在 P0 期间悄然引入。
- 团队对齐单一数据流，后续 `LogEvent`、cursor、viewport 切片有明确落点。
- 兼容期策略明确，降低一次性格式切换风险。

### 负面 / 成本

- `LogTailReader`、`LogQueryService` 等类型职责加重，需配套测试与文档切片（P0-02～P0-08）。
- 迁移期需同时维护 legacy 文本与 JSONL 解码路径，短期代码复杂度上升。
- 视口收口前（P0-06 前）仍可能存在短期多命令源，需按切片顺序消化。

---

## 兼容性

- **日志格式：** P0-03 之前 UI 必须能显示混合 legacy 文本与 JSONL（adapter/decoder）；旧文本行 EventId 使用稳定 `l:<SHA-256>` 规则，不因归档 rename 改变。
- **读取 API：** 现有 `ReadInitialView` 在 cursor 分页落地前保持可用；新 API 为 additive，不删除既有公共入口直至页面切换完成。
- **配置字段：** 新增筛选/视图字段仍走 `GuardConfigRepository` 与契约登记，不破坏现有 config 加载。
- **诊断包：** 导出范围扩展不改变“剔除 GitHub Token 字段”等既有脱敏规则。

---

## 回滚

1. **P0 阶段内：** 若 cursor/视口/写入切片无法在同一发布周期收敛，可停止后续 P0 切片合并，保留 P0-01 契约与 ADR；页面继续以现有 256KB 快照与字符串过滤运行。
2. **Writer 切换后：** 若 JSONL 实时写入引发不可接受兼容问题，回退 writer 切片（P0-03）到纯文本追加，保留 decoder 读取已写入 JSONL 行的能力；不在回滚中删除用户磁盘上的 JSONL 行。
3. **契约回滚：** 移除 §23 开放登记项前必须先删除或改写依赖门禁测试，并更新本 ADR 状态为「已 supersede」；禁止仅删断言而不恢复真源。

---

## 参考

- `docs/ARCHITECTURE-CONTRACT.md` §22（阶段四）、§23（日志工作台 P0）
- `Tests/SmartGuard.Architecture.Tests/LoggingWorkbenchArchitectureTests.cs`
