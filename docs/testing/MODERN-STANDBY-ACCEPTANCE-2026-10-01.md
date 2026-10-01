# 插电 Modern Standby 真实验收（2026-10-01）

结果：**BLOCKED，不能声明真实待机门禁通过。** 本轮无生产代码修改，未重跑上一轮已通过的自动门禁。全部现场证据保存在仓库 `.work/guard-rules/`，未以重试覆盖第一次操作。

## 操作与对象

用户选择插电合盖进入睡眠，再开盖移动鼠标，并确认已完成实际操作。本机 LAPTOP-17OSCVQK / Windows 11 22621，`powercfg /a` 支持 S0 低功耗待机，S3 不可用。候选来自 528b5a6、6e326f2、36a26e1 的已发布 bin，复制到 `.work/guard-rules/resume-desktop-root`，保存逐文件 SHA256；未部署到生产安装根。

本次候选启动为北京时间 **17:53:27**（09:53:27 UTC），Engine PID 39620、Tray PID 11448。生产任务定义保留，暂停原实例并运行独立默认配置；资源采样有界，五分钟原生健康采样周期未修改。运行基线不是默认 300/900 秒无人操作静置验收。

唯一验收入口为 `scripts/Invoke-PowerLifecycleAcceptance.ps1`，场景 LidMouseAC；包装会话只负责隔离、有限原始采样、在人工确认且后健康样本到达后发送 Read-Host 完成信号，以及 finally 恢复。

## 新鲜证据

| 事实 | 结果 |
|---|---|
| Windows Kernel-Power 506 | 18:00:26.1683178；LidOpenState=false |
| Windows Kernel-Power 507 | 18:03:09.8917363；SleepEntered=true、PowerStateAc=true |
| Engine Suspend/Resume 日志 | 两者均缺失 |
| 候选计划写入 | 18:00:30.3540299 PowerPlanSwitched，高性能；位于 506→507 的待机转换窗口内 |
| 前健康样本 | 17:58:27.9093477；droppedNormal/droppedHigh/failedBatches 均 0 |
| 后健康样本 | 18:06:06.0328530；三项计数仍为 0 |
| 候选状态 | 唤醒后继续刷新，enginePid 仍 39620，未观察到 Engine/Tray 退出 |
| 有限资源采样 | 42 份，17:53:28→18:06:55；Engine Private Bytes 峰值 24,018,944（约 22.91MiB），Tray 峰值 16,748,544；不作为长期稳定或整机能耗证明 |
| 规范验收脚本 | 退出 2，Blocked，checks=[]；错误 Cannot convert null to type System.DateTimeOffset |
| 包装会话 | 未通过；Restored=true，前后 Capture 快照完全相等 |

系统事件严格按 UTC 操作窗口及电源 Provider 查询，保存原始 XML，权威文件 `resume-desktop-system-events-v2.json`。第一次调查导出 EventID 节点类型误读为 0，且混入其他 Provider，保留但不用于判断；第一次宽泛 Get-WinEvent 查询也有资源类型读取错误，不作为缺少系统事件的证据。

正式回执 `resume-power-lifecycle.result.json`、`resume-desktop-session.result.json`，日志 `resume-desktop-root/SmartGuard.log`，transcript `resume-power-lifecycle.transcript.txt`。Windows 事件证明发生 S0 待机；仅凭 506→507 不能把所有阶段等同深度低功耗驻留，故不宣称已经测量实际睡眠中写入或整机能耗。缺少 Engine 观察事件使 no-suspend-plan-write 的规范窗口无法建立，该不变量仍未验收。

## 定位与下一步

1. 验收脚本把查询结果包成数组后使用 `$null -ne $suspend` / `$null -ne $resume`。无匹配时是空数组，不是 null，随后转换缺失的 lifecycleObservedAtUtc 抛错，未产生明确的失败 check。这是确定的诊断缺口，应先行为 RED，修正后仍以原现场证明缺事件，不能重跑覆盖本次结果。
2. `PowerEventWakeListener` 当前仅订阅 `SystemEvents.PowerModeChanged`；订阅成功即 IsEnabled=true，活动源码无显式 DAM/Modern Standby 暂停恢复注册。本机 Windows 506/507 与缺少 Engine 事件形成真实复现。
3. [微软 DAM 文档](https://learn.microsoft.com/en-us/windows/win32/w8cookbook/desktop-activity-moderator) 描述受暂停桌面进程须显式 opt-in，支持窗口注册或直接 callback；[PowerRegisterSuspendResumeNotification](https://learn.microsoft.com/en-us/windows/win32/api/powerbase/nf-powerbase-powerregistersuspendresumenotification) 规定注册、返回码和注销句柄。未注册是与现场吻合的根因线索，尚需验证本机插电 S0 的通知覆盖，不能把文档推断当成已修复的实验结论。
4. 后续扩展现有 listener，保持 PowerLifecycleStateMachine 为唯一生命周期 Owner；先设计回调取消/注销、重复 Resume、时间戳、资源所有权和失败健康信号，再 TDD。不得以显示关闭直接冒充睡眠，不新增轮询系统日志的第二生命周期入口。
5. 健康日志未携带 processId，旧脚本将缺失值转换成 0 比较会掩盖缺证据；本轮另有进程与状态原始采样支撑存活判断，该严格同进程门禁缺口单独记录，不顺手修改。

按项目停止线，当前回到通知覆盖调查与设计阶段。本轮不继续电池、锁屏或 300/900 秒无人静置来稀释这个阻塞；这些仍 NOT RUN。罕见 Win32 替换错误注入也仍 NOT RUN。

## 恢复与已有门禁

18:06:58 恢复完成：原任务定义、启用态和 Guardian/Tray 运行态恢复，配置 SHA256 仍为 9310CFD90225B1932AAFEA863A0DE150D7F02D7501DBFEE0887F187DD517DA93；原高性能 GUID 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c、亮度 30 已核对。原实例 Engine PID 33024、Tray PID 11112。用户 installer/version.txt 和原无关文件未修改。

上一轮完整门禁退出 0，Pester 132/0/0、.NET 1748/0/0、Tray Integration 2/0/0、Installer Integration 9/0/0（通过/失败/跳过）；原性能测试 3/0/0。该通过不替代本次真实待机 Blocked。本轮仅记录验收事实，文档检查 UTF-8 无 BOM、结构、链接及 git diff --check，不重新执行无变更的全量测试。
