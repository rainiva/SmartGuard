# SmartGuard 安装包冒烟验收（Phase 6 / 无 Register 脚本）

**日期：** 2026-06-17  
**安装包：** `dist\SmartGuard-Setup-1.0.5-x64.exe`  
**变更摘要：** Phase 6.3 起安装包不再携带 `Register-*.ps1`；计划任务由 `Engine.exe --install` 内 C# `ScheduledTaskRegistrar` 注册。

## 自动化验收

| 项 | 命令 / 说明 | 结果 |
|----|-------------|------|
| 全量测试 | `Run-Tests.ps1` | ✅ Pester 26 + 集成 4 全绿；dotnet xUnit 157 项全绿 |
| 静默安装 | `Tests/Integration/InstallerUserFlow.Tests.ps1` | ✅ 安装退出码 0 |
| 静默卸载 | 同上 | ✅ 卸载退出码 0 |
| Guardian 任务 | `schtasks /Query /TN "SmartGuard Guardian" /XML` | ✅ 含 `--root` 与安装路径 |
| 载荷 | `{app}` 下无 `Register-SmartGuardTask.ps1` | ✅（6.3 契约） |

## 与 2026-06-16 冒烟的差异

| 项 | 1.0.0 冒烟 | 1.0.5 + Phase 6 |
|----|------------|-----------------|
| Register 脚本 | 打入 `{app}` | **已移除** |
| 任务注册 | PS `Register-*.ps1` | **C# `ScheduledTaskRegistrar`** |
| PS 应用回退 | 存在 `lib/layers` | **已删除** |

## 仍待人工 / VM

- Phase 5 **V1**：无 .NET 的干净 VM 首次安装 redist
- **V7b**：GUI 卸载勾选「删除配置与日志」
- **V8**：取消 UAC 行为

## 参考

- [Phase 6 Task Contract](../PHASE-6-TASK-CONTRACT.md)
- [早前本机冒烟（1.0.0）](./smoke-report-2026-06-16.md)
