# Phase 7：开发机去 PowerShell — Task Contract

**制定日期：** 2026-06-17  
**状态：** 7.1–7.6 **全部完成**

---

## 一、目标（Goal）

Phase 6 已消除**用户运行时**对 PowerShell 应用栈的依赖。Phase 7 聚焦：

1. 仓库根目录**开发/手工启动器**改为 cmd + exe（不再保留等价 `.ps1`）
2. 诊断与遗留开发脚本去 PS（7.2、7.3）
3. **可选**构建链与资源管线现代化（7.4、7.5）

**保留 PowerShell 的范围（Phase 7 结束后仍允许）：**

- `Run-Tests.ps1`、`Tests/*.ps1`（Pester 与安装器集成）
- `scripts/Publish-All.ps1`、`installer/*.ps1`（`Publish-All` 委托 `build.cmd`）
- `lib/Create-TrayIcon.ps1`（仅校验 ico）

**非目标：**

- 不重写 Inno 编译入口
- 不要求删除真实安装/卸载 Pester 集成测试
- 不改变安装包载荷（仍无 PS 脚本）

---

## 二、切片与状态

| 切片 | 代号 | 交付 | 状态 |
|------|------|------|------|
| **7.1** | 7P-launchers-dev | 根目录启动/注册脚本 cmd-only；删除等价 `.ps1` | **已完成** |
| **7.2** | 7P-status | `Status.cmd` 纯 cmd 显示启动日志末 8 行 | **已完成** |
| **7.3** | 7P-legacy | 删除 `Repair-*`；更新 `Setup-All`/`Run-Tests.cmd`；归档迁移脚本 | **已完成** |
| **7.4** | 7P-xaml | `lib/SmartGuard.Settings.xaml` 为源；删除生成脚本 | **已完成** |
| **7.5** | 7P-publish | `build.cmd` + `Directory.Build.props`；`Publish-All` 委托 | **已完成** |
| **7.6** | 7P-docs | `MIGRATION.md`、README 与契约同步 | **已完成** |

**并行轨道：** Phase **5.3** 干净 VM / 人工验收（不阻塞 Phase 7）。

---

## 三、7.1 — 开发启动器去重 `7P-launchers-dev`

### 动机

`Start-Core.cmd` 已 VBS 提权 + exe；`Start-Core.ps1` 仍用 `powershell -Verb RunAs`，功能重复。

### 交付物

| 动作 | 文件 |
|------|------|
| **新增** | `Register-AllTasks.cmd` — `Engine.exe --root … --install --skip-publish` |
| **新增** | `Start-SmartGuard.cmd` — 转调 `Start-Core.cmd` |
| **删除** | `Start-Core.ps1`、`Register-AllTasks.ps1`、`Restart-Tray.ps1`、`Start-SmartGuard.ps1` |
| **保留** | `Start-Core.cmd`、`Start-Tray.cmd`、`Restart-Tray.cmd` |

### 验收

- [x] 根目录无上述四个 `.ps1`
- [x] Pester `Phase 7.1`：`Register-AllTasks.cmd`、`Start-SmartGuard.cmd` 含预期逻辑且不含 `powershell.exe`
- [x] `Run-Tests.ps1` 全绿

---

## 四、7.2 — 诊断无 PS `7P-status`

### 交付

- `Status.cmd` 用 `more +SKIP` 显示 `SmartGuard.startup.log` 末 8 行，**不**调用 PowerShell

### 验收

- [x] Pester 断言 `Status.cmd` 无 `powershell`
- [x] 手工：12 行日志时输出末 8 行（`line 5`–`line 12`）

---

## 五、7.3 — 遗留开发脚本 `7P-legacy`

| 文件 | 动作 |
|------|------|
| `Repair-Encoding.ps1` / `Repair.cmd` | **已删除**（会重建已废止 PS 栈） |
| `Setup-All.cmd` | `%~dp0` + `Publish-All` + `Run-Tests.ps1` + `Register-AllTasks.cmd` |
| `Run-Tests.cmd` | `%~dp0` 相对路径 |
| `scripts/Migrate-RenameToSmartGuard.ps1` | 移至 `scripts/archive/` |

### 验收

- [x] Pester `Phase 7.3` 全过
- [x] `lib/README-DEPLOY.txt` 同步

---

## 六、7.4 — XAML 源文件化 `7P-xaml`

| 动作 | 说明 |
|------|------|
| **保留** | `lib/SmartGuard.Settings.xaml` 为唯一源（Settings csproj `Page` 链接） |
| **删除** | `lib/Write-SmartGuardSettingsXaml.ps1` |
| **Build-Staging** | 不再调用 XAML 生成；仅复制已有 `lib\SmartGuard.Settings.xaml` |

### 验收

- [x] Pester `Phase 7.4` 全过
- [x] 安装包仍含 `lib\SmartGuard.Settings.xaml`

---

## 七、7.5 — dotnet 发布链 `7P-publish`

| 交付 | 说明 |
|------|------|
| `build.cmd` | 顺序 `dotnet publish` 四项目到 `bin\` |
| `Directory.Build.props` | `<SelfContained>false</SelfContained>` |
| `scripts/Publish-All.ps1` | 薄包装，调用 `build.cmd` |
| **删除** | `scripts/Publish-{Engine,Tray,LogViewer,Settings}.ps1` |
| `installer/Build-Staging.ps1` | 发布步骤改调 `build.cmd` |

### 验收

- [x] Pester `Phase 7.5` 全过
- [x] `build.cmd` 产出四 exe

---

## 八、7.6 — 文档 `7P-docs`

| 交付 | 说明 |
|------|------|
| `docs/MIGRATION.md` | Phase 7 完成态、测试计数、`build.cmd` 引用表 |
| `README.md` | `build.cmd` 首选、`Register-AllTasks.cmd`、Phase 7 契约链接 |
| `docs/INNO-INSTALLER-TASK-CONTRACT.md` | Build-Staging 步骤与发布链同步 |
| `docs/PHASE-6-TASK-CONTRACT.md` | 保留 PS 范围与 6+ 状态 |
| 证据清单 | `phase-5.3-vm-checklist.md`、冒烟报告测试计数 |

### 验收

- [x] Pester `Phase 7.6` 全过
- [x] 文档不再引用已删除的 `Publish-*.ps1` 分脚本 / `Write-SmartGuardSettingsXaml`

---

## 九、测试策略

| 层级 | 要求 |
|------|------|
| Pester 契约 | 每切片增加「无 `powershell.exe`」或「`.ps1` 已删除」断言 |
| 集成 | `InstallerUserFlow` 5 项保持全绿 |
| 门禁 | 每切片：`Run-Tests.ps1` 全绿 |

---

## 十、完成标准（Phase 7 Done）

- [x] 7.1–7.6 全部完成
- [x] 用户安装包仍与 Phase 6 一致
- [x] `Run-Tests.ps1` 全绿

---

## 十一、变更记录

| 日期 | 变更 |
|------|------|
| 2026-06-17 | 7.4–7.5：`build.cmd` 发布链；XAML 源文件化 |
| 2026-06-17 | 7.6：文档与证据同步；Phase 7 收尾 |
