# Phase 7：开发机去 PowerShell — Task Contract

**制定日期：** 2026-06-17  
**状态：** 7.1、7.2 **已完成**；7.3–7.6 未开始

---

## 一、目标（Goal）

Phase 6 已消除**用户运行时**对 PowerShell 应用栈的依赖。Phase 7 聚焦：

1. 仓库根目录**开发/手工启动器**改为 cmd + exe（不再保留等价 `.ps1`）
2. 诊断与遗留开发脚本去 PS（7.2、7.3）
3. **可选**构建链与资源管线现代化（7.4、7.5）

**保留 PowerShell 的范围（Phase 7 结束后仍允许）：**

- `Run-Tests.ps1`、`Tests/*.ps1`（Pester 与安装器集成）
- `scripts/Publish-*.ps1`、`installer/*.ps1`（除非完成 7.5）
- `lib/Write-SmartGuardSettingsXaml.ps1`、`lib/Create-TrayIcon.ps1`（除非完成 7.4）

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
| **7.3** | 7P-legacy | `Repair`/`Setup-All` 等遗留脚本清理 | 未开始 |
| **7.4** | 7P-xaml | XAML 源文件化，去掉 PS 生成（可选） | 未开始 |
| **7.5** | 7P-publish | `dotnet publish` 替代 `Publish-*.ps1`（可选） | 未开始 |
| **7.6** | 7P-docs | `MIGRATION.md`、README 与契约同步 | 未开始 |

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
| `Repair-Encoding.ps1` / `Repair.cmd` | 无 UTF-16 残留则归档或删除 |
| `Setup-All.cmd` | 去硬编码路径；不引用已删 PS 栈 |
| `scripts/Migrate-RenameToSmartGuard.ps1` | 归档 |

---

## 六、7.4 / 7.5 — 可选

见 Phase 7 规划讨论稿；实施前须单独立项更新本文档。

---

## 七、7.6 — 文档

- `docs/MIGRATION.md` Phase 7 表
- `README.md` / `lib/README-DEPLOY.txt` 开发流程仅写 cmd + `Publish-All`

---

## 八、测试策略

| 层级 | 要求 |
|------|------|
| Pester 契约 | 每切片增加「无 `powershell.exe`」或「`.ps1` 已删除」断言 |
| 集成 | `InstallerUserFlow` 5 项保持全绿 |
| 门禁 | 每切片：`Run-Tests.ps1` 全绿 |

---

## 九、完成标准（Phase 7 Done）

- [ ] 7.1–7.3 + 7.6 完成（或明确跳过 7.4/7.5）
- [ ] 用户安装包仍与 Phase 6 一致
- [ ] `Run-Tests.ps1` 全绿

---

## 十、变更记录

| 日期 | 变更 |
|------|------|
| 2026-06-17 | 7.2：`Status.cmd` 纯 cmd tail，去掉 `powershell -Command Get-Content` |
