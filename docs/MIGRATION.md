# SmartGuard 迁移规划

本文档整理自 DevGuard 流程下的完整迁移方案（原对话 Task Contract），并标注截至当前的**实施状态**。

**制定日期：** 2026-06-15  
**批准决策：** net8.0 · 仅 Core C# 化 · 框架依赖发布 · 项目迁至 `D:\Project\SmartGuard` 并更名为 SmartGuard  
**参考来源：** 桌面 `新建 文本文档.txt` / `新建 文本文档 (2).txt`（C# 引擎分层建议）

---

## Execution Summary

| 项 | 内容 |
|----|------|
| **Task** | 将 SmartPowerPlan 迁移为 SmartGuard：C# 四件套 + Inno 安装包；**运行时不再依赖 PS 应用栈**（Phase 6） |
| **Mode** | `STRICT` |
| **Skill chain** | project-understanding → impact-analysis → task-contract-freeze → TDD → migration-refactor → code-review → release-check |
| **Status** | Phase 5 **5.1–5.2 已完成**；Phase 6 **6.1–6.5 已完成**；Phase 7 **7.1 已完成** |
| **Next** | Phase 7.2 诊断 cmd；Phase 5.3 **VM/人工**验收 V1、V4/V5、V7b、V8、V9 |

---

## Risk Note

| 焦点 | 说明 |
|------|------|
| 配置/状态契约 | `SmartGuard.config.json`、`SmartGuard.status.json` 字段须与托盘只读逻辑兼容 |
| 功能差距 | 参考文档为双计划简化版；本项目为**三计划 + 电池 + 暂停 + Toast + 外部变更检测** |
| 权限与多实例 | Core 需管理员；Mutex 名 `Global\SmartGuard.Core` |
| 计划任务 | 切换引擎后须重新注册任务；旧 `C:\Tools` 任务已卸载 |

---

## 一、背景与目标

### 1.1 为何迁移到 C#

| 维度 | PowerShell | C# Engine |
|------|------------|-----------|
| 内存 | ~30–50 MB | ~5–15 MB |
| 启动 | 500ms–2s | &lt;100ms（已编译） |
| API 调用 | Add-Type / 脚本拼接 | P/Invoke、强类型 |
| 常驻稳定性 | 一般 | Win32 进程更稳 |
| Windows Service | 困难 | 可扩展（Phase 3） |

### 1.2 架构原则

```
┌─────────────────────────────────────────┐
│  展示层（C# exe，Phase 3–4）             │
│  Tray / Settings / LogViewer            │
└──────────────────┬──────────────────────┘
                   │ config.json + status.json
┌──────────────────▼──────────────────────┐
│  引擎层（C#）                            │
│  SmartGuard.Engine.exe                  │
│  空闲检测 / 策略 / powercfg / WMI / 日志  │
└─────────────────────────────────────────┘
```

**原则（Phase 6 后）：** 用户面与计划任务注册均为 C#；PowerShell 仅用于**构建、测试与安装包制作**（`scripts/`、`installer/`、`Run-Tests.ps1`）。

---

## 二、现状对照（迁移前 SmartPowerPlan）

| 层级 | 原实现 | 原路径 |
|------|--------|--------|
| Core 守护 | PS 常驻循环 | `lib/SmartPowerPlan.Core.ps1` |
| 领域策略 | 三计划 + 电池 + 暂停 | `lib/layers/Domain.PowerPlan.ps1` |
| 幂等/亮度 | powercfg + WMI 重试 | `Domain.Idempotency`, `Infrastructure.PowerCfg` |
| 日志 | 滚动 + 去重指纹 | `Infrastructure.Logging.ps1` |
| 状态 IPC | status JSON | `SmartPowerPlan.status.json` |
| 托盘 | WinForms | `lib/SmartPowerPlan.Tray.ps1` |
| 设置 | WPF Fluent UI | `SmartPowerPlan.Settings.ps1/.xaml` |
| 日志查看 | 独立隐藏 PS 进程 | `Presentation.LogViewer.ps1` |
| 部署 | 计划任务 | `Register-SmartPowerPlanTask.ps1` |
| 测试 | Pester | `Tests/SmartPowerPlan.Tests.ps1` |
| 项目根目录 | `C:\Tools` | — |

