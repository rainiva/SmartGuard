# 单入口单真源规则

这是 SmartGuard 的短治理入口。详细矩阵、历史轮次和测试索引以 [ARCHITECTURE-CONTRACT.md](../ARCHITECTURE-CONTRACT.md) 为准；这里专门声明当前不可回退的硬规则。

## Core Rules

1. Every concern must name exactly one `canonical owner`.
2. Any `fallback`, `marker`, runtime self-healing, compatibility stub, or duplicated resolver counts as a second owner and is forbidden.
3. User-visible shell entrypoints are installer-owned only: `InstallCommands -> DesktopShellRegistration` writes one start-menu shortcut `SmartGuard.lnk` and an optional desktop `SmartGuard.lnk` when the user opts in during install.
4. Settings layout is embedded-only: runtime must not read `installRoot\lib\SmartGuard.Settings.xaml`.
5. Runtime branding is embedded-only: `assets/branding/SmartGuard.ico` is the only build-time icon source, and runtime must not read `installRoot\lib\SmartGuard.ico`.
6. Distribution topology is manifest-owned only: payload, published projects, installer process list, and legacy cleanup must come from `DesktopDistributionManifest`.
7. Log entry is settings-only: `SmartGuard.Settings.exe --page logs` is the only live user entry; `SmartGuard.LogViewer.exe` must not return as a product artifact.
8. Shell damage or legacy residue must be repaired only by install/repair. Tray runtime and notifier runtime are not allowed to write shell state back.
9. Historical task-contract, migration, diagnosis, and support-plan docs live only under `docs/archive/**`. `scripts/archive/**` and `docs/superpowers/plans/**` are the only other paths that may retain superseded narratives; they are never live authority.
10. Inno live exe/task file names are generated-only: `SmartGuard.iss` consumes `generated/SmartGuard.Distribution.Constants.iss.inc` and `generated/SmartGuard.Distribution.Run.iss.inc`; handwritten `MyAppExeName`, `UninstallDisplayIcon` exe literals, or `[Run]` Engine/Cli/schtasks lines are forbidden.
11. Legacy SmartPowerPlan task cleanup names live only in `LegacyScheduledTaskCatalog`; live `scripts/` must not host manual cleanup entrypoints or duplicate name lists.

## Canonical Owners

| Concern | Canonical owner | Forbidden second owner |
| --- | --- | --- |
| shell owner | `InstallCommands -> DesktopShellRegistration` → one live start-menu shortcut `SmartGuard.lnk`; optional desktop `SmartGuard.lnk` | Tray runtime self-registration, `[Icons]`, dedicated settings/logs shortcuts, marker self-healing |
| xaml/icon true source | embedded `SmartGuard.Settings.xaml` and project-consumed `assets/branding/SmartGuard.ico` | loose `lib` assets, runtime file reads |
| distribution manifest | `DesktopDistributionManifest` + `InnoDistributionManifestWriter` generated includes | duplicated project/file/process lists in staging, installer, tests; handwritten Inno `[Run]` exe/task literals |
| logs entry | `SmartGuard.Settings.exe --page logs` | no dedicated shell shortcut; no `SmartGuard.LogViewer` product identity |
| legacy cleanup | `LegacyScheduledTaskCatalog` via `LegacyTaskCleaner` / `InstallCommands` | live `scripts/*.ps1` duplicate task names or manual unregister entrypoints |
| historical docs | `docs/archive/**` (+ `scripts/archive/**`, `docs/superpowers/plans/**` for snapshots/plans only) | top-level `docs/*.md` task-contract / migration / diagnosis narratives |

## PR Gate

- If a change introduces a second `canonical owner`, reject it.
- If a change reintroduces `fallback`, `marker`, loose runtime assets, or runtime shell repair, reject it.
- If a new distributable item requires touching more than the canonical manifest plus its generated consumers, reject it.
