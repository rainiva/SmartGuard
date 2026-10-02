# 对抗工具A1实施记录（2026-10-02）

规范真源：[实施计划](../../tasks/plan.md)。本轮只实施A1结果契约与项目骨架，不执行攻击；A2–A5及Checkpoint A未完成，不签发BootstrapVerified或Calibrated，不接CI发布门禁。

## 实现前影响分析

Function：新增开发工具模型与不可执行的观察入口；未实现的请求层必须Blocked。Architecture：唯一入口AdversaryCommand.RunAsync；结果归约仅为父验证器未来消费的纯模型，执行器不能授予Passed。Data：新增开发期request/observation契约，不触及产品格式或用户配置。Performance/Resource：请求最多4层、2040case预算、64KiB请求文件；没有队列、线程、子进程或周期任务。Concurrency/Time：支持入口取消，预算终止独立退出41。Reliability/Recovery：缺层、重复层、0case、未知tier/状态、校准缺失全部拒绝；不产生待恢复副作用。Compatibility：产品无引用，不改现有运行入口。Security：catalog只能缩小代码登记的tier和case上限，无root、命令或提权授权字段；wrapper尚无可信broker，拒绝执行。Observability：未请求NotRun、已请求Blocked和模型错误区分。Operations：正式bundle/broker留给A4，无部署。

Critical Module Check：新开发工具入口Owner为AdversaryCommand，效果权限Owner为EffectPolicy，状态/退出码纯模型Owner为ParentResultModel；不能读取产品配置、启动攻击、改任务或声称独立父验证完成。架构登记与测试须先RED。不会引入工具专属路径/日志/配置旁路；A2持久化与脱敏必须复用计划登记的生产能力。

## 验证

结果/权限模型RED为28失败4通过，`.work/test-results/adversary-a1-red.trx`；GREEN32通过。CLI有界解析与伪造字段拒绝RED9失败0通过，`adversary-a1-cli-red.trx`；架构登记RED1失败1通过，`adversary-a1-architecture-red.trx`。复核另补“0case不能确认或归类FlakyFinding”的RED2失败，`adversary-a1-empty-finding-red.trx`，随后最小修复。

最终目标套件43通过0失败0跳过，`adversary-a1-final-green-v2.trx`；架构目标2通过0失败0跳过，`adversary-a1-architecture-green.trx`。开发CLI及正式wrapper真实进程退出均为40，`.work/test-results/adversary-a1-smoke.json`；观察包含未执行的请求层，不包含Passed/TrustLevel/父退出码。CLI允许读取开发期request，不接受未登记字段、整数枚举、重复/未知层、空请求或超过64KiB文件；正式wrapper在可信broker缺失时不读取/执行请求。

NuGet还原提示NU1900（无法取得在线漏洞元数据），保留警告，不关闭audit绕过；使用仓库既有xUnit/Test SDK版本，没有引入FsCheck/Stryker或产品运行依赖。43项工具测试独立执行，依计划F1前不加入canonical门禁名单。

完整门禁`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1`于本地19:43:48完成，命令/summary退出码均0，Pester156/.NET1765/Tray2/Installer9全部0失败0跳过、无未运行层。summary为`.work/test-results/run-tests-20261002T113847444Z-59954cb5cef344439e9d23ed6c6445cf.summary.json`；包装`adversary-a1-gates.result.json`的Before/After原任务配置及运行态精确一致、Error为空。工具没有改电源/亮度/生产配置，没有部署；真实前台UI链仍因computer-use初始化失败BLOCKED，不能由门禁替代。

A1已完成上述骨架范围验证。A2–A5/Checkpoint A未完成，当前不能运行攻击、签发可信等级或据本工具模型宣布产品通过。现有未跟踪tasks/plan.md、tasks/todo.md及旧进度记录只精确更新本地状态，保留原未跟踪身份，不将存量规划内容整体纳入本次提交；任务的可提交实施证据由本记录承载。
