# SmartGuard PowerShell 脚本精简与构建统一化设计文档

**制定日期：** 2026-06-18  
**状态：** 待批准  
**关联契约：** [PHASE-7-TASK-CONTRACT.md](PHASE-7-TASK-CONTRACT.md)（Phase 7 已完成，本文档为 Phase 7 后续增强）

---

## 一、目标

在 Phase 7 已完成"开发机去 PowerShell 应用栈"的基础上，进一步**精简构建与测试相关的 PowerShell 脚本**，将构建入口统一为 `dotnet` CLI / MSBuild 体系，减少维护负担。

**核心原则：**
- 能进 MSBuild / `dotnet` 的进 MSBuild，必须留 PowerShell 的做精简
- 不删除任何集成测试（Pester 安装器/托盘集成测试保留）
- 不改动 Inno Setup 编译逻辑（ISCC.exe 调用保留）
- 所有变更需通过 `Run-Tests.ps1` 全绿验证

---

## 二、现状分析

### 2.1 现有 PowerShell 脚本（17 个）

| 目录 | 文件 | 功能 | 当前必要性 |
|------|------|------|-----------|
| 根目录 | `Run-Tests.ps1` | 运行 Pester + xUnit 测试 | **高** — 测试总入口 |
| `Tests/Integration/` | `InstallerUserFlow.Helpers.ps1` | 安装器集成测试辅助 | **高** — 集成测试依赖 |
| `Tests/Integration/` | `InstallerUserFlow.Tests.ps1` | 安装器集成测试 | **高** — 端到端验证 |
| `Tests/Integration/` | `TrayCoreUserFlow.Helpers.ps1` | 托盘集成测试辅助 | **高** — 集成测试依赖 |
| `Tests/Integration/` | `TrayCoreUserFlow.Tests.ps1` | 托盘集成测试 | **高** — 端到端验证 |
| `Tests/` | `SmartGuard.Tests.ps1` | Pester 契约测试（43 项） | **高** — 构建链验证 |
| `installer/` | `Build-Installer.ps1` | 构建 Inno Setup 安装包 | **高** — 调用 ISCC.exe |
| `installer/` | `Build-Staging.ps1` | 准备安装器 staging 素材 | **中** — 可部分迁移到 MSBuild |
| `installer/` | `InstallVersion.ps1` | 版本号 bump 逻辑 | **低** — 可替换为 MSBuild/GitVersion |
| `installer/` | `InstallStaging.ps1` | staging 布局验证辅助 | **中** — 测试辅助函数 |
| `scripts/` | `Publish-All.ps1` | 调用 `build.cmd` 的薄包装 | **低** — 与 `build.cmd` 完全冗余 |
| `scripts/` | `Measure-EngineStartup.ps1` | 测量引擎启动耗时 | **低** — 开发诊断工具 |
| `scripts/` | `Measure-EngineMemory.ps1` | 测量引擎内存占用 | **低** — 开发诊断工具 |
| `scripts/` | `Test-IsProcessElevated.ps1` | UAC 提权检测与重试 | **高** — `Run-Tests.ps1` 依赖 |
| `scripts/` | `Unregister-LegacySmartPowerPlanTasks.ps1` | 卸载旧版计划任务 | **低** — 一次性迁移脚本 |
| `scripts/archive/` | `Migrate-RenameToSmartGuard.ps1` | 一次性更名迁移 | **低** — 已归档 |
| `lib/` | `Create-TrayIcon.ps1` | 验证图标存在 | **低** — 功能极简，可合并 |

### 2.2 现有构建链路

```
Setup-All.cmd
├── [1/3] scripts/Publish-All.ps1  →  调用 build.cmd
│   └── build.cmd  →  dotnet publish 四项目到 bin/
├── [2/3] Run-Tests.ps1
│   ├── Pester: SmartGuard.Tests.ps1 (43 项)
│   ├── xUnit: 5 个测试项目 (157 项)
│   └── Pester: Integration/*.Tests.ps1 (7 项)
└── [3/3] Register-AllTasks.cmd  →  Engine.exe --install

Build-Installer.ps1
├── Build-Staging.ps1
│   ├── build.cmd (dotnet publish)
│   ├── Create-TrayIcon.ps1 (验证 ico)
│   ├── 复制 bin/ → staging/bin/
│   ├── 复制 lib/ → staging/lib/
│   ├── 下载 .NET Desktop Runtime redist
│   └── Test-InstallerStagingLayout
└── ISCC.exe 编译 .iss → dist/
```

### 2.3 问题识别

| 问题 | 影响 | 优先级 |
|------|------|--------|
| `Publish-All.ps1` 与 `build.cmd` 功能完全冗余 | 维护两份发布入口 | P1 |
| `Create-TrayIcon.ps1` 仅做文件存在验证 | 独立脚本过度设计 | P2 |
| `InstallVersion.ps1` 的 bump 逻辑可用 GitVersion 替代 | 手动维护版本号文件 | P2 |
| `Measure-Engine*.ps1` 使用频率低却占根目录 | 开发工具混杂 | P3 |
| `Setup-All.cmd` 仍通过 PowerShell 调用 `Publish-All.ps1` | 间接增加 PS 依赖 | P2 |
| `Build-Staging.ps1` 的"复制文件+下载 redist"可用 MSBuild 任务 | 构建逻辑分散 | P2 |

