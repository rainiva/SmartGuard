# Phase 5.3 安装验收清单（V1–V9）

**契约：** [`docs/INNO-INSTALLER-TASK-CONTRACT.md`](../../INNO-INSTALLER-TASK-CONTRACT.md) §八  
**当前安装包：** `dist\SmartGuard-Setup-1.0.6-x64.exe`  
**最后更新：** 2026-06-17

## 自动化已覆盖（本机 / CI）

| ID | 场景 | 自动化来源 | 状态 |
|----|------|------------|------|
| **V2** | Guardian 计划任务含 `--root` 与安装路径 | `InstallerUserFlow.Tests.ps1` | ✅ |
| **V3** | Tray 任务含 `SmartGuard.Tray.exe --root` | 同上 | ✅ |
| **V6** | 升级安装保留用户数据（侧车 `.sg-upgrade-marker`） | 同上 `upgrade install preserves...` | ✅ |
| **载荷** | 无 `Register-*.ps1`、无 `lib\SmartGuard.Core.ps1` | 同上 | ✅ |
| **静默装/卸** | 退出码 0 | 同上 + Tray 集成 2 项 | ✅ |
| **回归** | `Run-Tests.ps1` | Pester 26 + 集成 7 + xUnit 157 | ✅ `PASSED=33` |

证据日志：[`integration-1.0.6.log`](./integration-1.0.6.log)

## 须干净 VM 或人工交互

| ID | 场景 | 前置 | 步骤 | 期望 | 状态 |
|----|------|------|------|------|------|
| **V1** | 无 .NET 8 首次安装 | Win10/11 x64 快照，**未**装 Desktop 8.0 | 运行 Setup（P5A 捆绑 redist） | 成功；`dotnet --list-runtimes` 含 `Microsoft.WindowsDesktop.App 8.0.x` | ⏳ |
| **V4** | 托盘与设置/日志 | 已安装 | 托盘图标可见；打开设置、日志 | C# exe 启动；config 可保存 | ⏳ |
| **V5** | 引擎写日志 | 已安装，等待或切换电源计划 | `{app}\SmartGuard.log` 有新 `[INFO]`/`[HEART]` 行 | ⏳ |
| **V7a** | 卸载保留数据 | GUI 或静默，**不**勾选删除 | `schtasks` 两任务消失；config/log 仍在 `{app}` | ⏳ 本机 1.0.0 已测；1.0.6 待复测 |
| **V7b** | 卸载删除数据 | GUI 卸载，**勾选**「删除配置与日志」 | config/log 删除；目录可清空 | ⏳ |
| **V8** | 拒绝 UAC | 标准用户或取消提权 | 安装中止或明确错误；无半残计划任务 | ⏳ |
| **V9** | 自定义路径 | 向导选 `D:\Apps\SmartGuard` 等 | 任务 `--root` 与 WorkingDirectory 指向所选路径 | ⏳ 集成用 `%TEMP%` 路径近似 V2/V3 |

## VM 执行记录模板

复制下表到每次 VM 会话，填结果后另存为 `smoke-report-YYYY-MM-DD.md` 或追加到本文件。

```
日期：
VM：Win ___ x64，.NET 前状态：无 / 有
Setup：dist\SmartGuard-Setup-___-x64.exe
安装路径：

| ID | 结果 | 备注 / 日志路径 |
|----|------|-----------------|
| V1 |      |                 |
| V2 |      |                 |
| V3 |      |                 |
| V4 |      |                 |
| V5 |      |                 |
| V6 |      |                 |
| V7a |     |                 |
| V7b |     |                 |
| V8 |      |                 |
| V9 |      |                 |
```

## 建议 VM 命令（PowerShell 管理员）

```powershell
# 静默安装到自定义路径（近似 V9 + V2/V3 查询）
$root = 'D:\Apps\SmartGuard'
Start-Process '.\SmartGuard-Setup-1.0.6-x64.exe' `
  -ArgumentList "/DIR=$root",'/VERYSILENT','/SUPPRESSMSGBOXES','/LOG=D:\sg-install.log' `
  -Wait -Verb RunAs

schtasks /Query /TN 'SmartGuard Guardian' /XML | Out-File D:\sg-guardian.xml
schtasks /Query /TN 'SmartGuard Tray' /XML | Out-File D:\sg-tray.xml
dotnet --list-runtimes
```

## 完成标准

- V1–V9 全部 ✅ 或明确记录 ⏭（不可自动化项须注明原因）
- 截图 / 日志归档于 `docs\evidence\installer\`
- `docs\MIGRATION.md` 中 Phase **5.3** 标为 **已完成**