---

## 三、Task Contract（冻结范围）

### 3.1 目标（Goal）

用 C# 实现**引擎 + 桌面四件套**；计划任务由 `ScheduledTaskRegistrar`（C#）注册（Phase 6.1）。

### 3.2 已批准技术决策

| ID | 决策 | 选定值 |
|----|------|--------|
| D1 | .NET 版本 | **net8.0** |
| D2 | Phase 1 范围 | **仅 Core 引擎** |
| D3 | 日志文件名 | **`SmartGuard.log`**（2026-06 由 `SmartPowerPlan.log` 更名） |
| D4 | 发布形态 | **非 self-contained**（`win-x64`，依赖本机 .NET 8 运行时） |

### 3.3 非目标（Non-Goals）

| 阶段 | 不做 |
|------|------|
| Phase 1 | 不重写托盘、设置、日志查看器；不引入 Windows Service；不加全局热键 |
| Phase 2 | 不强制引入 NLog/Serilog（优先零依赖内置滚动日志） |
| Phase 3 | `/install` 安装器为可选，不阻塞 Phase 1 |

### 3.4 配置与文件契约

| 文件 | 路径 | 说明 |
|------|------|------|
| 配置 | `SmartGuard.config.json` | 字段名与旧版兼容，见下表 |
| 状态 | `SmartGuard.status.json` | Tray 读取；含可选 `notificationEvent` |
| 日志 | `SmartGuard.log` | 主运行日志；按日归档为 `SmartGuard.log.{yyyyMMdd}.bak`（保留 7 天）；超大时同日归档 |
| 启动日志 | `SmartGuard.startup.log` | 启动器 / 回退诊断 |
| 初始化标记 | `.SmartGuard.initialized` | 首次 powercfg 对齐标记 |

**配置字段（须保持兼容）：**

`ActivePlanGUID`, `BalancedPlanGUID`, `PowerSaverPlanGUID`, `BalancedThresholdSec`, `PowerSaverThresholdSec`, `LowBatteryPercent`, `CheckIntervalSec`, `BrightnessRestoreMs`, `LogFile`, `Paused`, `LogMaxBytes`, `BrightnessRetryCount`, `BrightnessRetryDelayMs`, `NotifyOnPlanChange`, `HeartbeatIntervalMin`, `AutoStartEnabled`

### 3.5 计划任务命名

| 旧名称 | 新名称 | 状态 |
|--------|--------|------|
| `SmartPowerPlan Guardian` | `SmartGuard Guardian` | 需在新路径重新注册 |
| `SmartPowerPlan Tray` | `SmartGuard Tray` | 需在新路径重新注册 |
| （旧任务） | — | 已通过 `scripts/Unregister-LegacySmartPowerPlanTasks.ps1` 卸载 |

---

## 四、功能对照表

| 功能 | 参考文档 | 原 PS | C# Engine | 状态 |
|------|----------|-------|-----------|------|
| 双计划 | ✓ | — | — | 不适用（本项目三计划） |
| 三计划策略 | — | ✓ | ✓ | **已完成** |
| 电池低电量策略 | — | ✓ | ✓ | **已完成** |
| 暂停 `Paused` | — | ✓ | ✓ | **已完成** |
| 外部计划变更检测 | — | ✓ | ✓ | **已完成** |
| 亮度锁定（powercfg + WMI 重试） | ✓ | ✓ | ✓ | **已完成** |
| Toast 通知 | — | ✓（Tray） | 写 `notificationEvent` | **已完成** |
| 日志滚动 | NLog 建议 | 自实现 | `FileLogger` | **已完成**（基础版） |
| 单实例 Mutex | ✓ | ✓ | ✓ | **已完成** |
| 结构化日志级别 | Phase 2.1 | ✓ | ✓ | **已完成** |
| 按日归档 7 天 | Phase 2.2 | ✓ | — | **已完成** |
| 电源事件驱动 | Phase 2.3 | — | ✓ | **已完成** |
| 托盘 C# 化 | Phase 3.2 | PS | ✓ | **已完成** |
| `/install` 安装器 | Phase 3.1 | — | ✓ | **已完成** |
| 全局热键 | 可选 | — | — | **不做** |

