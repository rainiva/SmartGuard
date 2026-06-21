# SmartGuard 设置补全规划

> 基于原型对照与产品边界讨论（托盘承载状态、设置保持精简）整理。  
> 日期：2026-06-21

## 目标

在**不膨胀日常配置面**的前提下，补齐用户「调乱后能恢复」与「反馈问题时能自助导出」两条路径。

## 范围

### 纳入（P0–P1）

| 优先级 | 功能 | 放置位置 | 理由 |
|--------|------|----------|------|
| P0 | 恢复默认策略 | 关于页 · 维护区 | 阈值调乱后的安全网；引擎已有 `GuardConfig.CreateDefault` |
| P1 | 导出诊断包 | 关于页 · 维护区 | 开源项目收 Issue / 远程排查；低频高价值 |

### 明确不做

| 类别 | 项 | 原因 |
|------|-----|------|
| 首页/概览 | 守护状态条、电池 gauge、场景快照 | 托盘已承担状态入口 |
| 配置暴露 | HeartbeatIntervalMin、LogMaxBytes、亮度重试 | 不影响策略行为；改 JSON 即可 |
| 预设/帮助 | 省心/标准/极客、FAQ 大页 | 产品逻辑简单；维护成本高 |
| 愿景能力 | 应用规则、时间规则、智能调度、建议 | 引擎无对应模块 |
| 设置内状态 | 服务状态/心跳卡片 | 与托盘重复 |

### 已完成（本迭代 UI）

| 项 | 说明 |
|----|------|
| 日志工具栏视觉统一 | 筛选 + 时间 + 操作收入 `logToolbarPanel`，与搜索框同套边框语言 |
| 软件图标 | 恢复 `lib/SmartGuard.ico` 至仓库原版 |

---

## 功能一：恢复默认策略（P0）

### 行为定义

1. 关于页新增 **「恢复默认策略」** 按钮（次要按钮样式，旁附说明文案）。
2. 点击后弹出确认对话框：
   - 标题：`恢复默认策略？`
   - 正文：说明仅重置守护配置项，**不删除**日志与学习数据（若未来有）。
   - 按钮：`恢复` / `取消`
3. 用户确认后：
   - 以 `GuardConfig.CreateDefault(root)` 为基准生成新配置；
   - **保留**当前文件中的 `LogFile` 路径（避免日志路径被改没）；
   - 通过现有 `SettingsSaveCoordinator.Save` 写入；
   - 刷新设置窗体各控件至默认值；
   - 显示成功 Toast：`已恢复默认策略`。
4. 用户取消：无任何写入。

### 默认项对照（须与 `CreateDefault` 一致）

| 字段 | 默认值 |
|------|--------|
| BalancedThresholdSec | 300（UI 显示 5 分钟） |
| PowerSaverThresholdSec | 900（15 分钟） |
| LowBatteryPercent | 30 |
| CheckIntervalSec | 15 |
| BrightnessRestoreMs | 300 |
| AutoStartEnabled | true |
| NotifyOnPlanChange | true |
| Paused | false |

不重置：`ManualHighPerformanceUntil`（若正在手动高性能，可选择保留或清除——**建议清除**并在验收中写明）。

### 实现要点

- 新类：`SettingsResetCoordinator` 或 `GuardConfigResetService`（纯逻辑，可单测）。
- UI 接线：`SettingsWindowController` 关于页按钮 + 确认对话框。
- 不新增导航页。

### 验收标准

- [ ] 关于页可见「恢复默认策略」及一行灰色说明（≤ 40 字）。
- [ ] 未确认时，`SmartGuard.config.json` 内容不变。
- [ ] 确认后，配置文件上述字段恢复默认；`LogFile` 路径不变。
- [ ] 设置窗体滑块/开关即时反映默认值，无需重启 Settings。
- [ ] 引擎在下一周期读取新配置（现有热读机制，无需额外「重新加载」按钮）。
- [ ] 单元测试：`GuardConfigResetService` 覆盖「保留 LogFile」「映射默认值」「清除 ManualHighPerformanceUntil（若采用）」。
- [ ] 集成测试：Settings 控制器模拟确认后写入并刷新 UI。
- [ ] `dotnet test Tests/SmartGuard.Settings.Tests` 全绿。

