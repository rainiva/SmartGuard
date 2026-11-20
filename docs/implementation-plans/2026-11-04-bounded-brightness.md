# 自动切换条件与前台状态延迟排查

## 证据与根因

- 现场版本 1.6.3，Engine、Tray、Settings 同属会话 15；计划任务使用 Interactive，设置根目录为 `D:\Apps\SmartGuard`。
- 现场日志中 Engine 19:51 启动，20:11、20:31、20:51 才完成完整评估，前台轻路径仍有事件；配置中检查间隔为 15 秒。不是单凭 UI 推定配置未保存。
- `BrightnessService.GetBrightness` 在每次完整评估内同步枚举 WMI，默认无枚举时限。同一现场原查询超过 10 秒未返回；设置 250ms 枚举超时后约 306ms 返回超时。
- 被破坏的不变量：可选亮度探测失败不得长期阻塞配置采用、前台事实更新及策略评估。已有策略测试通常注入 `readBrightness: () => -1`，未覆盖真实 WMI 枚举阻塞。

## 变更范围与影响矩阵

| 影响域 | 本次处理 |
| --- | --- |
| Function / Reliability | 亮度读写枚举超时后沿用不可用/尽力失败语义，守护继续运行；不改变切换条件、名单精确路径语义。 |
| Architecture / Data / Compatibility | 扩展既有 BrightnessService，不新增入口、配置项或持久化格式。 |
| Performance / Resource | 完整轮次每次亮度枚举预算 250ms；不新增 Task、线程、Timer、队列、缓存；释放结果集合和取得的 WMI 对象。恢复重试仍受既有次数约束。 |
| Concurrency & Time / Recovery | 半同步枚举 ReturnImmediately=true；Timeout 只限制枚举，不能声称限制 Connect、方法执行或所有 COM 调用。保留原写入调用；超时不重启 WMI、不终止系统进程。 |
| Security / Observability | 不提升应用权限、不读取窗口标题；沿用 brightness=-1 和已有亮度不可用日志/状态。 |
| Operations | 构建和隔离验证后才评估安装；现场版本与源码验证分别报告。 |

Critical Module Check：守护循环 Owner 为 GuardianLoop，亮度 Owner 为 BrightnessService。保持单轮串行、失败不授予守护资格、切换前配置/前台复核不变；不以超时包装任务留下无界后台工作。横向检查同类亮度写入枚举与方法调用。BatteryInfoProvider 的 WMI 补充属于独立路径，本次没有证据表明现场原生电量读取被它阻塞，不扩张修复。

## 验证

先运行真实 WMI 读取限时回归和架构门禁取得 RED，再补最小超时配置，运行前台/配置/状态链回归与 Run-Tests.ps1。记录现场基线、修改后耗时、全量 summary 和真实用户路径；未执行的安装/桌面验收明确标记。

官方依据：[ManagementOptions.Timeout](https://learn.microsoft.com/en-us/dotnet/api/system.management.managementoptions.timeout) 明确集合时限适用于枚举、不适用于 Connect；[.NET ManagementObject 源码](https://source.dot.net/System.Management/System/Management/ManagementObject.cs.html) 的同步 InvokeMethod 未消费 Timeout，因此不添加无效的方法超时选项，不宣称解决其他 COM 阻塞。

### 本轮结果

- RED：`brightness-service-red.trx` 的真实 WMI 读取在 3 秒截止失败；`brightness-architecture-red.trx` 因缺少枚举时限失败。均位于 `.work/test-results/`。
- GREEN：目标前台/配置间隔/状态链 80/80；最终 `brightness-final-policy.trx` 两项真实 WMI/条件重载链通过，`brightness-final-architecture.trx` 架构 342/342 通过。全量运行中的真实亮度读取约 0.79 秒、两轮配置/前台链约 0.83 秒；后续独立运行两项合计约 303ms，测量包含测试调度，不能等同裸 WMI 延迟。
- `Run-Tests.ps1`：退出 0；Pester 156/156、.NET 1946/1946、Tray Integration 2/2、Installer Integration 9/9；各层失败/跳过均为 0。[summary](../../.work/test-results/run-tests-20261104T130916695Z-85784776072441b4b3e3d2243ad2d16c.summary.json)。全量期间曾收窄无效的方法 Timeout 修改，最终另跑上述行为和完整架构测试，Release 再构建通过。
- 一次额外的无筛选 Engine 重跑与安装器集成同时进行，长时间未结束后取消；原因未确定，不计为通过。原全量 Engine 347/347 与最后独立两项回归分别保留证据。
- 本机通过既有安装器应用修复，退出 0；Engine 哈希与最终候选一致，配置前后 SHA-256 相同。原安装备份与安装验证结果位于 `.work/brightness-repair-20261104/`，未发布远程 Release、未改变版本号。
- 真实桌面：21:18:55 启动后约 2 秒首次完成评估；21:19:59 再次发布新状态。21:21:21 WPS 窗口进入前台，记录命中第 2 条规则，21:21:22 状态为 `automatic / foreground-matched`；返回设置窗口于 21:21:43 记录进入 180 秒退出观察。Engine、Tray、Settings 已恢复运行。
- 桌面临时把 5 分钟改为 6 分钟并提交的验证被自动审批拒绝，未持久化；未提交输入已恢复为 5，配置哈希仍与备份相同。改条件后的采用由隔离 Repository → Runner → Status 行为测试验证；真实桌面条件改写为 `NOT RUN`，不等同已经实测。
- 本次无 UI 代码变化，DPI/高对比度/减少动画无新增影响；实际亮度恢复、其他 COM 阻塞、长期运行/休眠唤醒未重新验收，不宣称全面解决硬件 WMI 故障。
