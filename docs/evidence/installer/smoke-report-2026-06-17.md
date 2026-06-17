# SmartGuard 安装包冒烟验收（Phase 6 / 1.0.6）

**日期：** 2026-06-17  
**安装包：** `dist\SmartGuard-Setup-1.0.6-x64.exe`  
**变更摘要：** Phase 6 完成；扩展集成测试覆盖 V2/V3/V6 与无 Register 脚本载荷。

## 自动化验收

| 项 | 命令 / 说明 | 结果 |
|----|-------------|------|
| 全量测试 | `Run-Tests.ps1` | ✅ **PASSED=33 FAILED=0**（Pester 26 + 集成 7；dotnet xUnit 157 项） |
| 静默安装 / 卸载 | `InstallerUserFlow.Tests.ps1` | ✅ 退出码 0 |
| Guardian 任务 (V2) | `schtasks /Query /TN "SmartGuard Guardian" /XML` | ✅ 含 `--root` 与安装路径 |
| Tray 任务 (V3) | `schtasks /Query /TN "SmartGuard Tray" /XML` | ✅ 含 `SmartGuard.Tray.exe` 与 `--root` |
| 升级保留 (V6) | 二次静默安装 `-PreserveExisting` | ✅ `.sg-upgrade-marker` 与 `SmartGuard.config.json` 保留 |
| 载荷 | 无 `Register-*.ps1`、无 `lib\SmartGuard.Core.ps1` | ✅ |
| 证据 | [`integration-1.0.6.log`](./integration-1.0.6.log) | ✅ |

## 与 1.0.5 的差异

| 项 | 1.0.5 | 1.0.6 |
|----|-------|-------|
| 集成用例 | 4 项 | **5 项**（+Tray 任务、+升级保留） |
| `Ensure-InstallerBuilt` | 仅比较 `.iss` 时间戳 | 亦比较 `bin\SmartGuard.Engine.exe` |
| 升级测试 | 无 | `Invoke-SmartGuardSilentInstall -PreserveExisting` |

## 仍待人工 / VM

见 [`phase-5.3-vm-checklist.md`](./phase-5.3-vm-checklist.md)：

- **V1**：无 .NET 的干净 VM
- **V4/V5**：托盘 UI、设置/日志、心跳日志
- **V7b**：GUI 勾选删除用户数据
- **V8**：取消 UAC
- **V9**：向导自定义路径（集成测试仅用 `%TEMP%` 路径验证任务 XML）

## 参考

- [Phase 6 Task Contract](../PHASE-6-TASK-CONTRACT.md)
- [早前本机冒烟（1.0.0）](./smoke-report-2026-06-16.md)
- [Phase 5.3 VM 清单](./phase-5.3-vm-checklist.md)
