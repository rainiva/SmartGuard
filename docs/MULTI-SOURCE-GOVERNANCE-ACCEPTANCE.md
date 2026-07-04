# 多真源治理终验报告

**日期：** 2026-07-02（第六轮治理全量修复后）  
**Run-Tests.ps1（`SMARTGUARD_SKIP_INSTALLER_TESTS=1`）：** 见下方终验输出

## 第一轮（深挖治理）

**Run-Tests.ps1：** `PASSED=54 FAILED=0 TOTAL=54`  
（Pester **52** + Architecture **39** + dotnet 全绿 + Tray 集成 **2**）

## 第二轮关闭项

| 类别 | ID | 状态 |
|------|-----|------|
| 多源 | S-01～S-08 | **已关闭** |
| 多入口 | E-01, E-02, E-04～E-07 | **已关闭** |
| 多入口 | E-03, E-08 | **登记/折中** |
| 上帝模块 | SettingsLogPageHost | **&lt;300 行** + 子模块提取 |
| 展示层 | L-01 | **已关闭（非配置真源）** |

## 第三轮关闭项

| 类别 | ID | 状态 |
|------|-----|------|
| 多源 | M-06, M-07 | **已关闭**（Tray cache 两参 ctor；主题经 `SettingsSaveCoordinator` 落盘） |
| 多入口 | ME-08 | **已关闭**（`SettingsLogsPageLauncher`） |
| 脚本/测试 | ME-03, ME-12, LOG-SCRIPT | **已关闭** |
| 脚本/测试 | ME-01 | **登记**（基准脚本 `# benchmark-only-start`） |
| 上帝模块 | Toast, Policy, About, AppDialog, Tray | **&lt;300 行** + 拆分模块 |

## 第四轮关闭项

| 类别 | ID | 状态 |
|------|-----|------|
| 多入口 | ME-09 | **已关闭**（`SettingsMainPageLauncher.Open`） |
| 文档 | DOC-01～03 | **已关闭**（`ARCHITECTURE-CONTRACT` §2/§6/§9/§10 对齐） |
| 文档 | ME-12-R | **已关闭**（集成测试注释移除 `Publish-All`） |
| 上帝模块 | GOD-01 | **已关闭**（`LogSearchFilterBar` &lt;300 行门禁） |
| 多入口 | E-10 | **阻断**（shell user entry 只能经 `DesktopShellRegistration` 落地） |
| 测试稳定性 | PERF | **已关闭**（启动 5000ms 预算 + stop 后 settle） |

## 新增 Architecture 门禁（第三轮 + 第四轮）

- `SettingsThemeSaveArchitectureTests`
- `TrayDisplaySettingsCacheArchitectureTests`
- `SettingsLogsPageLauncherArchitectureTests`
- `SettingsMainPageLauncherArchitectureTests`
- `ArchitectureContractFourthRoundTests`
- `LogSearchFilterBarLineCountTests`
- `DevTrayScriptArchitectureTests`
- `EnginePerformanceStopArchitectureTests`
- `EnginePerformanceStartupArchitectureTests`
- `MeasureEngineStartupArchitectureTests`
- `PublishAllReferenceArchitectureTests`
- `ToastNotificationLineCountTests`
- `SettingsPolicyCoordinatorLineCountTests`
- `SettingsAboutCoordinatorLineCountTests`
- `AppDialogLineCountTests`
- `TrayApplicationContextLineCountTests`

## Settings / Tray 模块行数

| 模块 | 门禁 |
|------|------|
| `SettingsWindowController` | &lt;300 行 |
| `SettingsLogPageHost` | &lt;300 行 |
| `SettingsPolicyCoordinator` | &lt;300 行 |
| `SettingsAboutCoordinator` | &lt;300 行 |
| `AppDialog` | &lt;300 行 |
| `ToastNotification`（拆分后） | 各文件 &lt;300 行 |
| `TrayApplicationContext` | &lt;300 行 |
| `LogSearchFilterBar` | &lt;300 行 |

## 验证命令

```powershell
$env:SMARTGUARD_SKIP_INSTALLER_TESTS = '1'
./Run-Tests.ps1
```

## 终验输出（第四轮）

```
PASSED=56 FAILED=0 TOTAL=56
Pester: 54/54
Architecture: 101/101
Tray integration: 2/2
（SMARTGUARD_SKIP_INSTALLER_TESTS=1，2026-07-02）
```

## 终验输出（第四轮）

```
PASSED=56 FAILED=0 TOTAL=56
Pester: 54/54
Architecture: 101/101
Tray integration: 2/2
（SMARTGUARD_SKIP_INSTALLER_TESTS=1，2026-07-02）
```

## 第五轮关闭项（2026-07-02）

