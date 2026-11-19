# 以 Windows 电源 API 替代 powercfg 的评估

日期：2026-11-19　性质：评估（spike），未修改生产代码　关联：[子进程清理闩锁设计](2026-11-19-process-cleanup-reopen.md)　后续：[迁移设计](2026-11-19-power-scheme-api-migration.md)

## 1. 结论

建议把 Engine 核心链路（计划读取、切换、亮度索引写入、计划名称）从 `powercfg.exe` 子进程迁移到 `PowrProf.dll` 电源 API，子进程隔离只保留给 WMI 亮度探测。

理由：

- 核心链路上“启动进程、读管道、终止、清理确认、闩锁”这一整类故障随之消失；2026-11-19 的停工完全发生在这一层。
- 当前计划读取实测 p50 0.11ms，对比 `powercfg /getactivescheme` 24ms，约 200 倍。
- 能顺带消除一个现存副作用：首次初始化用“向平衡计划写入亮度 50”来探测是否支持亮度项，改为只读即可。

不能解决的：缺少“核心功能停工多久要升级处理”的活性保障，仍需单独补；WMI 亮度仍走子进程，闩锁可证实重开（设计第 2 版）对 WMI 仍然需要，但范围缩小。

## 2. 官方文档要点

| API | 用途 | 文档要点 |
|-----|------|----------|
| [PowerGetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powergetactivescheme) | 读当前计划 | 第一参数保留须为 NULL；返回的 GUID 指针须 `LocalFree`；成功返回 0 |
| [PowerSetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme) | 切换计划 | 文档称“为当前用户设置活动方案”；成功返回 0；未说明权限要求 |
| [PowerWriteACValueIndex](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powerwriteacvalueindex) / DC | 写亮度索引、关闭自适应亮度 | 备注：对**活动方案**的修改在调用 `PowerSetActiveScheme` 之前不生效 |
| [PowerEnumerate](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerenumerate) | 列出方案 | `ACCESS_SCHEME`(16) 按索引循环；缓冲不足返回 `ERROR_MORE_DATA` |
| [PowerReadFriendlyName](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerreadfriendlyname) | 读方案名称 | 先以 NULL 缓冲取大小；返回 Unicode 字符串 |
| [PowerSettingRegisterNotification](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersettingregisternotification) | 设置变更通知 | `DEVICE_NOTIFY_CALLBACK`；注册后立即回调一次当前值 |

