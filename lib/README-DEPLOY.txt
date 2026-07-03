SmartGuard 部署说明
====================

## 首次部署（开发机）

1. 安装 .NET 8 桌面运行时
2. 以管理员运行分步命令（或 `bin\SmartGuard.Cli.exe install`）：
   - `build.cmd` 或 `bin\SmartGuard.Cli.exe publish`  编译四件桌面组件到 bin\
   - `Register-AllTasks.cmd` 或 `bin\SmartGuard.Cli.exe install --skip-publish`  注册计划任务
3. `Start-Tray.cmd` 或 `bin\SmartGuard.Cli.exe start tray` 启动托盘

## 桌面组件

- bin\SmartGuard.Engine.exe
- bin\SmartGuard.Tray.exe
- bin\SmartGuard.Settings.exe
- bin\SmartGuard.Cli.exe

## 配置文件

- SmartGuard.config.json  — 策略与阈值
- SmartGuard.status.json  — 运行时状态（Tray 读取）
- SmartGuard.log           — 运行日志

## 计划任务名称

- SmartGuard Guardian  — 核心守护
- SmartGuard Tray      — 托盘

## 状态

`bin\SmartGuard.Cli.exe status` 查看计划任务与启动日志末行（替代已删除的 Status.cmd）。

## 安装包

使用 installer\Build-Installer.ps1 生成 dist\SmartGuard-Setup-{version}-x64.exe
