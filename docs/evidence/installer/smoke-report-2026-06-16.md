# SmartGuard 安装包本机冒烟验收

**日期：** 2026-06-16  
**安装包：** `dist\SmartGuard-Setup-1.0.0-x64.exe`  
**测试路径：** `D:\SmartGuard-Smoke`（隔离于开发目录 `D:\Project\SmartGuard`）  
**安装日志：** [smoke-install.log](./smoke-install.log)

## 执行命令

```powershell
# 静默安装（管理员提权）
Start-Process dist\SmartGuard-Setup-1.0.0-x64.exe `
  -ArgumentList '/SILENT','/SUPPRESSMSGBOXES','/DIR=D:\SmartGuard-Smoke','/TASKS=""' `
  -Wait -Verb RunAs
```

## 验收结果

| 项 | 说明 | 结果 |
|----|------|------|
| **安装** | Setup 退出码 0；四件套 + 注册脚本落盘 | ✅ |
| **V1（本机近似）** | 机器已有 .NET 8；安装器跳过 redist 或已成功执行；`dotnet --list-runtimes` 含 `WindowsDesktop.App 8.0.x` | ✅ |
| **V2** | `SmartGuard Guardian` → `D:\SmartGuard-Smoke\bin\SmartGuard.Engine.exe`，WorkingDirectory=`D:\SmartGuard-Smoke` | ✅ |
| **V3** | `SmartGuard Tray` → `SmartGuard.Tray.exe --root "D:\SmartGuard-Smoke"` | ✅ |
| **V4** | 手动启动托盘/引擎进程成功；`SmartGuard.config.json` 首次运行生成 | ✅ |
| **V5** | `SmartGuard.log` 出现 `[INFO]` / `[HEART]` 行 | ✅ |
| **V6** | 二次安装同路径；`SmokeMarker=keep-me-20260616` 未被覆盖 | ✅ |
| **V7a** | 静默卸载（未勾选删除数据）；计划任务消失；`config`/`log`/`status` 仍保留于 `D:\SmartGuard-Smoke` | ✅ |
| **V7b** | 未测（需 GUI 勾选「删除配置与日志」） | ⏭ |
| **V8** | 未测（需交互取消 UAC） | ⏭ |
| **V9** | 本机用 `D:\SmartGuard-Smoke` 自定义路径验证通过（与 V2/V3 一致） | ✅ |

## 卸载后残留（V7a 预期）

```
D:\SmartGuard-Smoke\
  SmartGuard.config.json
  SmartGuard.log
  SmartGuard.startup.log
  SmartGuard.status.json
  .SmartGuard.initialized
```

## 开发环境恢复

冒烟后已将计划任务重新注册回 `D:\Project\SmartGuard` 并启动开发托盘。

## 备注

- 本机已有 .NET 8，**不能替代**契约要求的「干净 VM、无 .NET」V1 全量验收。
- 安装使用 `PrivilegesRequired=lowest` + HKCU 卸载项；`--install` 注册任务时经 Engine 提权（本机 UAC 已批准）。
- `D:\SmartGuard-Smoke` 目录可手动删除以清理冒烟残留。