官方 [Power Setting GUIDs](https://learn.microsoft.com/en-us/windows/win32/power/power-setting-guids) 只登记了 `GUID_POWERSCHEME_PERSONALITY`（报告人格：高性能/节能/平衡三类），**没有**登记“活动方案 GUID 变化”通知。因此不能据此用通知替代轮询，本评估不依赖通知。

## 3. 本机只读实测（非提升会话，未写任何电源设置）

脚本：会话 scratchpad 中的 `power-api-spike.ps1`、`power-api-breakdown.ps1`（P/Invoke，只调用读取 API）。

| 项目 | API | powercfg |
|------|-----|----------|
| 当前计划 | `8c5e7fda…`，与 powercfg 一致 | `8c5e7fda…（高性能）` |
| 方案列表 | 平衡、自定义模式、高性能（与 `/list` 完全一致，均不含隐藏方案） | 同左 |
| 隐藏方案名称 | `PowerReadFriendlyName` 直接读到“节能”“卓越性能” | 需逐个 `/query` |
| 亮度索引读取 | `VIDEONORMALLEVEL` AC/DC=30，`ADAPTBRIGHT` AC/DC=0，返回码 0 | 无对应只读命令 |
| 当前计划读取耗时 | p50 0.11ms，p95 0.18ms，max 4.77ms（500 次） | p50 23.9ms，max 25.6ms（20 次） |
| 方案枚举耗时 | 580–820ms（5 次） | `/list` p50 711ms |
| 单个名称读取 | 0.4–2.5ms | — |

发现：**方案枚举本身就慢（约 0.6–0.8 秒），慢在电源服务内部，不在子进程**；`powercfg /list` 慢是同一原因。按 GUID 读名称只要 1ms 左右。Guardian 只需要已配置和内置方案的名称，可以完全不枚举，从而去掉目录加载的 2 秒预算与其失败路径；需要完整列表的 Settings 仍要枚举，但没有进程和管道。

## 4. 现有 powercfg 用法与替代

| 现有用法 | 位置 | 替代 | 备注 |
|----------|------|------|------|
| `/getactivescheme` | Guardian 计划读取、切换后验证、最终观测 | `PowerGetActiveScheme` | 实测一致 |
| `/setactive` | Guardian 切换；Tray `PowerPlanActivator`（无超时 `WaitForExit`） | `PowerSetActiveScheme` | 写入未实测，见第 6 节 |
| `/set{ac,dc}valueindex … VIDEONORMALLEVEL` | 切换前亮度同步、首次初始化 | `PowerWrite{AC,DC}ValueIndex` | 写入的是目标方案，随后切换，符合“待 SetActive 生效”的文档语义 |
| `/set{ac,dc}valueindex … ADAPTBRIGHT 0` | 首次初始化 | 同上 | |
| 能力探测：向平衡方案写入亮度 50 | `IsBrightnessSupported` | `PowerReadACValueIndex` 只读 | 消除探测的写入副作用 |
| `/list` + `/query` | 计划目录（Guardian、Settings） | Guardian：按 GUID `PowerReadFriendlyName`；Settings：`PowerEnumerate` + 名称 | |

## 5. 新的失败模型

电源 API 是同步调用、不可取消。如果电源服务本身卡住，调用线程会阻塞——但这时 `powercfg` 同样会卡（它最终也要访问同一服务），所以迁移不引入新的依赖，只去掉了进程、控制台、管道和终止这几层。

建议的调用方式：

- 由一个 Owner 串行执行 API 调用（单飞）；调用方带截止时间等待。
- 超时后标记“有调用仍在途”，在途期间拒绝新调用并记录。
- 在途调用返回即自动恢复——完成本身就是可观测的证据，不需要清理证明、终止或闩锁。
- 资源上限：至多一个阻塞线程；在途超过阈值时进入活性告警。

与现有子进程方案相比：没有“无法证明清理”这种状态，也就不存在本次那种永久停工的路径。

## 6. 未验证项（实施前必须补）

1. **写入行为**：`PowerSetActiveScheme`、`PowerWrite*ValueIndex` 在提升的 Engine（计划任务 HighestAvailable）下的成功率、返回码与组策略限制；本次评估未执行任何写入。
2. **与 powercfg 的等价性**：实测读取一致，但未确认 powercfg 内部即调用这些 API；需在切换、外部改动、电源滑块覆盖（此前现场出现过 `961cc777…` Better Battery overlay）等场景下对比两者读数。
3. **唤醒后行为**：休眠/唤醒后首次调用的耗时与失败码，需要长时间运行加唤醒的实测。
4. **写入测试隔离**：拟用 `PowerDuplicateScheme` 复制临时方案、测试结束 `PowerDeleteScheme` 删除，使自动化写入测试不碰活动方案；这两个 API 的文档尚未核对。

## 7. 对当前计划的影响与建议顺序

1. **暂缓**[闩锁可证实重开实施方案第 2 版](2026-11-19-process-cleanup-reopen-plan.md)中针对 powercfg 的部分（Task 5 及 Guardian 相关改动）。
2. 先做电源 API 迁移的设计：建立唯一的电源方案 Owner（Configuration 层，Engine/Settings/Tray 共用），按“读取 → 切换 → 亮度索引 → 名称/目录 → Settings/Tray”分步替换，每步先写失败测试，并补完第 6 节验证。
3. 迁移后，闩锁方案收缩为只覆盖 WMI 子进程（运行器的 EOF 证明与移交仍然适用）。
4. 独立补上活性保障：核心计划读取连续 N 分钟失败或在途时，升级状态并提示，必要时由生命周期 Owner 受控重启 Engine。
