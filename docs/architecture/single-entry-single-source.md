# 单入口单真源规则

这是 SmartGuard 的短治理入口。详细矩阵、历史轮次和测试索引以 [ARCHITECTURE-CONTRACT.md](../ARCHITECTURE-CONTRACT.md) 为准；这里专门声明当前不可回退的硬规则。

## Core Rules

1. Every concern must name exactly one `canonical owner`.
2. Any `fallback`, `marker`, runtime self-healing, compatibility stub, or duplicated resolver counts as a second owner and is forbidden.
3. User-visible shell entrypoints are installer-owned only: `InstallCommands -> DesktopShellRegistration`.
4. Settings layout is embedded-only: runtime must not read `installRoot\lib\SmartGuard.Settings.xaml`.
5. Runtime branding is embedded-only: `assets/branding/SmartGuard.ico` is the only build-time icon source, and runtime must not read `installRoot\lib\SmartGuard.ico`.
6. Distribution topology is manifest-owned only: payload, published projects, installer process list, and legacy cleanup must come from `DesktopDistributionManifest`.
7. Log entry is settings-only: `SmartGuard.Settings.exe --page logs` is the only live user entry; `SmartGuard.LogViewer.exe` must not return as a product artifact.
8. Shell damage or legacy residue must be repaired only by install/repair. Tray runtime and notifier runtime are not allowed to write shell state back.

## Canonical Owners

| Concern | Canonical owner | Forbidden second owner |
| --- | --- | --- |
| shell owner | `InstallCommands -> DesktopShellRegistration` | Tray runtime self-registration, `[Icons]`, marker self-healing |
| xaml/icon true source | embedded `SmartGuard.Settings.xaml` and project-consumed `assets/branding/SmartGuard.ico` | loose `lib` assets, runtime file reads |
| distribution manifest | `DesktopDistributionManifest` | duplicated project/file/process lists in staging, installer, tests |
| logs entry | `SmartGuard.Settings.exe --page logs` | `SmartGuard.LogViewer.exe`, redirect stub, second shortcut topology |

## PR Gate

- If a change introduces a second `canonical owner`, reject it.
- If a change reintroduces `fallback`, `marker`, loose runtime assets, or runtime shell repair, reject it.
- If a new distributable item requires touching more than the canonical manifest plus its generated consumers, reject it.
