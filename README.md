# SmartGuard

Windows 智能电源守护：根据空闲时间、电池状态自动切换电源计划，并在切换时锁定屏幕亮度。

## 目录

| 路径 | 说明 |
|------|------|
| `src/SmartGuard.Engine/` | C# 核心守护引擎 |
| `src/SmartGuard.Tray/` | C# 托盘 |
| `src/SmartGuard.LogViewer/` | C# 日志查看器 |
| `src/SmartGuard.Settings/` | C# 设置窗体（WPF） |
| `lib/` | PowerShell 回退与部署脚本 |
| `bin/*.exe` | 发布后四件套：Engine / Tray / LogViewer / Settings |
| `installer/` | Inno 安装包：staging 构建、`SmartGuard.iss` |
| `dist/` | 安装包产出：`SmartGuard-Setup-{version}-x64.exe` |
| `SmartGuard.config.json` | 运行配置 |
| `SmartGuard.status.json` | Core → Tray 状态 IPC |
| `SmartGuard.log` | 运行日志 |

## 文档

- [迁移规划（完整）](docs/MIGRATION.md) — DevGuard Task Contract、分期方案与实施状态
- [Inno 安装包契约](docs/INNO-INSTALLER-TASK-CONTRACT.md) — Phase 5：P5A 运行时、H1–H6 已签署、验收表

## 环境要求

- Windows 10/11
- [.NET 8 运行时](https://dotnet.microsoft.com/download/dotnet/8.0)（框架依赖发布，非 self-contained）
- 管理员权限（切换电源计划）

## 快速开始

```powershell
cd D:\Project\SmartGuard

# 1. 编译全部 C# 组件
powershell -File scripts\Publish-All.ps1

# 2. 注册计划任务（管理员，会弹出 UAC）
.\bin\SmartGuard.Engine.exe --root D:\Project\SmartGuard --install

# 卸载计划任务（不删除配置与日志）
.\bin\SmartGuard.Engine.exe --root D:\Project\SmartGuard --uninstall

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
展示层 (C#)                    引擎层 (C#)
─────────────────             ─────────────
SmartGuard.Tray.exe      ←→   SmartGuard.Engine.exe
SmartGuard.Settings.exe       status.json / config.json
SmartGuard.LogViewer.exe
Register-*.ps1（安装）
lib/*.ps1（回退）
```

- **引擎**：空闲检测、三档计划策略、powercfg、WMI 亮度、日志、心跳
- **壳层**：托盘、WPF 设置、Toast、日志查看；`lib/` 保留 PS 回退

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

## 安装包构建（Inno Setup 6）

需安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)。若未安装在默认路径，可设置环境变量 `SMARTGUARD_ISCC`（例如 `D:\Apps\Inno Setup 6\ISCC.exe`），或传参 `-IsccPath`。

```powershell
# 1. 生成 staging（发布四件套 + 下载 .NET 8 Desktop Runtime redist）
powershell -File installer\Build-Staging.ps1

# 2. 编译安装包 → dist\SmartGuard-Setup-1.0.0-x64.exe
powershell -File installer\Build-Installer.ps1

# 仅重编 .iss（staging 已就绪时）
powershell -File installer\Build-Installer.ps1 -SkipStaging
```

安装包默认目录 `{autopf}\SmartGuard`；卸载默认保留配置与日志（可在卸载向导勾选删除）。详见契约文档。

## 测试

```powershell
.\Run-Tests.ps1
```

包含 Pester（PowerShell 壳层，含 Phase 5 installer 布局测试）与 xUnit（C# 引擎）。

日志行格式（C# 引擎）：`yyyy-MM-dd HH:mm:ss [LEVEL] message`（LEVEL 为 INFO / WARN / ERROR / HEART）。  
归档文件：`SmartGuard.log.yyyyMMdd.bak`（保留 7 天）。插拔电源后引擎会立即重新评估策略（无需等轮询间隔）。

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