| 类别 | ID | 状态 |
|------|-----|------|
| 多源 | M-17～M-19 | **已关闭**（`SmartGuardPaths` 单源：startup log、exe 路径、进程镜像名） |
| 上帝模块 | GOD-02 | **已关闭**（`UpdateDownloadProgressWindowFactory` + 行数门禁） |
| 上帝模块 | GOD-03 | **已关闭**（`SettingsPauseHandler` + `SettingsDebouncedSaver`） |
| 上帝模块 | GOD-04 | **已关闭**（`ToastRegistryWriter` + `StartMenuShortcutWriter`） |
| 上帝模块 | GOD-05 | **已关闭**（`GuardianFirstRunInitializer` + `GuardianExceptionStormHandler`） |
| 多入口 | E-11, E-11b | **阻断**（install/uninstall 只能经 `InstallCommands`；Inno 不得再拥有用户快捷方式） |
| 文档 | DOC-04 | **已关闭**（§2 `StopForUninstall`；§11 第五轮索引） |

## 新增 Architecture 门禁（第五轮）

- `SmartGuardPathsSingleSourceArchitectureTests`
- `SettingsUpdateCheckCoordinatorLineCountTests`
- `SettingsUpdateCheckCoordinatorArchitectureTests`
- `ArchitectureContractFifthRoundTests`
- `ToastAumidRegistrarArchitectureTests`
- `GuardianLoopArchitectureTests`

## Settings / Tray / Engine 模块行数（第五轮后）

| 模块 | 门禁 |
|------|------|
| `SettingsPolicyCoordinator` | 231 行（&lt;300） |
| `SettingsUpdateCheckCoordinator` | 221 行（&lt;300） |
| `ToastAumidRegistrar` | 49 行（&lt;300） |
| `GuardianLoop` | 143 行（&lt;300） |

## 结论

第四轮审计 open 项均已关闭或登记：设置 spawn 与日志 spawn 对称单源、契约文档与实现对齐、LogSearchFilterBar 行数门禁、dev Tray 脚本登记、性能测试在全量套件负载下稳定。

第五轮在第四轮基线上收敛 C# 路径小重复、Settings Policy/Update 上帝模块预防拆分、Tray Toast 注册与 Engine 主循环职责分离；M-15 折中维持登记，未收紧 schtasks-only 停止策略。

## 第六轮关闭项（2026-07-02）

| 类别 | ID | 状态 |
|------|-----|------|
| 多源 | M-20 | **已关闭**（`SmartGuardPaths`：`SettingsXaml`、Init/Elevation marker） |
| 多源 | M-21 | **已关闭**（`SmartGuardPathConstants.ps1/.cmd` + Inno `#define` parity） |
| 多源 | S-09 | **已关闭**（Settings 暂停开关初始展示读 `status.json`） |
| 多入口 | E-11 | **阻断**（提升后的 install/uninstall 只能委托 `InstallCommands` + `DesktopShellRegistration`） |
| 多入口 | E-11b | **阻断**（Inno 不得再拥有 `[Icons]` 用户快捷方式） |
| 文档 | DOC-05 | **已关闭**（§12 第六轮索引 + 本终验） |

## 新增 Architecture 门禁（第六轮）

- `SmartGuardPathsExtensionsArchitectureTests`
- `ScriptPathConstantsParityArchitectureTests`
- `SettingsPauseDisplayArchitectureTests`
- `ElevationSingleSourceArchitectureTests`
- `ElevationHelperTests`
- `InnoShortcutWrapperArchitectureTests`
- `ArchitectureContractSixthRoundTests`

第六轮在路径/脚本常量、Settings 暂停展示、UAC 与 Inno 包装脚本上完成收敛；第七轮将业务 cmd 迁入 `SmartGuard.Cli` + `SmartGuard.Configuration` 操作真源，删除 `Open-*.cmd`、`Status.cmd`、`Setup-All.cmd` 等运行时入口。

## 第七轮关闭项（2026-07-02）

| 类别 | ID | 状态 |
|------|-----|------|
| 多源 | R7-OPS | **已关闭**（`GuardianTaskRunner`、`TrayLauncher`、`TrayLifecycle`、`StatusReporter`；`InstallCommands` 在 Configuration） |
| 多入口 | R7-CLI | **已关闭**（`SmartGuard.Cli.exe` 薄路由；dev cmd 单行转发） |
| 多入口 | R7-INNO | **阻断**（用户 shell 入口不得回流到 Inno `[Icons]`） |
| 多源 | R7-PUBLISH | **阻断**（分发清单必须收敛到 `DesktopDistributionManifest`） |
| 文档 | DOC-06 | **已关闭**（§13 第七轮索引 + 本终验） |

