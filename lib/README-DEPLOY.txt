SmartGuard 部署说明
====================

## 首次部署（开发机）

1. 安装 .NET 8 桌面运行时
2. 以管理员运行 Setup-All.cmd，或分步执行：
   - build.cmd（或 scripts\Publish-All.ps1）  编译四件套到 bin\
   - Register-AllTasks.cmd                注册计划任务（Engine --install）
3. Start-Tray.cmd 或 Restart-Tray.cmd 启动托盘

## 桌面组件

- bin\SmartGuard.Engine.exe
- bin\SmartGuard.Tray.exe
- bin\SmartGuard.Settings.exe
- bin\SmartGuard.LogViewer.exe

## 配置文件

- SmartGuard.config.json  — 策略与阈值
- SmartGuard.status.json  — 运行时状态（Tray 读取）
- SmartGuard.log           — 运行日志

## 计划任务名称

- SmartGuard Guardian  — 核心守护
- SmartGuard Tray      — 托盘

## 安装包

使用 installer\Build-Installer.ps1 生成 dist\SmartGuard-Setup-{version}-x64.exe
