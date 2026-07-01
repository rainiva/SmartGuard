# SmartGuard 架构契约 — 单真源与单入口

**状态：** 生效  
**关联：** [AGENTS.md](../AGENTS.md) §8、§11；Pester `Phase 8`；`SmartGuard.Architecture.Tests`

本文档是 SmartGuard **配置、路径、状态、生命周期** 的唯一架构权威。新增字段、入口或共享类型必须先更新本文档并补充门禁测试（TDD RED）。

---

## 1. 真源注册表

| 概念 | 权威来源 | 允许读者 | 允许写者 | 禁止模式 |
|------|----------|----------|----------|----------|
| 安装根 `{root}` | `InstallRootResolver` | 全部 exe | — | 各 exe 内私有 `RootResolver` |
| 配置文件 | `{root}/SmartGuard.config.json` | 全部经 `GuardConfigRepository.TryLoad` | `GuardConfigRepository.Save`、`ConfigMutationService` | Engine 直调 `GuardConfig.LoadFromFile` |
| 运行时状态 | `{root}/SmartGuard.status.json` | `StatusJsonReader` / `StatusStore` | `StatusPublisher`（Engine） | Settings/Tray 各自解析 JSON |
| 主日志路径 | `config.LogFile` 或 `SmartGuardPaths.DefaultLogFile(root)` | Engine、Settings 日志页、LogViewer | Engine、`AppendInfoLog` | Settings 硬编码 `SmartGuard.log` |
| 计划任务名 | `ScheduledTaskRegistrar.GuardianTaskName` / `TrayTaskName` | 注册、恢复、安装 | `ScheduledTaskRegistrar` | 字面量 `"SmartGuard Guardian"` 散落 |
| 开机自启 UI | `config.AutoStartEnabled` + `AutoStartService.SyncFromTasks` | Settings | `SettingsSaveCoordinator` + `AutoStartService` | 仅写 config 不读 schtasks |
| 暂停状态展示 | `status.json` `paused`（Tray 菜单与状态行） | Tray | `ConfigMutationService.SetPaused` | Tray 菜单仅启动时读 config |
| 版本号（UI） | `installer/version.txt` → 各 csproj `<Version>` | Settings About | Packaging bump | 仅 Settings 同步版本 |
| Engine 停止 | `EngineLifecycle.Stop` | CLI uninstall、Inno、测试 helpers | `EngineLifecycle` | 独立 taskkill/schtasks 脚本副本 |

---

## 2. 入口注册表

| 用户操作 | 唯一推荐入口 | 说明 |
|----------|--------------|------|
| 注册计划任务 | `SmartGuard.Engine.exe --install` | Inno / `Register-AllTasks.cmd` 委托此入口 |
| 启动 Engine（生产） | 计划任务 `SmartGuard Guardian` | Tray `GuardianRecovery` 仅 `schtasks /Run` |
| 停止 Engine（卸载） | `EngineLifecycle.Stop` | CLI、Inno、集成测试共用 |
| 暂停/恢复守护 | `ConfigMutationService.SetPaused` | Tray 菜单、Settings 保存均经此 API |
| 打开设置 | `ExternalToolLauncher.OpenSettings` | 命名管道激活或 spawn |
| 打开日志 | `ExternalToolLauncher.OpenLogViewer` → Settings `--page logs` | LogViewer 为遗留快捷方式，非主入口 |
| 保存设置 | `SettingsSaveCoordinator.Save` | 含主题；禁止 `SaveThemePreferences` 旁路 |

---

## 3. 共享基础设施清单

| 类型 | 命名空间 | 职责 |
|------|----------|------|
| `InstallRootResolver` | `SmartGuard.Configuration` | `--root` / `SMARTGUARD_ROOT` / `bin` 提升 / config walk-up |
| `SmartGuardPaths` | `SmartGuard.Configuration` | config、status、log、exe 路径常量 |
| `GuardConfigRepository` | `SmartGuard.Configuration` | 读（含迁移）、全量 Save |
| `ConfigMutationService` | `SmartGuard.Configuration` | 原子局部写：Paused、ManualHighPerformanceUntil |
| `StatusJsonReader` | `SmartGuard.Configuration` | 读取并反序列化 status.json |
| `ScheduledTaskRegistrar` | `SmartGuard.Configuration` | 任务名与 XML 唯一源 |
| `EngineLifecycle` | `SmartGuard.Configuration` | 停止进程与任务顺序 |

---

## 4. 禁止清单（机器可测 — Phase 8 / Architecture.Tests）

1. `SmartGuard.Engine` 中不得调用 `GuardConfig.LoadFromFile`（经 Repository）。
2. 不得在 Tray/Settings/LogViewer/Engine 中定义 `class RootResolver` 或 `internal static class RootResolver`（使用 `InstallRootResolver`）。
3. 不得在 `GuardianRecovery` 等处重复字面量 Guardian 任务名（使用 `ScheduledTaskRegistrar.GuardianTaskName`）。
4. 不得在 Settings 硬编码 `Path.Combine(root, "SmartGuard.log")`（使用 `SmartGuardPaths` + `config.LogFile`）。
5. 不得新增绕过 `SettingsSaveCoordinator` 的主题/config 全量写（含 `SaveThemePreferences` 直写 `_originalConfig`）。
6. 不得在 Inno/cmd 新增独立 schtasks 注册逻辑（委托 Engine `--install`）。

---

## 5. 变更流程

1. **RED** — 更新本文档 + 添加/修改 Architecture.Tests 或 Pester Phase 8，确认失败。
2. **GREEN** — 最小实现使测试通过。
3. **REFACTOR** — 全绿后清理；`Run-Tests.ps1` 通过。
4. **git commit** — 每 TDD Slice 一 commit。
5. **终验** — 全部 Slice 后按 §2 问题表与 AGENTS 逐项验收。

---

## 6. 门禁索引

| 门禁 | 位置 |
|------|------|
| 文档契约 | `ArchitectureContractDocTests` |
| 单 RootResolver | `SingleRootResolverArchitectureTests` |
| 无 Engine LoadFromFile | `ConfigLoadStackArchitectureTests` |
| 任务名单源 | `TaskNameSingleSourceArchitectureTests` |
| SmartGuardPaths | `SmartGuardPathsArchitectureTests` |
| Pester Phase 8 | `Tests/SmartGuard.Tests.ps1` |