## 新增 Architecture 门禁（第七轮）

- `GuardianTaskRunnerArchitectureTests`
- `CliThinFacadeArchitectureTests`
- `InnoShortcutWrapperArchitectureTests`（更新：禁止 `Open-*.cmd`）
- `PublishSingleSourceArchitectureTests`
- `StatusReporterArchitectureTests`
- `ArchitectureContractSeventhRoundTests`

## 终验输出（第七轮）

```
PASSED=61 FAILED=0 TOTAL=61
Pester: 59/59
Architecture: 179/179
Tray integration: 2/2
（SMARTGUARD_SKIP_INSTALLER_TESTS=1，2026-07-02）
```

## 终验输出（第六轮）

```
PASSED=60 FAILED=0 TOTAL=60
Pester: 58/58
Architecture: 143/143
Tray integration: 2/2
（SMARTGUARD_SKIP_INSTALLER_TESTS=1，2026-07-02）
```

## 第八轮绝对收口（2026-07-03）

| 切片 | 最终状态 |
|------|------|
| shell owner | **阻断生效**：`InstallCommands -> DesktopShellRegistration` 是唯一 shell owner；live shell surface 仅为开始菜单 `SmartGuard.lnk` 与可选桌面 `SmartGuard.lnk`；Tray / Notifier 不得 runtime 自注册或自愈 AUMID |
| xaml/icon 真源 | **阻断生效**：`SmartGuard.Settings.xaml` 只允许项目内 embedded source；官方原始图标帧只允许位于 `assets/branding/official/`，live build-time 品牌图标只允许是 `assets/branding/SmartGuard.ico`；`installRoot\lib` 不再承载 runtime 真源 |
| distribution manifest | **阻断生效**：发布项目、payload、installer 进程与 legacy cleanup 只允许来自 `DesktopDistributionManifest`；Inno 仅消费生成的 include |
| LogViewer 退场 | **阻断生效**：`SmartGuard.LogViewer.exe` 与 `SmartGuard.LogViewer` 命名空间/测试项目身份不再是 live product artifact；日志功能仅保留 `SmartGuard.Settings.LogView` 内部身份与 `SmartGuard.Settings.exe --page logs` 运行时入口 |

## 终验输出（第八轮）

```
PASSED=68 FAILED=0 TOTAL=68
Pester: 61/61
Architecture: 187/187
Engine: 104/104
Configuration: 96/96
Cli: 1/1
Packaging: 14/14
Tray: 58/58
LogViewer: 36/36
Settings: 268/268
Tray integration: 2/2
Installer integration: 5/5
Installer integration output: dist\SmartGuard-Setup-1.1.6-x64.exe
Installer rebuild output: dist\SmartGuard-Setup-1.1.7-x64.exe
（2026-07-03）
```

## 结论

第七轮将业务 cmd 收敛为 `SmartGuard.Cli.exe` 薄路由 + `SmartGuard.Configuration` 操作真源；删除 `Open-*.cmd`、`Status.cmd`、`Setup-All.cmd`、`SmartGuardPathConstants.cmd` 与 `Request-AdminElevation.cmd`；`DesktopSuitePublisher` 单源 publish；Inno 快捷方式经 `SmartGuard.Cli.exe open *`。DEV-ONLY 保留 `Run-Tests.cmd`、`Clean-Workspace.cmd`。

截至 2026-07-03，E-10、E-11、E-11b、R7-INNO、R7-PUBLISH 都必须被视为当前阻断规则，而不是历史上已关闭的例外：shell user entry 只能由 `DesktopShellRegistration` 落地，提升后的 install/uninstall 只能经 `InstallCommands`，Inno 不得再拥有 `[Icons]` 用户快捷方式，分发清单只能来自 `DesktopDistributionManifest`。

同一轮绝对收口还要求：runtime 不得继续自愈 shell/AUMID，不得再把 `installRoot\lib\SmartGuard.Settings.xaml` 或 `installRoot\lib\SmartGuard.ico` 当作真源，shell surface 只能是一个开始菜单主入口加可选桌面入口，`SmartGuard.LogViewer` 产品身份不得残留（`LegacyCleanupPaths` 仅用于升级清理旧 `SmartGuard.LogViewer.*` 文件）；所有遗留物只能在 install/repair 期间清理。

## 第九轮 repo-governance 收口（2026-07-03）

