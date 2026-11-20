# 状态发布失败有界调查

2026-10-01；仅调查，未改生产代码或现有测试。原全量失败保留，不以独立绿色结果覆盖。

## 已确认

- 原全量门禁 [summary](../test-results/run-tests-20261001T030849449Z-24877bc284ab43e59d6264270aa2becf.summary.json) exit 1；.NET 1739 通过 / 1 失败 / 0 跳过。Engine 的 `Idle_growth_is_visible_at_refresh_cadence` 第二次 Publish 在 `StatusPublisher.WritePayload:63` 的 `File.Move(temp, status, overwrite: true)` 抛 `UnauthorizedAccessException`。
- 有界独立检查：原用例 1/1 通过，相关类 8/8 通过，均 exit 0、TRX `Completed`：[single](closeout-status-single.trx)、[class](closeout-status-class.trx)。这是复现条件差异，不是原门禁转绿。
- 新建 isolated executable 调用真实生产 `StatusPublisher`，1000 次连续 meaningful-change 发布，**第 119 次（0 基序号）失败**，前 119 次成功；同样异常，HResult `0x80070005`，正式文件属性仅 `Archive`。探针一遇首次失败即停止，没有重试。[原始 receipt](closeout-status-probe/raw/receipt.json)；对应源字节备份 [Program-v1](closeout-status-probe/Program-v1.cs.txt)。
- 独立 10ms 间隔对照 1000 次均成功；间隔仅用于实验，不能作为生产修复或零失败承诺。[对照 receipt](closeout-status-probe/raw-delayed/receipt.json)。

## 真实文件属性与共享句柄对照

| 条件 | 实际 StatusPublisher/File.Move overwrite |
|---|---|
| 普通文件、Archive | 成功 |
| 目标 ReadOnly + Archive | UnauthorizedAccessException / 0x80070005；旧内容保持 |
| 目标打开读句柄，FileShare.Read | UnauthorizedAccessException / 0x80070005；旧内容保持 |
| 目标打开读句柄，FileShare.ReadWrite + Delete | UnauthorizedAccessException / 0x80070005；旧内容保持 |

同目录 `File.Replace` 独立对照：目标读句柄 FileShare.Read 时失败 `IOException / 0x80070020`；ReadWrite + Delete 时成功。因此不能只把 Move 换成 Replace 就宣称共享读写链已修复。

当前共享读取真源 `StatusJsonReader.TryRead` 使用 `File.ReadAllText`；Tray `StatusStore`、Settings `StatusReader` 都经过它。原失败用例本身第二次 Publish 前没有主动打开 reader；每例 GUID 临时目录隔离。因此**人工共享句柄实验确认了一个可复现的发布限制，但没有证明原失败就是 Tray/Settings 或本例 reader 导致**。

## 推测与暂不能确认

高速覆盖更容易暴露短暂目标占用/删除完成窗口；外部文件系统过滤器、索引或安全软件可能参与。但未捕获原失败时间点的打开句柄、ACL、过滤驱动或系统跟踪，不能归因为杀毒/环境噪声。实际 burst 除 publisher 外没有自建 reader，仍复现，说明不能把发布脆弱性排除在产品调查之外。

原门禁异常未包含 HResult 与文件属性，无法把它和 burst 的底层原因完全等同。未证明自然 15 秒周期或 60 秒刷新必然失败；未测长期频率。

## 建议

本轮最终门禁保持失败，不能通过重跑盖掉。下一步若继续修复，先为共享读句柄下发布写 RED，明确读写共同契约，再考虑 `StatusJsonReader` 允许共享删除、publisher 原子替换与旧内容保留；另保留 burst 失败以调查无自建 reader 的瞬时拒绝。禁止直接加延迟、吞异常或无限重试。

已确认产品发布在受控占用情况下存在限制；**原全量失败根因尚未确定**。只凭当前证据无法认定当前四项修复引入回归，也无法认定不是产品缺陷。

官方核对：[File.Move 文档](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-8.0) 将权限不足和不能获得独占目标访问列为 UnauthorizedAccessException 条件；[.NET 8 Windows 实现](https://raw.githubusercontent.com/dotnet/runtime/v8.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Windows.cs) 原生 Move 失败直接转换 Win32 错误，不提供重试。以上说明异常可能性，不是本次根因证据。

探针首次路径引用错误以及 sandbox build 无诊断失败均保留为工具执行限制；正确引用、禁用 build servers 的 unsandbox 调查 exit 0。exit 0 只表示 receipt 写入完成，receipt 内的 failure 不因此视为通过。未运行全量门禁，未触碰安装配置、电源计划、任务或其他 agent 修改。