---

## 五、分期执行方案

### Phase 1：C# 核心引擎 + 项目迁移（已完成）

#### 5.1 工程结构（当前）

```
D:\Project\SmartGuard\
├── src/SmartGuard.Engine/
│   ├── Config/GuardConfig.cs
│   ├── Domain/PolicyEngine.cs
│   ├── Infrastructure/
│   │   ├── IdleDetector.cs
│   │   ├── PowerCfgExecutor.cs
│   │   ├── BrightnessService.cs
│   │   ├── FileLogger.cs
│   │   ├── LogLevel.cs
│   │   ├── LogLineFormatter.cs
│   │   ├── LogArchivePlanner.cs
│   │   ├── PowerEventWakeListener.cs
│   │   ├── StatusPublisher.cs
│   │   ├── SingleInstanceGuard.cs
│   │   ├── BatteryInfoProvider.cs
│   │   ├── BatteryStatusInterpreter.cs
│   │   └── ...
│   ├── Worker/GuardianLoop.cs
│   └── Program.cs
├── tests/SmartGuard.Engine.Tests/    # xUnit，32 项
├── lib/layers/Infrastructure.Battery.ps1
├── lib/                              # PowerShell 壳层（保留）
├── bin/SmartGuard.Engine.exe         # dotnet publish 输出
└── scripts/
    ├── Publish-Engine.ps1
    ├── Measure-EngineStartup.ps1
    ├── Measure-EngineMemory.ps1
    ├── Migrate-RenameToSmartGuard.ps1
    └── Unregister-LegacySmartPowerPlanTasks.ps1
```

#### 5.2 Phase 1 交付清单

- [x] 项目自 `C:\Tools` 迁至 `D:\Project\SmartGuard`
- [x] 全局更名 SmartPowerPlan → SmartGuard（脚本、函数、任务名）
- [x] `SmartGuard.Engine` net8.0 实现主循环
- [x] `Register-SmartGuardTask.ps1` 优先注册 exe，无 exe 时回退 PS Core
- [x] `Start-Core.ps1` 优先启动 C# 引擎，无 exe 时回退 PS Core
- [x] `Register-TrayTask.ps1` 路径自 `$PSScriptRoot` 解析（去除硬编码）
- [x] 引擎 `OutputType=WinExe`（开机无控制台弹窗）
- [x] 电量读取对齐系统 API（`BatteryStatusInterpreter` + `Infrastructure.Battery.ps1`）
- [x] `Run-Tests.ps1`：Pester 63 + xUnit 32
- [x] `README.md`、`lib/README-DEPLOY.txt`
- [x] 卸载旧 `C:\Tools` 计划任务
- [x] 日志文件统一为 `SmartGuard.log`

#### 5.3 部署命令

```powershell
cd D:\Project\SmartGuard
powershell -File scripts\Publish-Engine.ps1
powershell -ExecutionPolicy Bypass -File Register-SmartGuardTask.ps1
powershell -File Register-TrayTask.ps1
powershell -File Restart-Tray.ps1
```

或一键：`Setup-All.cmd`（含编译、测试、注册任务）。

#### 5.4 验收标准（Phase 1）

| # | 验收项 | 状态 |
|---|--------|------|
| A1 | 空闲 5min → 平衡；15min → 节能；活跃+插电 → 高性能 | 已实现 + 单测 |
| A2 | 低电量（&lt;30%）+ 活跃 → 平衡 | 已实现 + 单测 |
| A3 | `Paused=true` 不自动切计划 | 已实现 + 单测 |
| A4 | 切计划前后亮度锁定 | 已实现 |
| A5 | 稳态内存 &lt; 20MB | **已观测**（net8 WinExe；可用 `scripts/Measure-EngineMemory.ps1` 现场确认） |
| A6 | 启动到首条日志 &lt; 500ms | **已观测** ~150–240ms（`scripts/Measure-EngineStartup.ps1`） |
| A7 | 双开 Core 第二实例退出 | Mutex 已实现 |
| A8 | Tray 读 status、Toast、设置、日志 | PS 壳层保留 |
| A9 | 计划任务指向新 exe | **已验收**（需 `Register-SmartGuardTask.ps1` 重新注册） |
| A10 | 回滚至 PS Core | `lib/SmartGuard.Core.ps1` 保留 |

