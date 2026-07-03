# SmartGuard 任务栏图标异常偏小诊断报告

日期：2026-07-02

## 0. 深挖更新（2026-07-03）

在继续追到真实运行态之后，原报告里的“24px/32px 旧帧回退”结论只能解释一部分现象，不足以解释为什么修过一次后，用户看到的 **Settings/任务栏主按钮** 仍然明显偏小。

最新可复现、可反证的结论如下：

- **最终根因不是安装目录没更新，也不是快捷方式绑错图标。**
  - 工作区 `lib/SmartGuard.ico` 与安装目录 `D:\Apps\SmartGuard\lib\SmartGuard.ico` 的 WPF 解码结果完全一致；
  - 两边的 `64x64` 帧有效宽度都只有 `81.25%`，因此可以排除“修复没部署到安装态”这个解释。
- **真正命中的是真实 WPF/任务栏大图标链路，而不是 Pillow 的 `ico.getimage()` 取帧结果。**
  - `AppBrandIcon.LoadImageSource(...)` 原先会把 WPF 窗口图标限制在 `32px` 路径；
  - 即使改成直接 `BitmapFrame.Create(...)`，WPF 默认也会吃到 `16px` 首帧，而不是任务栏需要的 `64px` 大帧；
  - 这就是为什么“共享 `.ico` 明明修过，任务栏按钮还是小”的核心原因。
- **现有 Pester 图标测试存在量法盲区。**
  - `Tests/SmartGuard.Tests.ps1` 通过 Pillow `ico.ico.getimage((32,32)) / getimage((64,64))` 测出来的宽度，比 WPF/Win32 真实拿到的帧更大；
  - 例如旧资产在 Pillow 下 `64px` 可见宽度可到 `87.5%`，但 WPF `BitmapDecoder` 与窗口 `WM_GETICON / ICON_BIG` 实测只有 `81.25%`；
  - 所以之前出现了“Pester 绿了，但真实任务栏还是小”的假绿。
- **已完成的修复有两部分。**
  - `BrandIconLoader.LoadWpfImageSource(...)` 改为显式选择适合窗口大图标的 `64px` WPF 帧，不再走 `16px/32px` 默认路径；
  - `lib/SmartGuard.ico` 改为保留 `HEAD` 已验证的小尺寸家族（16–40），只放大真正影响任务栏主按钮的 48/64/128/256 大尺寸家族，从而避免把 16px 托盘语义一并打坏。

本次深挖还顺带反证了一个很重要的伪根因：

- **“只要继续折腾 `WM_SETICON`/原生句柄注入就能修好”不是根因。**
  - 在大尺寸资产本身仍旧偏小的时候，继续叠原生 `ICON_BIG` 句柄只会稳定地产生一个“正确设置了、但内容还是偏小”的大图标。

## 1. 问题描述

用户反馈：SmartGuard 在 Windows 任务栏中的应用图标明显比其他软件小，属于肉眼可见的异常，而不是轻微视觉差异。

本次排查目标不是直接修复，而是给出可验证的根因结论，并排除几个最容易误判的伪根因。

## 2. 结论

**根因已经定位：当前 `lib/SmartGuard.ico` 被回退到了一个“16px 可用、但 24px/32px/48px 有效绘制宽度明显偏窄”的旧版本。**

这会直接导致：

- 托盘 16px 图标基本正常；
- 任务栏主按钮、窗口标题栏、Explorer 等依赖 24px/32px/48px 帧的场景，视觉上明显偏小。

进一步说，`2026-07-02 21:38` 的提交 `1449cfb` 将 `lib/SmartGuard.ico` 从 `736027e` 中修过的版本回退到了旧版本；当前 `HEAD` 与 `1449cfb` 的像素测量结果完全一致。

`BrandIconLoader` 在同一次回退里也撤销了 WinForms 路径强制取 32px 帧的保护，这会放大 WinForms 表面的不确定性；但**即使不考虑这一点，仅当前 ICO 的 24px/32px 帧本身就足以解释截图里的“小图标”现象**。

## 3. 真实取图链

### 3.1 运行时统一走 `lib/SmartGuard.ico`

- `SmartGuardPaths.BrandIcon(root)` 将品牌图标真源固定到 `lib/SmartGuard.ico`。
- `BrandIconLoader.LoadWinFormsIcon(...)` / `LoadWpfImageSource(...)` 都先读这个路径。
- `TrayApplicationContext` 的 `NotifyIcon.Icon` 通过 `TrayIconLoader.Load(root)` 间接使用它。
- `SettingsWindowController` 在窗口创建后调用 `AppBrandIcon.ApplyTo(window, root)`，将同一个品牌图标设为 WPF 窗口图标。
- `LogViewerForm` 也直接把 `BrandIconLoader.LoadWinFormsIcon(...)` 的结果设为 `Form.Icon`。

### 3.2 嵌入式应用图标也是同一个真源

以下项目都把 `ApplicationIcon` 指向同一个 `..\..\lib\SmartGuard.ico`：

- `src/SmartGuard.Tray/SmartGuard.Tray.csproj`
- `src/SmartGuard.Settings/SmartGuard.Settings.csproj`
- `src/SmartGuard.LogViewer/SmartGuard.LogViewer.csproj`
- `src/SmartGuard.Cli/SmartGuard.Cli.csproj`
- `src/SmartGuard.Engine/SmartGuard.Engine.csproj`

