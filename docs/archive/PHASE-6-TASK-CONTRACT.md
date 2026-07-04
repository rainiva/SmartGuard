# Phase 6：去 PowerShell 化 — Task Contract

**制定日期：** 2026-06-17  
**状态：** 6.1–6.3、6.5 **已完成**；干净 VM 验收待办；构建链迁移已由 **Phase 7.5** 完成

---

## 一、目标（Goal）

消除**用户机器运行时**对 PowerShell 应用栈的依赖：计划任务注册、手动启动、托盘打开设置/日志、安装包载荷均只依赖已发布的 C# exe。

**保留 PowerShell 的范围（Phase 6 后；Phase 7 未改变用户运行时）：** `Run-Tests.ps1`、`Tests/*.ps1`、`installer/*.ps1`、`scripts/Publish-All.ps1`（委托 `build.cmd`）、`lib/Create-TrayIcon.ps1`。

---

## 二、切片与状态

| 切片 | 代号 | 交付 | 状态 |
|------|------|------|------|
| **6.1** | 6P-schtasks | `ScheduledTaskRegistrar`；`InstallCommands` / `AutoStartService` 纯 C# | **已完成** (`9c70dfb`) |
| **6.2** | 6P-launchers | `Start-Core.cmd`、`Start-Tray.cmd`、`Restart-Tray.cmd` 等只启 exe | **已完成** (`2a3250a`) |
| **6.3** | 6P-fallback | 删除 `lib/layers`、PS 托盘/设置/日志/引擎；C# 无 PS 回退 | **已完成** (`c7f8146`) |
| **6.4** | 6P-packaging | 安装包去掉 `Register-*.ps1`（与 6.3 同提交） | **已完成** (`c7f8146`) |
| **6.5** | 6P-docs | 本文档、`MIGRATION.md`、Inno 契约同步、冒烟证据 | **已完成** |
| **6+** | — | 用 CI/dotnet 替代分项目 `Publish-*.ps1` | **已由 Phase 7.5 完成**（`build.cmd`） |

---

## 三、6.1 — 计划任务 C# 注册

### 交付物

| 文件 | 说明 |
|------|------|
| `src/SmartGuard.Configuration/ScheduledTaskRegistrar.cs` | `schtasks /Create /XML`；对齐原 PS 的 Logon、RunLevel、RestartOnFailure |
| `src/SmartGuard.Engine/Cli/InstallCommands.cs` | `--install` 调 Registrar，不再 `powershell -File Register-*.ps1` |
| `src/SmartGuard.Configuration/AutoStartService.cs` | 任务缺失时 `RegisterIfMissing` |

### 验收

- [x] `Engine.exe --install` / `--uninstall` 集成测试通过
- [x] `schtasks /Query /XML` 中 Guardian/Tray 指向 `bin\*.exe` 且含 `--root`

---

## 四、6.2 — 启动链 exe 化

### 交付物

| 文件 | 变更 |
|------|------|
| `Start-Core.cmd` | VBS + `cscript` 提权；只启动 `SmartGuard.Engine.exe` |
| `Start-Tray.cmd` / `Restart-Tray.cmd` | 纯 batch；缺 exe 报错 |
| `Register-AllTasks.ps1` | `Engine.exe --install --skip-publish` |
| `ToastShortcutResolver.cs` | 无 `powershell.exe` 回退 |

### 验收

- [x] 上述 cmd 内容不含 `powershell.exe`（`Start-Core.ps1` 仍可用作备用提权入口）

---

## 五、6.3 / 6.4 — 删除 PS 应用栈与安装包瘦身

### 已删除（摘要）

- `lib/SmartGuard.{Core,Tray,Settings}.ps1`、`Show-LogViewer.ps1`、`SmartGuard.Functions.ps1`
- `lib/layers/**`
- `Register-SmartGuardTask.ps1`、`Register-TrayTask.ps1`

### C# 行为

- `ScheduledTaskRegistrar`：缺 exe → `FileNotFoundException`
- `ExternalToolLauncher`：只启动 `SmartGuard.Settings.exe` / `SmartGuard.LogViewer.exe`

### 安装目录布局（当前）

```
{app}\
├── bin\          # 四件套 + DLL
├── lib\          # SmartGuard.ico、SmartGuard.Settings.xaml
├── SmartGuard.config.json
└── （运行时生成 status / log）
```

### 验收

- [x] Pester `Phase 6.3` 断言遗留 PS 路径不存在
- [x] `.iss` / `Build-Staging.ps1` 不含 `Register-*.ps1`
- [x] 静默安装/卸载集成测试通过（`InstallerUserFlow.Tests.ps1`）

---

## 六、废止决策

| 原决策 | 处置 |
|--------|------|
| Phase 1 **A10**「回滚至 PS Core」 | **废止** — 缺 exe 时提示重装/重新 `Publish-All` |
| Phase 3/4「保留 PS 回退」 | **废止** — 桌面四件套为唯一用户面 |

### 新回滚方案

1. 重新运行 `dist\SmartGuard-Setup-*-x64.exe` 覆盖安装，或  
2. `build.cmd`（或 `scripts\Publish-All.ps1`）后执行 `bin\SmartGuard.Engine.exe --root <dir> --install`

配置 / 状态 / 日志 JSON **无需格式迁移**。

---

## 七、测试策略（Phase 6 后）

| 类型 | 位置 | 约数量 |
|------|------|--------|
| Pester 契约 / 安装包 | `Tests/SmartGuard.Tests.ps1` | 26 |
| Pester 集成 | `Tests/Integration/*.ps1` | 4 |
| xUnit | `Tests/SmartGuard.*.Tests/` | 157 |
| 运行 | `Run-Tests.ps1` | 全串联 |

原 Pester 域逻辑测试（`Get-ExpectedPlanGuid` 等）已移除；等价行为由 `tests/SmartGuard.Engine.Tests` 覆盖。

---

## 八、非目标（Non-Goals）

- 不重写 `Run-Tests.ps1` 为纯 dotnet
- 不删除 `scripts/Publish-All.ps1`（Phase 7.5 已改为委托 `build.cmd`）
- 不将 Inno 构建迁到 MSBuild Task
- Phase 5 **5.3** 干净 VM V1–V9 仍待执行

---

## 九、变更记录

| 日期 | 变更 |
|------|------|
| 2026-06-17 | 6.1：`ScheduledTaskRegistrar` + `InstallCommands` C# 化 |
| 2026-06-17 | 6.2：cmd 启动链 exe-only |
| 2026-06-17 | 6.3–6.4：删除 PS 栈；安装包去掉 Register 脚本 |
| 2026-06-17 | 6.5：本文档与 `MIGRATION.md` / Inno 契约同步 |