---

## 三、设计方案

### 3.1 总体架构（变更后）

```
构建入口统一层
├── build.cmd          ← 唯一编译入口（已有，保留）
├── Run-Tests.ps1      ← 唯一测试入口（保留，精简）
└── Build-Installer.ps1 ← 唯一安装包入口（保留，调用简化）

MSBuild 增强层（新增）
├── Directory.Build.props  ← 增加版本号、staging 复制 Target
└── installer/Staging.targets  ← 替代 Build-Staging.ps1 的文件复制逻辑

精简保留的 PowerShell（8 个）
├── Run-Tests.ps1
├── Test-IsProcessElevated.ps1
├── Build-Installer.ps1
├── Tests/SmartGuard.Tests.ps1
├── Tests/Integration/*.ps1（4 个）
└── installer/InstallStaging.ps1（测试辅助）

删除/归档（9 个）
├── scripts/Publish-All.ps1          → 删除（冗余）
├── scripts/Measure-EngineStartup.ps1 → 归档 scripts/dev/
├── scripts/Measure-EngineMemory.ps1  → 归档 scripts/dev/
├── scripts/Unregister-LegacySmartPowerPlanTasks.ps1 → 归档 scripts/archive/
├── lib/Create-TrayIcon.ps1           → 删除（合并到 Build-Staging）
├── installer/InstallVersion.ps1      → 删除（MSBuild 替代）
└── installer/Build-Staging.ps1       → 删除（MSBuild + Build-Installer 内联）
```

### 3.2 详细变更

#### 3.2.1 删除 `scripts/Publish-All.ps1`

**理由：** 功能与 `build.cmd` 完全重叠，仅多一层 PowerShell 包装。

**影响面：**
- `Setup-All.cmd` 第 7 行：`powershell -File scripts\
Publish-All.ps1` → 直接 `call build.cmd %Configuration%`
- `MIGRATION.md` 引用更新
- `SmartGuard.Tests.ps1` Phase 7.5 断言更新：删除 `Publish-All.ps1` 相关检查

**验收：**
- [ ] `scripts/Publish-All.ps1` 不存在
- [ ] `Setup-All.cmd` 直接调用 `build.cmd`
- [ ] Pester `Phase 7.5` 断言更新并全过
- [ ] `build.cmd` 产出四 exe 不变

---

#### 3.2.2 删除 `lib/Create-TrayIcon.ps1`，合并到 Build-Staging 流程

**理由：** 仅验证 `lib/SmartGuard.ico` 存在，可在 `Build-Staging` 阶段内联检查。

**变更：**
- 删除 `lib/Create-TrayIcon.ps1`
- `Build-Staging.ps1` 中的图标检查逻辑内联到 `Build-Installer.ps1` 或 MSBuild Target

**验收：**
- [ ] `lib/Create-TrayIcon.ps1` 不存在
- [ ] Pester `Tray assets` 测试更新：不再调用 `Create-TrayIcon.ps1`
- [ ] `build.cmd` 或 MSBuild 在编译前验证 `lib/SmartGuard.ico` 存在

---

#### 3.2.3 删除 `installer/InstallVersion.ps1`，版本号由 MSBuild 管理

**理由：** 手动维护 `version.txt` 并 bump 容易出错，MSBuild 可从 Git tag 或 AssemblyInfo 自动生成。

