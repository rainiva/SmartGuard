# 多真源治理终验报告

**日期：** 2026-07-01（补洞 Slice 后）  
**Run-Tests.ps1：** PASS（`SMARTGUARD_SKIP_INSTALLER_TESTS=1`：Pester **51** + Tray 集成 **2** + dotnet 全绿；`PASSED=53 FAILED=0`）

## §2 问题关闭状态

| 级别 | 已关闭 | 说明 |
|------|--------|------|
| H-01～H-08 | **8/8** | Engine 经 Repository；单一 InstallRootResolver；LogFile/主题/暂停已收敛；**H-07** Inno `StopSmartGuardProcesses` 委托 `Engine --uninstall` → `EngineLifecycle` |
| M-01 | 1/1 | `AutoStartService.SyncFromTasks` |
| M-02 | 1/1 | Tray 暂停菜单随 status 刷新 |
| M-03 | **1/1** | `PolicyEngine`、`SettingsWindowController`、`PowerPlanMappingValidator` 均引用 `PowerPlanCatalogProvider` 显示名常量 |
| M-05 | 1/1 | `StatusJsonReader` 共享 |
| M-09 | 1/1 | `GuardianRecovery` 仅 schtasks |
| M-10 | 1/1 | 安装器日志快捷方式 → Settings `--page logs` |
| M-12 | 1/1 | Inno/Status.cmd 任务名与 Registrar 一致（契约测试） |
| M-13 | 1/1 | `Directory.Build.props` 同步 version.txt |
| M-04, M-06～08, M-11, M-14～16 | **登记延期** | 见 ARCHITECTURE-CONTRACT §7；非用户可见回归，下轮治理 |
| L-01～L-04 | **登记延期** | 低优先级常量重复 / 文档漂移；L-04 部分由本报告与 Contract 索引覆盖 |

## §9 验证清单

- [x] Phase 8 Pester（**51** 项）+ Architecture.Tests（**20** 项）全绿
- [x] 四端 `InstallRootResolver` parity 测试
- [x] Engine 无 `LoadFromFile`
- [x] Settings 日志页 `SmartGuardPaths.ResolveLogFile`
- [x] 主题保存经 `SettingsSaveCoordinator`
- [x] `EngineLifecycle.StopForUninstall`：CLI + Inno 委托同一实现
- [x] CI workflow `.github/workflows/test.yml`
- [x] Installer 集成 helper 改调 `SmartGuard.Packaging installer`（非已删除的 `Build-Installer.ps1`）
- [x] 三项登记遗留（L-task / L-log / M-03 PolicyEngine）已闭环
- [ ] 计划 §9「从 bin 无 --root 四端 parity」— 有 parity 单测，未单独 E2E 脚本
- [ ] M-04～16 / L-01～04 全矩阵关闭 — **显式延期**（见上表）

## 补洞 Slice 摘要（2026-07-01）

| Slice | 改动 |
|-------|------|
| H-07 | `EngineLifecycle` 增加 `EndAndDisableScheduledTasks` + 等待退出；Inno 停止委托 `Engine.exe --uninstall` |
| M-03 | Settings / `PowerPlanMappingValidator` 使用 `PowerPlanCatalogProvider.*DisplayName` |
| Installer 集成 | `InstallerUserFlow.Helpers.ps1` → `dotnet run SmartGuard.Packaging installer` |
| 门禁 | `InnoStopDelegationArchitectureTests`、`SettingsDisplayNameArchitectureTests`、Pester 更新 |

## 结论

**核心验收通过** — H 项、关键 M 项、登记遗留与 H-07/M-03/Installer 补洞已闭环；M-04～16 与 L 项在 Contract §7 登记延期，不构成当前阻断项。
