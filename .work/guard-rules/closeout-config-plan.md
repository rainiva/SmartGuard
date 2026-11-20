# 配置原子替换复核与修复

2026-10-01。风险：高影响（现有配置持久化真源的失败语义）。

已确认根因：`GuardConfigRepository` 已统一调用 `GuardConfigAtomicFileWriter`，但 writer 完成临时文件后先删除正式文件再移动。删除与移动之间中断，或移动失败，会丢失最后有效配置。修复 writer，不另建配置入口。

## Change Impact Matrix / Critical Module Check

- Function / Reliability / Recovery：替换前临时文件失效时，保存仍抛出原始错误，正式配置须保持旧内容；下一次正常保存可恢复。
- Architecture / Data / Compatibility：入口、Owner、JSON 格式与未知字段保留行为不变。既有配置用同目录 `File.Replace`，首次创建用 `File.Move`；不提供 Delete+Move 降级。
- Performance / Resource：每次变更仍只有一份完整 JSON 临时文件与一次提交；复杂度 O(JSON 字节数)，无新增线程、缓存、轮询、重试。固定 `.tmp` 仍有并发争用，跨进程读改写丢更新属既有范围外问题，不宣称本修复解决。
- Concurrency & Time：修复单次提交的旧版本可恢复性；不修改多写者事务语义。
- Security：保留目标权限合并的默认失败语义，不忽略 metadata 错误；不触碰安装配置。
- Observability：保存失败继续传给调用方；测试保留 exit 与 TRX。
- Operations：仅 isolated 临时目录验证；主代理执行最终全量门禁。其余影响 N/A。

不变量：配置唯一入口不变；临时 JSON 完整后提交；不先删除最后有效正式版本；提交失败能重试。证据先 RED（在提交前移除测试临时文件），再最小 GREEN，另覆盖正常替换、首次创建与失败重试。

官方依据：[File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace)、[File.Move](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-8.0)。本次不宣称断电持久性、跨文件系统原子性或跨进程无丢更新。