### 3.3 安装器快捷方式也显式绑定同一个图标

`installer/SmartGuard.iss` 的 `[Icons]` 段为托盘、设置、日志、桌面快捷方式统一指定：

`IconFilename: "{app}\lib\SmartGuard.ico"`

因此这不是“某个入口忘了配图标”的问题，而是**多入口共同依赖的共享图标资源发生了回退**。

## 4. 像素级证据

对 `lib/SmartGuard.ico` 各帧做 alpha 包围盒测量，结果如下：

| 版本 | 16px 宽度占比 | 24px 宽度占比 | 32px 宽度占比 | 48px 宽度占比 |
|---|---:|---:|---:|---:|
| `HEAD` / `1449cfb` | 100.0% | 70.8% | 71.9% | 72.9% |
| `736027e` | 100.0% | 91.7% | 87.5% | 83.3% |

解释：

- 16px 帧在当前版本依然铺满，所以托盘区不一定明显异常；
- 24px/32px/48px 帧在当前版本只覆盖约 71% 的横向画布，任务栏主按钮自然会显得比其他应用小一截；
- `736027e` 正是那个把任务栏相关帧做宽的版本，它的测量值和提交说明 `fix(ui): tray icon sizes` 完全吻合。

## 5. Git 回溯证据

最近与该问题直接相关的提交链：

1. `736027e` `fix(ui): tray icon sizes, theme title bar, toast placement, dialog buttons`
2. `5fefaab` `fix(settings,icon): single-source autostart UI and taskbar icon frame`
3. `1449cfb` `revert(icon): restore pre-736027e ICO and BrandIconLoader`

关键点：

- `736027e` 把 `lib/SmartGuard.ico` 从 `42888` 字节替换成 `46597` 字节版本；
- `1449cfb` 又把它从 `46597` 字节回退回 `42888` 字节；
- 当前 `HEAD` 的图标测量结果与 `1449cfb` 完全一致；
- `5fefaab` 给 `BrandIconLoader` 增加了 `TaskbarIconFrameSize = 32`，而 `1449cfb` 同时把这层 WinForms 保护也撤掉了。

## 6. 当前 RED 证据

工作区里的 `Tests/SmartGuard.Tests.ps1` 已经新增了针对任务栏帧的守护断言：

- 24px 宽度占比必须 `>= 88.0`
- 32px 宽度占比必须 `>= 88.0`

实测执行 `Invoke-Pester -Path Tests/SmartGuard.Tests.ps1` 后，只有两项失败：

- `bundled SmartGuard.ico fills enough 24px taskbar canvas width`
  - 实际值：`70.8`
- `bundled SmartGuard.ico keeps a crisp 32px frame for taskbar and Explorer`
  - 实际值：`71.875`

其余 59 项通过。这个结果非常关键，因为它说明：

- 问题已经可以稳定复现；
- 失败只集中在任务栏相关帧；
- 16px 托盘相关检查没有一起崩掉，进一步支持“这是尺寸帧回退，不是整套图标损坏”的判断。

## 7. 已排除的伪根因

### 7.1 “是不是快捷方式图标没配对？”

不是。

安装器快捷方式显式使用 `"{app}\lib\SmartGuard.ico"`，并不是让系统去拿 `SmartGuard.Cli.exe` 的默认图标。

### 7.2 “是不是图标文件丢失或没打包进去？”

不是。

`lib/SmartGuard.ico` 在仓库中存在，大小为 `42888` 字节；运行时加载链和安装器快捷方式都稳定指向它。

### 7.3 “是不是所有尺寸的图标都坏了？”

不是。

16px 帧当前仍然是 `100%` 宽度占比，这解释了为什么托盘区不一定同样难看。

### 7.4 “是不是纯 Windows 图标缓存问题？”

不是根因。

这次结论不是靠截图或缓存猜测，而是直接对当前 on-disk `lib/SmartGuard.ico` 做帧级测量，并且测量结果还能和 Git 回退点一一对应。即使系统缓存清空，当前文件本身仍然会产出偏小的 24px/32px 结果。

## 8. 根因判定

### 主根因

`1449cfb` 将共享品牌资源 `lib/SmartGuard.ico` 回退到了一个旧的、任务栏帧有效宽度不足的版本，导致 SmartGuard 在任务栏主按钮、窗口图标和 Explorer 等场景中显示偏小。

### 次级放大因素

同一次回退还撤销了 `BrandIconLoader` 中 WinForms 路径强制取 32px 帧的保护，可能让 WinForms 表面的表现更依赖系统默认选帧；但这不是解释当前截图所必需的前提。

## 9. 后续修复建议（不在本报告执行）

建议单独开一个 fix slice，只做两件事：

1. 恢复 `736027e` 中经过放大修正的 `lib/SmartGuard.ico` 任务栏相关帧；
2. 保留 `5fefaab` 中 `BrandIconLoader` 的 `TaskbarIconFrameSize = 32` 保护，避免 WinForms 表面再次退回默认选帧行为。

完成后应以以下两类证据闭环：

- `Tests/SmartGuard.Tests.ps1` 新增的 24px/32px 宽度断言转绿；
- 至少一次真实 Windows 任务栏主按钮视觉验证，而不是只看托盘 16px。