---

#### 5.5 Phase 1 收尾（2026-06-16）

- [x] `Start-Core.ps1` 与 `Register-TrayTask.ps1` 对齐规划行为
- [x] 计划任务 / 手动启动均优先 C# 引擎
- [x] 电量显示与 Windows 任务栏对齐（系统 API 优先于 WMI）
- [x] 验收脚本：`scripts/Measure-EngineStartup.ps1`、`scripts/Measure-EngineMemory.ps1`
- [x] 本文档与测试数量同步更新

---

### Phase 2：日志增强 + 电源事件

| 子阶段 | 模块 | 方案 | 状态 |
|--------|------|------|------|
| **2.1** | 结构化日志 | `FileLogger` + `LogLineFormatter`；级别 INFO/WARN/ERROR/HEART | **已完成** |
| **2.2** | 按日归档 | `{LogFile}.{yyyyMMdd}.bak`，保留 7 天 | **已完成** |
| **2.3** | 电源事件 | `SystemEvents.PowerModeChanged` 立即重新评估策略 | **已完成** |

**Phase 2 总验收：** 插拔电源后 &lt; 1s 重新评估（2.3）；日志按日滚动（2.2）。**已达成。**

---

### Phase 3：托盘 C# 化 + 安装器（Contract 已冻结）

完整切片见 **[`docs/PHASE-3-TASK-CONTRACT.md`](PHASE-3-TASK-CONTRACT.md)**。

| 子阶段 | 代号 | 方案 | 状态 |
|--------|------|------|------|
| 3A | 基线 | 托盘继续 PowerShell（当前生产态） | **当前默认** |
| **3.1** | 3C | `SmartGuard.Engine.exe --install` / `--uninstall` | **已完成** |
| **3.2** | 3B-core | C# `SmartGuard.Tray.exe` + Contracts；设置/日志仍调 PS | **已完成** |
| **3.3** | 3B-toast | C# 原生 Toast + Balloon 回退 | **已完成** |

---

### Phase 4：设置 / 日志查看器 C# 化（规划待批准）

完整切片见 **[`docs/PHASE-4-TASK-CONTRACT.md`](PHASE-4-TASK-CONTRACT.md)**。

| 子阶段 | 代号 | 方案 | 状态 |
|--------|------|------|------|
| 4A | 基线 | PS 设置 + PS 日志（当前生产态） | **当前默认** |
| **4.1** | 4D-config | `SmartGuard.Configuration` 共享库 | **已完成** |
| **4.2** | 4D-log | `SmartGuard.LogViewer.exe` | **已完成** |
| **4.3** | 4D-settings | `SmartGuard.Settings.exe`（端口 XAML） | **已完成** |
| **4.4** | 4D-wire | 托盘对接 + `Publish-All` | **已完成** |

---

### Phase 5：Inno Setup 安装包

完整契约见 **[`docs/INNO-INSTALLER-TASK-CONTRACT.md`](INNO-INSTALLER-TASK-CONTRACT.md)**。

