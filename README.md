# SmartGuard

Windows 智能电源守护：根据空闲时间、电池状态自动切换电源计划，并在切换时锁定屏幕亮度。

## 目录

| 路径 | 说明 |
|------|------|
| `src/SmartGuard.Engine/` | C# 核心守护引擎（net8.0，推荐） |
| `lib/` | PowerShell 壳层：托盘、设置、日志查看器、部署脚本 |
| `bin/SmartGuard.Engine.exe` | 发布后引擎可执行文件 |
| `SmartGuard.config.json` | 运行配置 |
| `SmartGuard.status.json` | Core → Tray 状态 IPC |
| `SmartGuard.log` | 运行日志（保留原文件名） |

## 环境要求

- Windows 10/11
- [.NET 8 运行时](https://dotnet.microsoft.com/download/dotnet/8.0)（框架依赖发布，非 self-contained）
- 管理员权限（切换电源计划）

## 快速开始

```powershell
cd D:\Project\SmartGuard

# 1. 编译 C# 引擎
powershell -File scripts\Publish-Engine.ps1

# 2. 注册计划任务（管理员）
powershell -ExecutionPolicy Bypass -File Register-SmartGuardTask.ps1

# 3. 启动托盘
powershell -File Restart-Tray.ps1
```

手动启动核心（调试）：

```powershell
.\Start-Core.cmd
# 或
.\bin\SmartGuard.Engine.exe --root D:\Project\SmartGuard
```

## 架构

```
展示层 (PowerShell)          引擎层 (C#)
─────────────────           ─────────────
SmartGuard.Tray.ps1    ←→   SmartGuard.Engine.exe
SmartGuard.Settings    status.json / config.json
LogViewer (WinForms)
Register-*.ps1
```

- **引擎**：空闲检测、三档计划策略、powercfg、WMI 亮度、日志、心跳
- **壳层**：托盘图标、WPF 设置、Toast 通知、日志实时查看、开机自启开关

## 配置

编辑 `SmartGuard.config.json`：

| 字段 | 默认 | 说明 |
|------|------|------|
| `BalancedThresholdSec` | 300 | 空闲 5 分钟 → 平衡 |
| `PowerSaverThresholdSec` | 900 | 空闲 15 分钟 → 节能 |
| `LowBatteryPercent` | 30 | 低电量时活跃态改用平衡 |
| `CheckIntervalSec` | 15 | 轮询间隔 |
| `Paused` | false | 暂停自动切换 |
| `LogFile` | `...\SmartGuard.log` | 日志路径 |

## 测试

```powershell
.\Run-Tests.ps1
```

包含 Pester（PowerShell 壳层）与 xUnit（C# 引擎）。

## 从 SmartPowerPlan (C:\Tools) 迁移

1. 项目已迁至 `D:\Project\SmartGuard` 并重命名
2. 配置/状态文件改为 `SmartGuard.config.json` / `SmartGuard.status.json`
3. 日志文件保持 `SmartGuard.log`
4. 计划任务名：`SmartGuard Guardian`、`SmartGuard Tray`
5. 重新运行 `Register-SmartGuardTask.ps1` 与 `Register-TrayTask.ps1`

## 回滚到 PowerShell 引擎

若 `bin\SmartGuard.Engine.exe` 不存在，计划任务与 `Start-Core.ps1` 会自动回退到 `lib\SmartGuard.Core.ps1`。

## 许可证

个人/内部使用。
