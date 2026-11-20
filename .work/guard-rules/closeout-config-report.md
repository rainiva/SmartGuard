# 配置原子替换复核结果

2026-10-01；范围与影响分析见 [closeout-config-plan.md](closeout-config-plan.md)。

## 结论

实际缺口位于现有内部 `GuardConfigAtomicFileWriter`，不是 Repository 调用缺失：旧逻辑先删正式文件再 Move，发布失败时最后有效配置路径消失。已最小修改为既有文件 `File.Replace`、首次创建 `File.Move`，入口、JSON 与缓存失效流程未改变。

测试使用现有提交前钩子移除已完成的临时文件，旧实现的 Save 和手动期限 mutation 两条行为都在正式文件保留断言失败；修复后均通过并可成功重试。另使用真实 Windows 临时文件独占句柄触发实际 `File.Replace` 共享冲突，证明旧正式文件字节保持、可读取，释放句柄后可重试。初次创建及正常替换由既有用例与重试路径覆盖。

## 新鲜证据

- RED：exit 1，2 失败 / 0 通过 / 0 跳过，[closeout-config-red.trx](closeout-config-red.trx)。失败原因均为正式路径已不存在。
- 最小 GREEN：exit 0，3 通过 / 0 失败 / 0 跳过，[closeout-config-green.trx](closeout-config-green.trx)。
- 配置全套（含额外实际共享冲突用例）：exit 0，411 通过 / 0 失败 / 0 跳过，[closeout-config-suite.trx](closeout-config-suite.trx)。TRX outcome `Completed`、计数与命令退出一致。
- 本次源码、测试与计划已严格 UTF-8 字节解码，无 BOM；`git diff --check` exit 0。已有测试构建警告未扩展修复。
- Pester / 其他 .NET / Tray / Installer：由主代理最终全量门禁记录，本分支报告不冒充已运行。

## 横向扫描与边界

- `DiagnosticBundleExporter.cs:91–94` 覆盖导出 ZIP 仍使用 Delete+Move；属于本次范围外，只记录。
- 固定 `.tmp` 与跨进程读改写争用是既有并发问题，本修复不承诺无丢更新；未扩大为事务/锁框架。
- 失败临时文件允许保留在原有单个 `.tmp` 槽位，下次保存覆盖，不新增无限临时文件或重试。
- 不宣称断电持久性、完整 ACL 故障矩阵或所有系统失败码下的文件系统事务保证。

建议主代理同步架构契约：`GuardConfigRepository` 仍为唯一读写 Owner，writer 为内部提交实现；禁止先删正式文件，既有文件 Replace / 首次创建 Move；已验证临时源失效与共享冲突失败时旧正式配置保持、错误上抛。无新增公共 API 或持久化格式。
