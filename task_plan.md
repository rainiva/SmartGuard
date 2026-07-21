# Task Plan: 修复 Engine SystemEvents stub 导致无心跳/无计划切换日志

## Goal

让安装/发布产物中的 `SmartGuard.Engine` 能稳定进入守护主循环，恢复 `[HEART]` 心跳日志与计划切换日志，并用门禁防止 stub `Microsoft.Win32.SystemEvents.dll` 再次进入交付物。

## Current Phase

Phase 5 complete — 验收通过（loop 结束）

## Root Cause（已反证，见 findings.md）

1. `GuardianLoop.RunAsync` 先写「Engine 启动」日志，再 `new PowerEventWakeListener()`。
2. 安装目录 `bin\Microsoft.Win32.SystemEvents.dll` 是 NuGet `lib/net8.0` **stub**（约 26KB），订阅 `PowerModeChanged` 抛 `PlatformNotSupportedException`。
3. 真实现位于 `bin\runtimes\win\lib\net8.0\`（约 59KB），但运行时优先加载应用目录 stub。
4. 进程在进入 `while` 主循环前崩溃 → 无心跳、无计划切换、`status.json` 过期 → UI「状态可能过期」。

## Phases

### Phase 0: 诊断冻结（已完成）

- [x] 现场复现 Engine 崩溃栈
- [x] stub/真 DLL A/B 互换反证
- [x] 排除「心跳关闭 / UI 过滤 / 功能未实现」
- [x] 将结论写入 `findings.md`
- **Status:** complete

### Phase 1: 修复计划与验收标准（本阶段）

- [x] 明确双轨修复：交付物真源 + 运行时兜底
- [x] 定义可度量验收标准（AC）
- [x] 定义 TDD RED 顺序与最小 GREEN 范围
- [x] grill-me 锁定关键决策（见 Decisions Made）
- [x] 用户最终确认锁定 Phase 1 → 进入 Phase 2 RED
- **Status:** complete

### Phase 2: RED — 失败测试先行（未见失败不得写生产实现）

按 AGENTS.md §1/§11，顺序如下：

#### 2A. 发布/staging 门禁（真源）

- [ ] 新增/扩展测试：发布或 staging 输出中，`bin/Microsoft.Win32.SystemEvents.dll` **不得**为 stub
  - **grill 已锁定判定 = H**：若存在 `runtimes/win/lib/net8.0/Microsoft.Win32.SystemEvents.dll`，则根目录同名文件 SHA256 必须一致；runtimes 侧缺失则失败
  - （不采用单独体积阈值 Z，不采用反射订阅 R 作为本切片门禁）
- [ ] 扩展 `StagingLayoutValidator`（或并列 validator）断言上述约束；`StageCommand` 失败时阻断打包
- [ ] 运行相关测试，**贴出 RED 失败输出**到 `progress.md`

#### 2B. Engine 运行时兜底（防御纵深）

- [ ] 新增单元测试：`PowerEventWakeListener` / 工厂在 `SystemEvents` 不可用时：
  - **不得**让未处理异常冲出 `GuardianLoop` 启动路径
  - 应降级为 no-op（或记录一条 WARN 后继续主循环）
- [ ] 运行相关测试，**贴出 RED 失败输出**

#### 2C. 多项目覆盖写回归

- **grill = P0：不进本切片 DoD。** 方案 A 在全部 publish 结束后执行 F1；门禁 H 验最终布局即可。
- （取消原「可选 2C」必做项）

- **Status:** complete

### Phase 3: GREEN — 最小实现

仅实现让当前 RED 变绿的最小 diff：

#### 3A. 交付物修复（优先，治本）

候选方案（实施时选影响面最小且可测的一条，并记入 Decisions）：

| 方案 | 做法 | 优点 | 风险 |
|------|------|------|------|
| A. Publish 后处理 | `DesktopSuitePublisher.Publish` 结束后，若存在 `runtimes/win/lib/net8.0/Microsoft.Win32.SystemEvents.dll`，覆盖到 `bin/` 根 | 改动小、对准现场根因 | 需覆盖所有共享依赖同类问题的扩展性 |
| B. 去掉裸 PackageReference | Engine 改依赖 WindowsDesktop 内置 SystemEvents，避免拉 NuGet stub | 从源头减少 stub | 需验证 Tray/Settings 同目录 publish 是否仍覆盖 |
| C. 分目录 publish 再合并 | 各项目独立输出再智能合并 RID 资产 | 最干净 | diff 大，超出最小 GREEN |

**默认建议：A + 门禁；B 作为 REFACTOR/跟进。** 运行时兜底见 3B，不替代 A。

> **grill 已锁定：方案 A。** B/C 不进本切片 GREEN。

#### 3B. 运行时兜底（治标，防再炸）

- [ ] `PowerEventWakeListener` 构造/`TryCreate`：捕获 `PlatformNotSupportedException`（及文档所述上下文不支持异常），返回 disabled listener
- [ ] 启动路径打一条 `WARN`（写入日志），然后进入主循环
- [ ] **禁止**吞掉其它异常导致静默失败

- **Status:** complete

### Phase 4: REFACTOR（仅在全绿后）

- [ ] 抽取「RID 运行时资产展平/校验」为可单测的纯函数（若 Phase 3 内联过脏）
- [ ] 评估是否移除 Engine 对 `Microsoft.Win32.SystemEvents` 包引用（跟进，非本切片必做）
- [ ] **grill = D1（必做）**：在 `docs/ARCHITECTURE-CONTRACT.md` 登记 SystemEvents RID 对齐/禁 stub，并挂对应门禁测试
- **Status:** complete

### Phase 5: 验证与交付

- [ ] 跑受影响 xUnit / Packaging 测试，贴通过输出
- [ ] 跑 `Run-Tests.ps1`（或至少 Architecture + Packaging + Engine 相关子集，按仓库惯例全量优先）
- [ ] 本地：对临时目录 publish → 断言根 DLL 非 stub → 启动 Engine ≥ 若干秒不退出
- [ ] （可选）现场热修验证：覆盖安装目录 stub 后，日志出现启动后的状态/心跳（心跳间隔默认 10 分钟，可用缩短配置加速）
- [ ] 用户确认后提交（仅在用户要求 commit 时）
- **Status:** complete

## Acceptance Criteria（验收标准）

### AC-1 根因闭合（阻断项）

| ID | 标准 | 验证方法 |
|----|------|----------|
| AC-1.1 | 新 publish 的 `bin/Microsoft.Win32.SystemEvents.dll` 为 Windows 真实现，而非 NuGet `lib` stub | 文件哈希/大小门禁测试；手工对比 stub 26376 vs 真 ~59656 |
| AC-1.2 | staging/`PayloadCopier` 复制后的安装布局同样满足 AC-1.1 | `StagingLayoutValidator`（或等价）失败即阻断 `stage`/`installer` |
| AC-1.3 | 在「根目录仍是 stub」的模拟环境下，Engine **仍能**进入主循环（兜底生效） | 单元/集成：stub 旁路 + 进程存活或循环被调用 |

### AC-2 行为恢复（用户可感知）

| ID | 标准 | 验证方法 |
|----|------|----------|
| AC-2.1 | Engine 启动后进程保持运行（计划任务 Last Result 非 `0xE0434352`） | 启动后 `Get-Process SmartGuard.Engine`；或 Wait 10s 未退出 |
| AC-2.2 | **grill = V1+V3**：(V1) `status.json` 的 `timestamp`/`enginePid` 启动后刷新；(V3) 临时 `HeartbeatIntervalMin=1` 抽检出现 `[HEART]`，验完可改回 | 对比 status；改配置→等 ≥1 分钟→搜日志 HEART |
| AC-2.3 | （并入 V1）运行期间 status 持续新鲜 | 与 AC-2.2 V1 同证 |
| AC-2.4 | 设置页日志不再长期「状态可能过期」（status 年龄 &lt; 90s） | status 新鲜即可；UI 抽检可选 |

### AC-3 回归与质量门禁（阻断项）

| ID | 标准 | 验证方法 |
|----|------|----------|
| AC-3.1 | 先 RED 后 GREEN；声称完成前贴出测试通过输出 | `progress.md` 保留 RED/GREEN 原文 |
| AC-3.2 | 相关自动化测试全绿；不得靠删断言/Skip 让架构门禁变绿 | `dotnet test` / `Run-Tests.ps1` |
| AC-3.3 | 不引入第二套路径解析/停止引擎逻辑等架构违规 | 对照 AGENTS.md §8 |
| AC-3.4 | GREEN diff 不超过「让当前 RED 通过」的最小范围；额外清理进 REFACTOR | code review |

### AC-4 非目标（本轮不做，避免范围膨胀）

- 不重做日志 UI / 过滤标签
- 不调整心跳默认间隔产品策略（测试可临时改配置）
- 不要求本轮必须打新安装包发布到 GitHub（可另开发布切片）
- 不强制本轮 GitHub Release；但 **必须** 使 `D:\Apps\SmartGuard` 已装实例恢复（完成定义 B，grill 已确认）

## Key Questions

1. 治本优先用 Publish 后处理（方案 A）还是去掉 PackageReference（方案 B）？ → **默认 A + 门禁，B 跟进**
2. 运行时兜底是否必须？ → **必须**（防御纵深；已安装坏包用户升级前也可受益于新二进制）
3. 心跳验收是否必须等满 10 分钟？ → **否**；验收可用临时 `HeartbeatIntervalMin=1` 或断言「主循环已写 status/非启动-only 日志」
4. 是否本轮打 installer？ → **否**（除非用户另要求）；先保证 publish/stage 门禁与 Engine 行为

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| 双轨：publish 展平/校验 + PowerEventWakeListener 降级 | 治本防止坏包出门；治标防止再因 SystemEvents 拖死守护 |
| 默认 Publish 后处理覆盖 stub，而非大改分目录合并 | 对准已反证根因，GREEN 最小 |
| 门禁放进 StagingLayoutValidator 扩展点 | stage/installer 已有校验钩子，失败即阻断 |
| 严格 TDD：2A/2B RED 输出贴盘后再写实现 | AGENTS.md 阻断项 |
| 规划文件落在仓库根 `task_plan.md` 等 | planning-with-files 本次 init 为 legacy 根目录模式 |
| 完成定义 = B：仓库绿 + 本机已装实例恢复 | 用户故障在 `D:\Apps\SmartGuard`；仅修仓库等于下次重装才好 |
| 本机恢复方式 = 2：门禁通过后同步整份 `bin\` 再拉起 Guardian | 与仓库产物一致；避免只盖 DLL 留下旧 Engine/无兜底 |
| 运行时兜底 = Y-lite：try/catch + no-op/WARN，不做重型工厂 | 主路径靠真 DLL；兜底防漏网；RED 面可控 |
| 治本方案 = A：Publish 后用 RID 真 DLL 覆盖根 stub + 门禁 | grill 确认；对准反证根因，GREEN 最小 |
| 本机同步前停进程 = S1：停 Engine/Tray/Settings 再拷 bin，再拉起 Guardian（按需 Tray） | grill 确认；避免文件锁与混版 |
| 门禁判定 = H：根 DLL SHA256 必须等于 `runtimes/win/lib/net8.0/` 同名文件 | grill 确认；与方案 A 同构，单测稳定 |
| 本机行为验收 = V1+V3：status 刷新 + 短心跳抽检 `[HEART]` | grill 确认；不干等默认 10 分钟 |
| 展平范围 = F1：仅 `Microsoft.Win32.SystemEvents.dll` | grill 确认；不对全部 runtimes 资产做通用覆盖 |
| 架构契约 = D1：本切片必须更新 ARCHITECTURE-CONTRACT + 挂门禁测试 | grill 确认；发布共享行为变更须登记 |
| V3 配置 = C1：临时 HeartbeatIntervalMin=1，验完改回原值 | grill 确认；不永久改用户策略 |
| Phase 2C = P0：不进本切片 DoD | grill 确认；末尾 F1+门禁 H 足够 |
| 代码落点 = L1：Configuration.Build 纯函数 Ensure+Validate，Publisher/Staging 共用 | grill 确认；禁止 Packaging↔Publisher 双份逻辑或反向依赖 |
| S1 停机顺序 = K2：schtasks /End Guardian → EngineLifecycle.StopProcesses → 拷贝 → /Run；不 Disable | grill 确认；防拷贝中途被拉起 |
| 同步后拉起 = T1：自动 TrayLauncher.Start；不自动开 Settings | grill 确认 |
| Y-lite 降级 = W1：接受无即时电源唤醒；WARN + 主循环继续 | grill 确认；不另做备用监听 |

## Errors Encountered

| Error | Attempt | Resolution |
|-------|---------|------------|
| session-catchup.py 路径不存在 | 1 | 跳过 catchup；用本会话诊断结论写入 findings |
| init-session 后 Get-ChildItem .planning 触发 .pytest_cache 拒绝访问 | 1 | 忽略；文件已在仓库根创建 |
| A/B 换 DLL 后还原 stub 曾被进程锁住 | 1（诊断期） | 先停 Engine 再还原；最终已确认 stub 崩溃可复现 |

## Notes

- 更新阶段状态：pending → in_progress → complete
- 重大决策前重读本文件
- 所有错误记入本表，禁止原样重复失败动作
- 实现阶段遵守：中文文件 UTF-8 无 BOM；用户未要求则不自动 git commit（与 user_rule 对齐；AGENTS 提交规范在「用户要求提交」时再执行）