| 子阶段 | 代号 | 方案 | 状态 |
|--------|------|------|------|
| **5.0** | 5I-decide | 运行时策略：**P5A**；H1–H6 已签署（2026-06-16） | **已签署** |
| **5.1** | 5I-stage | `installer\Build-Staging.ps1` + staging 布局 | **已完成** |
| **5.2** | 5I-inno | `installer\SmartGuard.iss` + `dist\` 产出 | **已完成** |
| **5.3** | 5I-verify | 干净 VM 验收 V1–V9 | **部分完成**（1.0.6 自动化 V2/V3/V6 + 载荷；VM/人工 V1/V4/V5/V7b/V8/V9 待办） |
| **5.4** | 5I-sign | （可选）Authenticode | 未开始 |

本机冒烟与集成测试见 [`docs/evidence/installer/`](evidence/installer/)。

---

### Phase 6：去 PowerShell 化

完整切片见 **[`docs/PHASE-6-TASK-CONTRACT.md`](PHASE-6-TASK-CONTRACT.md)**。

| 子阶段 | 代号 | 方案 | 状态 |
|--------|------|------|------|
| **6.1** | 6P-schtasks | `ScheduledTaskRegistrar`；`--install` 不再调 PS | **已完成** |
| **6.2** | 6P-launchers | cmd 启动链只走 exe | **已完成** |
| **6.3** | 6P-fallback | 删除 `lib/layers` 与 PS 应用回退 | **已完成** |
| **6.4** | 6P-packaging | 安装包去掉 `Register-*.ps1` | **已完成** |
| **6.5** | 6P-docs | 文档与证据同步 | **已完成** |

---

### Phase 7：开发机去 PowerShell

完整切片见 **[`docs/PHASE-7-TASK-CONTRACT.md`](PHASE-7-TASK-CONTRACT.md)**。

| 子阶段 | 代号 | 方案 | 状态 |
|--------|------|------|------|
| **7.1** | 7P-launchers-dev | 根目录 `Register-AllTasks.cmd` 等；删除等价 `.ps1` | **已完成** |
| **7.2** | 7P-status | `Status.cmd` 不调用 PowerShell | 未开始 |
| **7.3** | 7P-legacy | `Repair`/`Setup-All` 遗留脚本 | 未开始 |
| **7.4** | 7P-xaml | XAML 源文件化（可选） | 未开始 |
| **7.5** | 7P-publish | `dotnet publish` 链（可选） | 未开始 |
| **7.6** | 7P-docs | 文档同步 | 未开始 |

---

## 六、路径与命名对照

| 旧 (`C:\Tools`) | 新 (`D:\Project\SmartGuard`) |
|-----------------|------------------------------|
| `SmartPowerPlan.config.json` | `SmartGuard.config.json` |
| `SmartPowerPlan.status.json` | `SmartGuard.status.json` |
| `SmartPowerPlan.log` | `SmartGuard.log` |
| `SmartPowerPlan.startup.log` | `SmartGuard.startup.log` |
| `lib/SmartPowerPlan.Core.ps1` | `bin/SmartGuard.Engine.exe` |
| `lib/SmartPowerPlan.Tray.ps1` | `bin/SmartGuard.Tray.exe` |
| `Register-SmartPowerPlanTask.ps1` | `Engine.exe --install`（C# `ScheduledTaskRegistrar`） |
| 计划任务 `SmartPowerPlan Guardian` | `SmartGuard Guardian` |
| 计划任务 `SmartPowerPlan Tray` | `SmartGuard Tray` |
| Mutex `Global\SmartPowerPlan.Core` | `Global\SmartGuard.Core` |

---

## 七、测试策略

| 类型 | 位置 | 数量 |
|------|------|------|
| Pester 契约 / 安装包 | `Tests/SmartGuard.Tests.ps1` | 26 |
| Pester 集成 | `Tests/Integration/*.ps1` | 4 |
| xUnit | `Tests/SmartGuard.*.Tests/` | 157 |
| 运行 | `Run-Tests.ps1` | 串联上述全部 |

**TDD 纪律（后续 Phase 仍适用）：** Red → Green → Refactor；声称完成前 `Run-Tests.ps1` 全绿。

---

## 八、回滚方案（Phase 6 后）

1. 重新运行 `dist\SmartGuard-Setup-*-x64.exe` 覆盖安装，或  
2. `scripts\Publish-All.ps1` 后执行 `bin\SmartGuard.Engine.exe --root <安装目录> --install`  
3. 配置 / 状态 / 日志文件**无需格式迁移**

> Phase 1 **A10**（回滚至 `lib\SmartGuard.Core.ps1`）已于 Phase 6.3 **废止**。

---

## 九、相关脚本与文档

| 文件 | 用途 |
|------|------|
| [`README.md`](../README.md) | 项目概览与快速开始 |
| [`docs/PHASE-3-TASK-CONTRACT.md`](PHASE-3-TASK-CONTRACT.md) | Phase 3 安装器 + 托盘 C# 化（3.1–3.3） |
| [`docs/PHASE-6-TASK-CONTRACT.md`](PHASE-6-TASK-CONTRACT.md) | Phase 6 去 PS 化（6.1–6.5） |
| [`docs/evidence/installer/`](evidence/installer/) | 安装包冒烟与集成测试证据 |
| [`docs/PHASE-2.1-TASK-CONTRACT.md`](PHASE-2.1-TASK-CONTRACT.md) | Phase 2.1 结构化日志（已完成） |
| [`lib/README-DEPLOY.txt`](../lib/README-DEPLOY.txt) | 部署步骤 |
| [`scripts/Publish-Engine.ps1`](../scripts/Publish-Engine.ps1) | 编译发布引擎 |
| [`scripts/Measure-EngineStartup.ps1`](../scripts/Measure-EngineStartup.ps1) | Phase 1 启动耗时验收 |
| [`scripts/Measure-EngineMemory.ps1`](../scripts/Measure-EngineMemory.ps1) | Phase 1 内存验收 |
| [`scripts/Migrate-RenameToSmartGuard.ps1`](../scripts/Migrate-RenameToSmartGuard.ps1) | 一次性更名（已执行） |
| [`scripts/Unregister-LegacySmartPowerPlanTasks.ps1`](../scripts/Unregister-LegacySmartPowerPlanTasks.ps1) | 卸载旧计划任务 |

---

## 十、变更记录

| 日期 | 变更 |
|------|------|
| 2026-06-15 | DevGuard 方案起草；用户批准 D1–D4 |
| 2026-06-15 | 迁至 `D:\Project\SmartGuard`；Phase 1 实施完成 |
| 2026-06-16 | 卸载 `C:\Tools` 旧计划任务 |
| 2026-06-16 | 日志文件 `SmartPowerPlan.log` → `SmartGuard.log` |
| 2026-06-16 | 本文档 `docs/MIGRATION.md` 落盘 |
| 2026-06-16 | Phase 3.1：`SmartGuard.Engine.exe --install` / `--uninstall` |
| 2026-06-16 | Phase 3.2：`SmartGuard.Tray.exe`；Contracts 抽取；`Publish-Tray.ps1` / `Publish-All.ps1` |
| 2026-06-16 | Phase 3.3：C# 托盘 WinRT Toast（`notificationEvent`）+ Balloon 回退 |
| 2026-06-16 | Phase 4.1：`SmartGuard.Configuration` 共享配置库 |
| 2026-06-16 | Phase 4.2：`SmartGuard.LogViewer.exe` 实时日志查看器 |
| 2026-06-16 | Phase 4.3–4.4：Settings C# 化 + `Publish-All` 四 exe |
| 2026-06-16 | Phase 2.1：结构化日志 INFO/WARN/ERROR/HEART |
| 2026-06-16 | Phase 5.1–5.2：`installer\` staging + `SmartGuard.iss`；产出 `dist\SmartGuard-Setup-1.0.0-x64.exe` |
| 2026-06-17 | Phase 6.1–6.5：C# 计划任务注册、exe 启动链、删除 PS 应用栈、安装包瘦身、文档同步 |

---

## 附录：策略逻辑（与 `PolicyEngine` 一致）

```
if Paused → 不切换
if idle >= PowerSaverThresholdSec → 节能计划
else if idle >= BalancedThresholdSec → 平衡计划
else if 插电 OR 电量 >= LowBatteryPercent → 高性能计划
else → 平衡计划
```

空闲时间来源：`GetLastInputInfo`（`user32.dll`）。  
轮询间隔：`CheckIntervalSec`（默认 15 秒）。  
心跳间隔：`HeartbeatIntervalMin`（默认配置 30 分钟）。