| 切片 | 最终状态 |
|------|------|
| repo-governance 边界 | **阻断生效**：历史 task-contract / migration / diagnosis 文档只允许位于 `docs/archive/**`；历史脚本只允许位于 `scripts/archive/**`；`docs/superpowers/plans/**` 仅作计划记录，不得充当 live authority |
| dev asset generation | **阻断生效**：live repo 不得保留 `assets/icon/**` 草稿树，也不得保留旧图标 draft / generator 脚本；唯一允许的 live 图标源层是 `assets/branding/official/`，唯一允许的 live build-time 图标资产是 `assets/branding/SmartGuard.ico`；历史脚本或旧产物描述只允许位于 archive 路径 |
| gate 豁免机制 | **阻断生效**：Repo grep / contract gates 对旧真相的豁免只能按 path boundary（`docs/archive/**`、`scripts/archive/**`、`docs/superpowers/plans/**`），不得再按 `PHASE-`、`MIGRATION`、日期、单文件名做豁免 |

## 第十轮 live entry/source 收口（2026-07-03）

| 切片 | 最终状态 |
|------|------|
| third-pass installer closeout | **阻断生效**：`SmartGuard.iss` 不得手写 live exe/task literals；只能消费 `generated/SmartGuard.Distribution.Constants.iss.inc` 与 `generated/SmartGuard.Distribution.Run.iss.inc` |
| third-pass legacy cleanup closeout | **阻断生效**：legacy task names 只允许存在于 `LegacyScheduledTaskCatalog`；live `scripts/` 不得保留 `LegacyScheduledTaskNames.ps1` / `Unregister-LegacySmartPowerPlanTasks.ps1`；历史脚本仅 `scripts/archive/**` |
| contract history pointers | **阻断生效**：live `docs/ARCHITECTURE-CONTRACT.md` 不得保留指向已归档 migration 文档的索引指针 |

## 终验输出（第十轮）

```
PASSED=77 FAILED=0 TOTAL=77
Pester: 75/75
Architecture: 201/201
Engine: 104/104
Configuration: 102/102
Cli: 1/1
Packaging: 14/14
Tray: 58/58
LogView: 36/36
Settings: 268/268
Tray integration: 2/2
（SMARTGUARD_SKIP_INSTALLER_TESTS=1，2026-07-03）
```

## 结论（第十轮）

第三轮 live entry/source 收口完成：`SmartGuard.iss` 活跃 exe/task literal 已迁至 `InnoDistributionManifestWriter` 生成的 Constants/Run include；`LegacyScheduledTaskCatalog` 成为唯一 live 旧任务名真源；历史 cleanup 脚本已移至 `scripts/archive/**`。但截至 2026-07-03 最终收口前，`StopSmartGuardProcesses()` 仍残留手写 engine exe literal、tray 仍使用 `SystemIcons.Shield` fallback、truth gate 仍可能误伤 docs/tests 合法断言文本——见第十一轮。

## 第十一轮最终收口（2026-07-03）

| 切片 | 最终状态 |
|------|------|
| final inno stop closeout | **阻断生效**：`StopSmartGuardProcesses()` 只消费 `{#EngineExeName}`，不再手写 `SmartGuard.Engine.exe` |
| final tray icon closeout | **阻断生效**：`TrayIconLoader` 不再使用 `SystemIcons.Shield`；runtime brand icon 仅 embedded chain |
| final truth gate closeout | **阻断生效**：implementation-path grep 与 docs/tests contract gate 分离，验收口径与 live repo 一致 |
| final implementation-path grep | **阻断生效**：只扫描 `installer/`、`src/`、活动 `scripts/`（排除 `scripts/archive/**`）中的 live residual；不得再把 docs/tests 的合法断言文本当作 runtime 命中 |
| docs/tests mention policy | **阻断生效**：docs/tests may mention retired literals only as assertions or contract text；retired literal 在 `docs/archive/**` 中可作为历史快照保留；在 live docs/tests 中仅允许作为负断言、契约文本、gate 说明出现，不得宣称它仍是 live truth |
| icon chain consumer rule | **阻断生效**：允许的 Windows consumers 仅 `ApplicationIcon`、`window.Icon`、`Tray.exe,0`、`IconUri=Tray.exe`；它们不得指向 loose `.ico` 或系统 shield fallback |

## 结论（第十一轮）

最终收口完成：Inno stop chain 消费 generated `{#EngineExeName}`；tray/WPF icon chain embedded-only 且 fail-fast；Pester Phase 8 以 **implementation-path grep** 阻断 live residual，同时允许 docs/tests 以契约/断言形式提及 retired literals。

## 终验输出（第十一轮）

```
PASSED=81 FAILED=0 TOTAL=81
Pester: 79/79
Architecture: 205/205
Engine: 104/104
Configuration: 103/103
Cli: 1/1
Packaging: 14/14
Tray: 59/59
LogView: 36/36
Settings: 268/268
Tray integration: 2/2
（SMARTGUARD_SKIP_INSTALLER_TESTS=1，2026-07-03）
```
