# 守护规则最终收口计划（2026-10-01）

用户授权：复核并修复配置替换/时间戳，补桌面链，再运行最终门禁和既有性能预算。保留既有用户修改，不扩建框架。

## 已确认根因与影响

- 协调器将 TimeProvider 的原始计数与 TimeSpan.Ticks 混算。当前10MHz机器碰巧等价，其他频率会改变15秒周期及1/5秒节流。改用该TimeProvider的频率换算截止时间，并经GetElapsedTime计算持续时间；首次未执行不用巨大负数伪造时间戳。
- 配置正式文件删除后再移动临时文件的独立问题由配置子任务复现；只改变已有Writer替换原语，不改配置Schema或新增Owner。

| 影响域 | 本轮处理 |
|---|---|
| Function / Architecture | 保持PolicyEngine及现有协调器/Repository/Writer单源；周期和节流语义不变 |
| Data / Recovery | 替换失败保留旧正式配置；首次创建沿用现有路径；不扩大为并发更新框架 |
| Concurrency & Time | 注入时钟1000/10MHz/1GHz验证周期、轻重节流和失败冷却；不改人工期限格式 |
| Performance / Resource | 协调器每周期O(1)换算，固定时间戳/TimeSpan，不新增Timer、线程、缓存或队列；名单仍≤128项 |
| Reliability / Compatibility | 当前机器频率行为保持；非10MHz时钟得到相同持续时间；配置失败不删除最后有效文件 |
| Observability / Operations | 保存RED/GREEN、全量summary、桌面及性能证据；原任务/进程/配置/电源恢复并核对 |
| Security | 不新增权限、网络、敏感字段；桌面验收隔离根 |

Critical Module Check：时间Owner仍为GuardianCycleCoordinator与注入TimeProvider，原始计数只与同频计数比较；配置写入仍经GuardConfigRepository和既有Writer，完整临时文件完成后才能替换正式文件，失败保留有效旧文件。同步架构契约和门禁。未涉及UI改版/安装器变更。

## 验收顺序

1. 两类行为RED→最小GREEN→受影响业务回归。
2. 用户随后选择“先完成自动可执行项，人工环节待验收”。最新发布的隔离桌面先验证键盘添加删除及状态；默认300/900秒无输入、一小时自然到期、锁屏/真实休眠、DPI/高对比等仍保留待验收，不自动触发睡眠或修改系统时间。
3. 最终Run-Tests.ps1核对退出码及四层summary；既有Engine启动<5秒/私有内存<20MiB预算独立运行。任何缺证据项写NOT RUN/BLOCKED，不拿短时台架代替生产预算。

## 当前收口结果

两项指定缺口完成行为RED/GREEN和独立回归，键盘添加/删除已走真实设置窗口。最终门禁与原启动预算均失败；状态文件发布有界复现仍失败，具体占用来源未确认。按项目停止线保持调查状态，不靠重跑、放宽预算或添加延迟关闭缺口。详细结果及人工待验收清单见 [closeout-report.md](closeout-report.md)。
