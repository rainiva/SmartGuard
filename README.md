# SmartGuard

Windows 智能电源守护：根据空闲时间、电池状态自动切换电源计划，并在切换时锁定屏幕亮度。

## 目录

| 路径 | 说明 |
|------|------|
| `src/SmartGuard.Engine/` | C# 核心守护引擎 |
| `src/SmartGuard.Tray/` | C# 托盘 |
| `src/SmartGuard.LogViewer/` | C# 日志查看器 |
| `src/SmartGuard.Settings/` | C# 设置窗体（WPF） |
| `src/SmartGuard.Configuration/` | 配置、计划任务与自动启动服务 |
| `src/SmartGuard.Contracts/` | 共享契约与状态模型 |
| `src/SmartGuard.Packaging/` | 发布、打包与安装包生成 |
| `lib/` | 图标、Settings XAML |
| `bin/*.exe` | `build.cmd` 发布后四件套：Engine / Tray / LogViewer / Settings |
| `installer/` | Inno Setup 6 脚本与 staging 文件 |
| `dist/` | 安装包产出：`SmartGuard-Setup-{version}-x64.exe` |
| `SmartGuard.config.json` | 运行配置 |
| `SmartGuard.status.json` | Core → Tray 状态 IPC |
| `SmartGuard.log` | 运行日志 |

## 环境要求

- Windows 10/11
- [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)（框架依赖发布）
- 管理员权限（切换电源计划与注册任务）

## 快速开始

### 普通用户

直接下载最新 Release 安装包：

[https://github.com/rainiva/SmartGuard/releases/latest](https://github.com/rainiva/SmartGuard/releases/latest)

安装完成后会自动注册计划任务并启动引擎与托盘。

### 开发者

```batch
cd <仓库根目录>

:: 1. 编译并发布四件套到 bin/
build.cmd Release

:: 2. 注册计划任务（需要管理员权限）
Register-AllTasks.cmd
:: 或: bin\SmartGuard.Engine.exe --root <仓库根目录> --install

:: 3. 启动托盘
Start-Tray.cmd
```

手动启动核心（调试）：

```batch
Start-Core.cmd
:: 或: bin\SmartGuard.Engine.exe --root <仓库根目录>
```

卸载计划任务（不删除配置与日志）：

```batch
bin\SmartGuard.Engine.exe --root <仓库根目录> --uninstall
```

## 架构

```
展示层 (C#)                    引擎层 (C#)
─────────────────             ─────────────
SmartGuard.Tray.exe      ←→   SmartGuard.Engine.exe
SmartGuard.Settings.exe       status.json / config.json
SmartGuard.LogViewer.exe
Engine.exe --install（计划任务）
```

- **引擎**：空闲检测、三档计划策略、powercfg、WMI 亮度、日志、心跳
- **壳层**：托盘、WPF 设置、Toast、日志查看器（均为 C# exe）

## 配置

编辑 `SmartGuard.config.json`：

| 字段 | 默认 | 说明 |
|------|------|------|
| `BalancedThresholdSec` | 300 | 空闲 5 分钟 → 平衡 |
| `PowerSaverThresholdSec` | 900 | 空闲 15 分钟 → 节能 |
| `LowBatteryPercent` | 30 | 低电量时活跃态改用平衡 |
| `CheckIntervalSec` | 15 | 轮询间隔 |
| `BrightnessRestoreMs` | 300 | 切计划后等待亮度恢复的时间 |
| `AutoStartEnabled` | true | 是否随用户登录启动托盘 |
| `NotifyOnPlanChange` | true | 切换电源计划时是否弹出 Toast 通知 |
| `HeartbeatIntervalMin` | 30 | 心跳日志写入间隔（分钟） |
| `LogMaxBytes` | 1048576 | 单个日志文件大小上限（字节） |
| `Paused` | false | 暂停自动切换 |
| `LogFile` | `SmartGuard.log` | 日志路径 |

## 构建安装包

需要 [Inno Setup 6](https://jrsoftware.org/isinfo.php)。若未安装在默认路径，可设置环境变量 `SMARTGUARD_ISCC`（例如 `D:\Apps\Inno Setup 6\ISCC.exe`），或传参 `--iscc`。

```batch
:: 生成 staging：发布四件套 + 下载 .NET 8 Desktop Runtime redist
dotnet run --project src\SmartGuard.Packaging -- stage --root . --configuration Release

:: 编译安装包 → dist\SmartGuard-Setup-<version>-x64.exe
dotnet run --project src\SmartGuard.Packaging -- installer --root . --configuration Release
```

安装包默认安装到 `{autopf}\SmartGuard`；卸载默认保留配置与日志（可在卸载向导选择删除）。

## 测试

```batch
Run-Tests.cmd
```

- **Pester**：39 项仓库契约测试 + 7 项集成/安装流程测试
- **xUnit**：232 项 C# 组件测试（含 2 项性能测试）

日志行格式（C# 引擎）：`yyyy-MM-dd HH:mm:ss [LEVEL] message`（LEVEL 为 INFO / WARN / ERROR / HEART）。
归档文件：`SmartGuard.log.yyyyMMdd.bak`（保留 7 天）。
插拔电源后引擎会立即重新评估策略（无需等轮询间隔）。

## 文档

- [Phase 7 开发机去 PS](docs/PHASE-7-TASK-CONTRACT.md)

## 故障恢复

若组件缺失或行为异常：

1. 重新运行最新 Release 安装包，或
2. 执行 `build.cmd` 后运行 `bin\SmartGuard.Engine.exe --root <目录> --install` 重新注册任务。

## 许可证

个人/内部使用。