**方案：**
- 使用 `Directory.Build.props` 定义 `<Version>` 属性
- 或引入 [GitVersion.MsBuild](https://gitversion.net/docs/reference/msbuild) 自动生成语义化版本
- `Build-Installer.ps1` 从 MSBuild 输出读取版本号，不再读写 `version.txt`

**验收：**
- [ ] `installer/InstallVersion.ps1` 不存在
- [ ] `installer/version.txt` 不再需要（或仅作为缓存）
- [ ] `Build-Installer.ps1` 仍正确传递版本号给 ISCC.exe
- [ ] Pester `Phase 5` 版本 bump 测试更新或移除

---

#### 3.2.4 将 `installer/Build-Staging.ps1` 迁移到 MSBuild Target

**理由：** "复制文件 + 下载 redist" 是纯构建任务，适合 MSBuild。

**方案：**
- 新增 `installer/Staging.targets`：
  - `CopyPayload` Target：复制 `bin/`、`lib/`、`license_zh-CN.txt` 到 `installer/staging/`
  - `DownloadRuntimeRedist` Target：下载 .NET Desktop Runtime（使用 MSBuild 的 `DownloadFile` 任务）
  - `ValidateStaging` Target：调用 `Test-InstallerStagingLayout`
- `Build-Installer.ps1` 简化：调用 `dotnet build` 触发 Staging.targets，然后调用 ISCC.exe

**保留的 PowerShell 部分：**
- `Test-InstallerStagingLayout` 仍在 `InstallStaging.ps1` 中（测试辅助函数）
- `Ensure-DotNetDesktopRuntimeRedist` 的下载逻辑迁移到 MSBuild，但保留失败回退

**验收：**
- [ ] `installer/Build-Staging.ps1` 不存在
- [ ] `dotnet build` 触发 staging 准备
- [ ] `Build-Installer.ps1` 仍能成功编译安装包
- [ ] Pester `Phase 5` staging 测试更新并全过

---

#### 3.2.5 归档开发诊断脚本到 `scripts/dev/`

**理由：** `Measure-Engine*.ps1` 使用频率低，不应与核心构建脚本混放。

**变更：**
- 新建 `scripts/dev/` 目录
- 移动 `Measure-EngineStartup.ps1`、`Measure-EngineMemory.ps1` 到 `scripts/dev/`
- 移动 `Unregister-LegacySmartPowerPlanTasks.ps1` 到 `scripts/archive/`（已归档）

**验收：**
- [ ] `scripts/Measure-Engine*.ps1` 不存在于根 `scripts/`
- [ ] `scripts/dev/` 存在且包含两个测量脚本
- [ ] `MIGRATION.md` 引用路径更新

---

#### 3.2.6 精简 `Run-Tests.ps1`

**理由：** Pester 自动安装逻辑增加复杂度和外部依赖。

**变更：**
- 移除 Pester 自动安装逻辑（要求开发机预装 Pester 5.x）
- 保留 UAC 提权检测（`Test-IsProcessElevated.ps1`）
- 保留 UTF-8 编码设置
- 保留 xUnit 5 项目 + Pester 集成测试调用链

**验收：**
- [ ] `Run-Tests.ps1` 不再包含 `Install-Module Pester`
- [ ] 文档说明需预装 Pester 5.x
- [ ] `Run-Tests.ps1` 仍全绿

---

### 3.3 变更汇总表

| 文件 | 动作 | 替代方案 | 风险 |
|------|------|---------|------|
| `scripts/Publish-All.ps1` | 删除 | `build.cmd` 直接调用 | 低 |
| `lib/Create-TrayIcon.ps1` | 删除 | MSBuild 内联验证 | 低 |
| `installer/InstallVersion.ps1` | 删除 | MSBuild `<Version>` 或 GitVersion | 中（需验证版本传递） |
| `installer/Build-Staging.ps1` | 删除 | `installer/Staging.targets` | 中（MSBuild 学习成本） |
| `scripts/Measure-EngineStartup.ps1` | 归档 | `scripts/dev/` | 低 |
| `scripts/Measure-EngineMemory.ps1` | 归档 | `scripts/dev/` | 低 |
| `scripts/Unregister-LegacySmartPowerPlanTasks.ps1` | 归档 | `scripts/archive/` | 低 |
| `Setup-All.cmd` | 修改 | 直接调用 `build.cmd` | 低 |
| `Directory.Build.props` | 修改 | 增加版本号、staging Target | 中 |
| `Run-Tests.ps1` | 修改 | 移除 Pester 自动安装 | 低 |
| `SmartGuard.Tests.ps1` | 修改 | 更新被删除文件的断言 | 低 |
| `MIGRATION.md` | 修改 | 更新引用路径 | 低 |

---

## 四、测试策略

### 4.1 测试层级

| 层级 | 内容 | 验证方式 |
|------|------|---------|
| **单元测试** | xUnit 157 项 | `dotnet test` |
| **契约测试** | Pester 43 项 | `SmartGuard.Tests.ps1` |
| **集成测试** | Pester 7 项 | `Tests/Integration/*.Tests.ps1` |
| **构建验证** | `build.cmd` 产出四 exe | 文件存在 + 版本检查 |
| **安装器验证** | `Build-Installer.ps1` 产出 `dist/*.exe` | 文件存在 + 版本检查 |

### 4.2 每切片验收

每个变更切片完成后必须：
1. `Run-Tests.ps1` 全绿
2. 手工验证受影响的功能（如 `build.cmd`、`Build-Installer.ps1`）
3. Pester 相关断言更新并全过

---

## 五、完成标准（Done Criteria）

- [ ] 17 个 PowerShell 脚本精简至 8 个
- [ ] `build.cmd` 成为唯一编译入口
- [ ] `Run-Tests.ps1` 成为唯一测试入口
- [ ] `Build-Installer.ps1` 成为唯一安装包入口
- [ ] `Setup-All.cmd` 不经过任何中间 `.ps1` 直接调用 `build.cmd`
- [ ] `Run-Tests.ps1` 全绿（Pester + xUnit）
- [ ] `Build-Installer.ps1` 成功产出 `dist/SmartGuard-Setup-*-x64.exe`
- [ ] 文档（README、MIGRATION）同步更新

---

## 六、回滚方案

若任何变更导致测试失败或构建中断：
1. 回滚该切片的文件变更（git revert）
2. 恢复被删除的 PowerShell 脚本
3. 重新运行 `Run-Tests.ps1` 确认基线恢复

---

## 七、变更记录

| 日期 | 变更 |
|------|------|
| 2026-06-18 | 设计文档起草 |
