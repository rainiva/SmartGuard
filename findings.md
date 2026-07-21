# Findings & Decisions

## Requirements

- 修复「日志无心跳、无计划切换」的用户可见故障
- 先写详细修复计划与验收标准（本文件 + `task_plan.md`）
- 实现须遵守项目 TDD / 架构门禁（AGENTS.md）
- 根因已诊断并反证，计划须对准真源而非 UI 表象

## Research Findings

### 症状（现场）

- 设置 → 日志：可见多次 `[INFO] SmartGuard Engine 启动`，无 `[HEART]`，无计划切换类日志
- UI 提示「状态可能过期」；空闲显示异常
- `SmartGuard Guardian` 计划任务 Last Result：`-532462766`（`0xE0434352` CLR 未处理异常）
- 无常驻 `SmartGuard.Engine` 进程；Tray/Settings 可运行

### 日志时间线

| 区间 | 现象 |
|------|------|
| ≤ 2026-07-17 归档 | 大量 `[HEART]` / 计划相关日志（功能曾正常） |
| 2026-07-17 22:13 重装后 | 起止几乎只有「Engine 启动」行 |
| 2026-07-18～21 | 归档 HeartOrSwitch=0；当前 log 同模式 |

### 崩溃栈（安装目录 Engine）

```
System.PlatformNotSupportedException: SystemEvents is not supported on this platform.
  at Microsoft.Win32.SystemEvents.add_PowerModeChanged(...)
  at SmartGuard.Engine.Infrastructure.PowerEventWakeListener..ctor(...)
  at SmartGuard.Engine.Worker.GuardianLoop.RunAsync(...)  // 写完启动日志之后
```

### DLL 布局反证

| 路径 | 大小 | 角色 |
|------|------|------|
| `D:\Apps\SmartGuard\bin\Microsoft.Win32.SystemEvents.dll` | 26376 | NuGet `lib/net8.0` **stub** |
| `...\bin\runtimes\win\lib\net8.0\Microsoft.Win32.SystemEvents.dll` | 59656 | Windows **真实现** |
| 仓库 `D:\Project\SmartGuard\bin\` | 同上双份布局 | 说明是 publish 产物问题，非单机损坏 |
| Engine 单独 `.../win-x64/` 输出根 | 59656 | RID 展平正确时可工作 |

**A/B：** 用真 DLL 覆盖根 stub → Engine 存活；还原 stub → 再次立刻崩溃。

### 代码路径

- 心跳：`GuardianIterationRunner` 在 `HeartbeatIntervalMin > 0` 且间隔到达时 `writeLog(..., LogLevel.Heart, ...)`
- 计划切换日志：同文件 `计划切换(切前同步)...`
- 配置现场：`HeartbeatIntervalMin: 10`（未关闭）
- `status.json` 停在 `2026-07-17T14:52:36` → 与 stale UI 一致

### 发布链路疑点

- `DesktopSuitePublisher`：多项目依次 `dotnet publish -r win-x64 --self-contained false -o <root>/bin`
- Engine csproj 显式 `PackageReference Microsoft.Win32.SystemEvents 9.0.0`（lib 为 stub）
- `StagingLayoutValidator` 只检查 exe/redist 等路径存在，**不校验** SystemEvents 是否为 stub
- Tray/Settings 使用 WindowsForms/WPF；与 Engine 共享同一 `bin` 输出，存在后写覆盖共享依赖的风险

## Technical Decisions

| Decision | Rationale |
|----------|-----------|
| 计划双轨：publish 门禁/展平 + Listener 降级 | 治本 + 防炸 |
| 默认方案 A（publish 后用 RID 真 DLL 覆盖根 stub） | 已反证根因，最小 GREEN |
| 验收不强制等 10 分钟心跳 | 用短间隔或 status/非启动日志证明主循环 |
| 本轮不强制打 GitHub Release | 先门禁与行为；发版另切片 |
| 完成定义 = B（仓库绿 + 本机已装恢复） | grill 确认；事故验收对准 `D:\Apps\SmartGuard` |
| 本机恢复 = 同步整份已门禁 `bin\` 后拉起任务 | grill 选 2；不用单 DLL 热修、本轮不重打 installer |
| 运行时兜底 = Y-lite | grill 确认；构造失败则 WARN+继续，不做完整工厂 |
| 治本 = 方案 A（Publish 后覆盖 stub + 门禁） | grill 确认；B/C 不进本切片 |
| 同步前停进程 = S1 | grill 确认；全停再拷，再拉 Guardian/按需 Tray |
| 门禁判定 = H（SHA256 对齐 runtimes/win） | grill 确认；不用体积/反射作本切片门禁 |
| 本机验收 = V1+V3 | grill 确认；status 刷新 + 短心跳 HEART 抽检 |
| 展平范围 = F1（仅 SystemEvents） | grill 确认；F2/F3 不进本切片 |
| 架构契约 = D1（必更新 ARCHITECTURE-CONTRACT） | grill 确认；与发布单源门禁同级登记 |
| V3 配置 = C1（临时改 1，验完改回） | grill 确认；不留下 HeartbeatIntervalMin=1 |
| Phase 2C = P0（不进 DoD） | grill 确认；Publish 末尾 F1 + H 即可 |
| 代码落点 = L1（Configuration.Build 共用 Ensure/Validate） | grill 确认 |
| S1 停机 = K2（/End → StopProcesses → 拷 → /Run，不 Disable） | grill 确认 |
| 同步后 = T1（自动开 Tray，不开 Settings） | grill 确认 |
| Y-lite 降级 = W1（WARN + 继续，接受轮询延迟） | grill 确认 |

## Issues Encountered

| Issue | Resolution |
|-------|------------|
| 误判为「UI 过滤 / 心跳关闭」 | 用磁盘计数与 config 反证排除 |
| 换 DLL 时文件锁定导致还原失败 | 先停进程再拷贝；最终 stub 状态已恢复并可复现崩溃 |

## Resources

- `src/SmartGuard.Engine/Worker/GuardianLoop.cs` — 启动日志后构造 Listener
- `src/SmartGuard.Engine/Infrastructure/PowerEventWakeListener.cs` — `SystemEvents.PowerModeChanged`
- `src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs` — HEART / 计划切换写入
- `src/SmartGuard.Configuration/Build/DesktopSuitePublisher.cs` — 共享 `bin` publish
- `src/SmartGuard.Packaging/Staging/StagingLayoutValidator.cs` — staging 门禁钩子
- `src/SmartGuard.Engine/SmartGuard.Engine.csproj` — SystemEvents 包引用
- 现场：`D:\Apps\SmartGuard\`

## Visual/Browser Findings

- 用户截图：日志页选中「日志」，标签含 INFO/WARN/ERROR/HEART；当前可见多为 Engine 启动 INFO；底部「状态可能过期」；刷新时间晚于最后一条日志数小时

---

*Update after every 2 view/browser/search operations*
