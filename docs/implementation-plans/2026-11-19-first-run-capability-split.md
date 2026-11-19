# 首次初始化：能力探测与一次性对齐拆分

**状态：** 用户于 2026-11-19 选定方案“探测与对齐拆开”，按本文实施。前置：电源方案 API 第一阶段（`GuardianFirstRunInitializer` 已改为只读探测与确定的失败语义）。

## 1. 问题（均早于本次迁移）

1. **入口缺失：** `GuardianLoop.RunAsync` 对 `InitializeIfNeeded` 的调用在 `7be38e78`（日志重构）中被删除，提交说明未提及，没有任何文档或测试记录这是有意删除。因此生产中首次初始化从不运行。
2. **结果到不了 Runner：** `GuardianIterationRunner` 在 `GuardianLoop` 构造时取得 `powerCfgBrightnessSupported` 的**值拷贝**，初始化器之后经 `ref` 改的是 `GuardianLoop` 的字段（`11e453fc` 中即如此）。“不支持亮度设置项 → WMI-only 模式”从未对守护轮次生效。
3. **能力不持久化：** 完成标记写入后，后续启动直接返回，能力恢复默认值“支持”。

被破坏的不变量：亮度设置项能力是本机事实，应在每次启动时以只读方式确定，且守护轮次读取的必须是同一事实；一次性写入（关闭自适应亮度、三方案亮度对齐）才需要完成标记。

## 2. 设计

| 事实/动作 | Owner | 时机 | 语义 |
|-----------|-------|------|------|
| 亮度设置项能力 | `GuardianLoop` 字段 `_powerCfgBrightnessSupported`，由 `GuardianFirstRunInitializer` 在启动时写入 | **每次启动**，在进入守护循环前，只读一次 | 成功读取为支持；Win32 码 2 为不支持；其他失败保持当前值（默认支持）并告警 |
| 一次性对齐写入 | `GuardianFirstRunInitializer` | 仅当没有完成标记且能力为“支持” | 语义同第一阶段 Task 6：任一写入失败不写标记、下次启动重试 |
| Runner 读取能力 | `GuardianIterationRunner` | 每次切换前 | 构造参数由 `bool` 改为 `Func<bool>`；`GuardianLoop` 传入读取上述字段的委托，Runner 不保存副本 |

- **入口：** `GuardianLoop.RunStartupInitialization(GuardConfig)`（internal），由 `RunAsync` 在 `LoadConfig` 与启动日志之后、守护循环之前调用一次。
- **已有安装：** 完成标记已存在，因此每次启动只多一次只读探测（实测 `PowerReadACValueIndex` 毫秒级），没有写入。
- **全新安装：** 首次启动执行至多 1 次读取与 12 次写入，每次经执行器、截止 2 秒。超时后门被持有，后续调用立即返回 `call-pending` 并在第一处失败停止，因此启动阻塞上界约 2 秒加 WMI 亮度读取预算。
- **并发与生命周期：** 启动初始化在守护循环之前同步执行，此时尚未注册电源事件监听，无并发轮次；不新增线程、队列或定时器。
- **不变：** 完成标记路径（`SmartGuardPaths.InitMarkerFile`）、卸载时删除标记、安装时不创建标记。

## 3. 实施步骤（TDD）

1. **重构（无行为变化）：** Runner 参数改为 `Func<bool> powerCfgBrightnessSupported`；`GuardianLoop` 传 `() => _powerCfgBrightnessSupported`，测试调用点传 `() => true/false`。全量 Engine 测试保持通过，计数不变。
2. **RED：**
   - 初始化器：完成标记已存在时仍只读探测并更新能力（码 2 → 不支持），不写入。
   - `GuardianLoop`：存在 `RunStartupInitialization`，调用后循环字段反映探测结果；`RunAsync` 源码在守护循环前调用它（架构门禁）。
   - Runner：能力委托在两轮之间由支持变为不支持后，第二轮不再写亮度设置项（保护性用例，重构后即通过，如实记录）。
3. **GREEN：** 初始化器改为“先探测、再判断标记”；`GuardianLoop` 增加入口并在 `RunAsync` 中调用。
4. **验证：** Engine、Architecture 全量；`Run-Tests.ps1`；契约“初始化标记”行补充能力语义。真实全新安装路径需卸载后重装，默认标 `NOT RUN`，除非用户另行执行。
