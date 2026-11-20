# 从实际窗口添加程序与匹配验证执行记录

## 范围与验收

用户批准五项：窗口识别添加、搜索来源及刷新、多组件独立选择、添加后匹配验证、路径失效修正。个人项目采用一次性操作，不建立软件家族库、进程树监控或目录授权。

入口仍是 SettingsForegroundCoordinator 的具体 exe 名单变更与既有防抖保存链。搜索只提供候选；窗口识别复用 ForegroundProgramReader 同一物理源码，Settings 不依赖 Engine 可执行项目。Engine 准入、配置 Schema、日志与运行状态格式不变。

## Change Impact Matrix

| 域 | 影响和约束 |
|---|---|
| Function | 识别按钮提供五秒切换时间，结果展示名称、exe、路径后确认；多组件逐项添加；验证未匹配可添加当前组件，旧路径不存在时可确认替换。 |
| Architecture | 前台 Win32 采集唯一实现以链接源码供 Settings 使用；设置交互仍由现有 Foreground Owner 拥有，搜索 Owner 支持显式刷新。 |
| Data / Compatibility | 不增持久字段；验证仅在本次设置会话有效，重新打开显示尚未验证；旧配置不自动改写。 |
| Performance / Resource | 搜索保留两秒、2048检查、512候选、20结果、单在途STA线程预算；识别单在途Task，无常驻采集；路径检查最多128项/合作两秒，仅本机路径且后台执行，名单载入/添加或显式刷新请求一次，不随状态刷新持续扫描。 |
| Concurrency / Time | 五秒延迟可取消；重复点击不叠加；关闭取消并丢弃晚到结果；检查快照只能更新仍在名单中的原条目。 |
| Reliability / Recovery | 无窗口、权限不足、切换竞态、查询异常明确提示；取消不改名单；确认前重核原条目，替换不会删除其他组件；保存失败沿用既有提示。 |
| Security | 不启动程序、不读标题、不扩张同目录/同厂商/子进程授权，不自动替换，不提权探测；拒绝UNC发现和探测。 |
| Observability | 来源与本次验证明确展示；路径不可访问/不存在不武断声称软件卸载；成功匹配不声称守护动作执行成功。 |
| Operations | 安装/版本/发布 N/A；此轮不构建新安装包。 |

## Critical Module Check

- 配置：GuardConfigRepository、SettingsSnapshotMapper、防抖保存链保持唯一入口；只确认具体路径后变更，不新增旁路写入。
- 时间/后台生命周期：SettingsForegroundCoordinator 拥有一次识别任务及取消；延迟使用可取消 Task.Delay，关闭停止任务且不更新窗口。既有状态读取仍单在途。
- 采集：沿用查询后复核 HWND/PID 和 finally 释放句柄；未知不猜测，不回退启动器；链接编译不产生第二套实现。

## 分步实施与验证

1. 搜索来源/刷新：先补 RED，保留单在途与关闭取消。
2. 窗口识别/验证/失效修正：先补确认、取消、重复、匹配/错配和旧路径修正行为测试，再接真实控件。
3. 契约门禁、相关回归、全量 Run-Tests.ps1、编码和 diff 检查；实际桌面路径未运行时明确 NOT RUN。

官方依据：[GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow) 可能返回空窗口；[QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew) 使用有限查询权限；[DispatcherTimer](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatchertimer) 不保证准时，必须在关闭时解除资源持有。因此不以 UI Tick 次数充当识别计时，不增加常驻采集 Timer。

## 证据

- 搜索 RED：`program-search-red.trx`，2项行为断言失败（缺刷新、来源说明）。GREEN：`program-search-green.trx`，12/12。
- 识别/UI RED：`window-identification-red.trx`，7项失败（缺识别交互及实际控件）；架构 RED：`window-architecture-red.trx`，1项缺共享源码接入。初次测试代码的编译错误已修正，不计入有效 RED。
- 资源 RED：`path-check-red.trx`，同一条目在短周期内被检查9次而非1次；修正为按需一次，`window-identification-green-v2.trx` 11/11。
- 重载 RED：`modal-reload-red.trx`，WPF Selector因record值相等保留旧选中对象，阻碍下一次验证。改为引用身份的UI条目并保留确认前后身份检查；路径去重仍归既有Owner。相关回归见 `window-settings-final.trx`。
- 最终相关回归：`window-settings-final.trx`，49通过、0失败、0跳过（包括真实WPF设置窗口构建及实际本机快捷方式/冻结图标发现）；架构接入目标门禁 `window-architecture-green.trx` 1/1。
- 128项实际本机exe路径检查成本探针：`.work/program-identification/performance-probe/` 使用已构建Settings程序集，不改生产入口、不开Timer、只读系统exe文件。500样本/50次预热，空批次模拟原先无路径检查的增量基线 Mean 0.0001904ms/P95 0.0002ms；满128项首次批次42.6511ms，预热后Mean 4.7693ms/P95 7.2532ms/Max 13.4759ms。此结果只证明新增本机路径检查的成本，不等于整应用冷启动、真实桌面识别或慢磁盘性能；两秒为合作预算，单个平台文件调用不可强制中断。
- 专业复核覆盖：确认取消不写名单、具体组件不扩张、128项上限与重复合并、未知/UNC/相对路径拒绝、延迟关闭取消、重复单在途、重载及模态确认竞态、失效路径提醒和恢复不误报验证、快捷方式来源明确。已有保存链/Engine规则不改变。
- 不以WPF窗口构建、注入测试或集成门禁替代实际桌面验收：WPS四组件物理切换、5秒识别和确认、实际DPI/高对比度/屏幕阅读器为 NOT RUN；当前工具会话查询到WPS进程但窗口句柄为0，不能据此声称程序没有窗口或识别不支持WPS。
- 新安装包、版本升级、正式安装为 NOT RUN（本轮未要求）；源码交付后旧1.6.2安装包不自动包含这轮改动。
- 全量门禁：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1`，2026-10-05结束，进程退出码0；汇总 `.work/test-results/run-tests-20261005T030718995Z-28f34bcc53a14ae29883f3f0f05f19bd.summary.json` 的 Result=Passed、ExitCode=0。

| 层 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| Pester | 156 | 0 | 0 |
| .NET | 1938 | 0 | 0 |
| Tray Integration | 2 | 0 | 0 |
| Installer Integration | 9 | 0 | 0 |

.NET分项目：Engine345、Configuration466、CLI1、Architecture341、Packaging15、Tray87、LogView69、Settings614，TRX前缀 `3d866901ff28495199b59ec52192dd0c`。正式桌面验收与发布的 NOT RUN 项保持如上，四层自动化通过不替代它们。

15个任务文件经字节级严格UTF-8解码、无BOM检查及 `git diff --check`；新增执行文档本地链接存在，XAML已实际构建并通过窗口级测试。旧的无关未跟踪产物保留，不提交性能探针或测试结果目录。