---

## 功能二：导出诊断包（P1）

### 行为定义

1. 关于页新增 **「导出诊断包…」** 按钮。
2. 点击后打开系统「另存为」对话框，默认文件名：  
   `SmartGuard-diagnostic-yyyyMMdd-HHmmss.zip`
3. 压缩包内容（UTF-8 文本，缺失文件跳过并在 manifest 注明）：

| 路径（包内） | 来源 |
|--------------|------|
| `manifest.json` | 导出时间、应用版本、OS 版本、包含/缺失文件列表 |
| `config/SmartGuard.config.json` | 当前配置（**剔除** `GitHubToken` 或置空） |
| `status/SmartGuard.status.json` | 最近状态快照（若存在） |
| `logs/SmartGuard.log` | 主日志；若超过 512KB 仅打包尾部 512KB 并注明 |
| `logs/SmartGuard.startup.log` | 若存在 |

4. 导出成功：Toast `诊断包已保存`；失败：MessageBox 显示可读错误。

### 安全与边界

- 不得包含 Token、密码类字段。
- 不拉起管理员权限；用户自选保存路径。
- 磁盘满 / 路径无写权限时失败有明确提示。

### 实现要点

- 新类：`DiagnosticBundleExporter`（`System.IO.Compression.ZipArchive`）。
- 可选依赖：无新 NuGet；版本号从现有 About 页逻辑复用。

### 验收标准

- [ ] 关于页可见「导出诊断包…」按钮。
- [ ] 成功导出 zip，manifest 含版本与时间戳。
- [ ] 包内 config **无**有效 `GitHubToken`。
- [ ] 缺失 status/log 时不崩溃，manifest 标记 `missing`。
- [ ] 主日志 > 512KB 时仅尾部入包，manifest 记录 `truncated: true`。
- [ ] 单元测试：`DiagnosticBundleExporter` 对临时目录 fixture 验证文件清单与脱敏。
- [ ] 手动验收：解压 zip 可读懂，可用于 GitHub Issue 附件。
- [ ] `dotnet test` 相关项目全绿。

---

## 日志工具栏视觉统一（已完成）

### 设计

- 搜索框下方单一 `logToolbarPanel` 容器（与搜索框同边框/圆角/背景）。
- 第一行：左「级别」复选框组，右「时间」下拉 + 区分大小写。
- 分隔线 `logToolbarDivider`。
- 第二行：左操作按钮组，右「跟随最新」+「刷新」。
- 按钮使用 `LogToolbarCompactButton`，避免与关于页大按钮视觉权重冲突。

### 验收标准

- [ ] 筛选、时间、操作在同一边框容器内，不再三行漂浮。
- [ ] 所有既有控件 `x:Name` 不变，日志功能回归通过。
- [ ] `LogToolbarLayoutTests` 与 `SettingsWindowControllerTests` 全绿。
- [ ] 窄窗口（≥ MinWidth 600）下筛选行可换行或压缩，不出现控件重叠（手动看一眼）。

---

## 软件图标（已完成）

### 验收标准

- [ ] `lib/SmartGuard.ico` 与 `b47e878` 提交中版本一致（约 40KB，非 prototype 新图标）。
- [ ] Engine / Tray / Settings / LogViewer 构建后任务栏与 exe 图标均为旧版（手动打开 Settings 与 Tray 确认）。

---

## 建议实施顺序

1. ~~日志工具栏 UI + 图标恢复~~（本批）
2. P0 恢复默认策略（TDD）
3. P1 导出诊断包（TDD）
4. 可选：打 patch 安装包 + 安装冒烟测试

## 测试命令

```batch
dotnet test Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj
Run-Tests.cmd
```
