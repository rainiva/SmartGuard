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
| xaml/icon 真源 | **阻断生效**：`SmartGuard.Settings.xaml` 只允许项目内 embedded source；`assets/branding/SmartGuard.ico` 是唯一品牌图标真源；`installRoot\lib` 不再承载 runtime 真源 |
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
