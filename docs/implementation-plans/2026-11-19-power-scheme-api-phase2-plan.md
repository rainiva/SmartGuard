# 电源方案 API 迁移 — 第二阶段（Settings / Tray）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**状态：** 第 3 版草案，已按 2026-11-19 外部审查与 2026-11-20 复审修订（见文末两份修订记录），待用户批准后实施。设计见 [2026-11-19-power-scheme-api-migration.md](2026-11-19-power-scheme-api-migration.md) 第 2.2 节“阶段二”，第一阶段记录见同文档末尾。

**Goal:** Settings 计划目录与 Tray“一键高性能”经 `PowerSchemeService` 调用 Windows 电源 API；删除 powercfg 包装与文本解析；整个产品不再启动 `powercfg.exe`；Settings 窗口不在 UI 线程上加载计划目录，并如实区分“正在加载 / 无法读取列表 / 名称读取失败 / 未在本机找到”。

**Architecture:**
- **目录：** `PowerPlanCatalogProvider` 的 Guardian 与 Settings 两条加载路径收敛为同一个经 `PowerSchemeService` 的私有核心（列举 + 读名，共享总预算）。每次加载发布一个不可变的 `PowerPlanCatalogSnapshot`，包含状态、名称、已列举方案、确认不存在的隐藏方案。UI 只读快照，从不触发加载 IO。
- **Tray：** `PowerPlanActivator` 改调 `PowerSchemeService.SetActiveAsync`（2 秒截止）；失败时由守护程序按已写入的手动高性能期限接管。
- **清理：** `PowerCfgProcessRunner` 只保留 WMI 探测使用的 `RunProcess`/`RunProcessAsync`，架构门禁从 Engine 扩展到全部 `src`。

**Tech Stack:** .NET 8、C#、WPF（Settings）、WinForms（Tray）、xUnit 2.5.3、FluentAssertions 8。

**证据目录：** `.work/power-scheme-api-phase2/`。

---

## 背景与新发现（调研 2026-11-19）

| # | 事实 | 位置 | 影响 |
|---|------|------|------|
| 1 | Tray“一键高性能”启动 `powercfg /setactive` 后 `WaitForExit()` **无超时** | `src/SmartGuard.Configuration/PowerPlanActivator.cs` | powercfg 卡住时托盘线程池任务永不结束，菜单项保持禁用 |
| 2 | Settings 打开窗口时，`ApplyInitialValues → ApplyPlanCombosToUi` 在 **UI 线程**上调用 3 次 `PopulatePlanCombo` 与 1 次 `UpdatePlanMappingStatus`，每次都同步调用 `PowerPlanCatalogProvider.TryLoad()` | `SettingsPlanCatalogCoordinator.cs:81,187`；`SettingsPolicyCoordinator.cs:244`；`SettingsWindowController.cs:162-165` | 缓存为空时在 UI 线程执行 `powercfg /list` 与至多 3 次 `/query`，每次 15 秒超时；不完整目录不缓存，缺少节能方案的机器每次重新填充都会重复。违反 AGENTS §5 |
| 3 | 契约 S-05 写“Settings 只经可取消的 `LoadWithRetryAsync`”，`SettingsPlanCatalogArchitectureTests` 却**要求**协调器包含 `PowerPlanCatalogProvider.TryLoad()` | 契约 S-05；`SettingsPlanCatalogArchitectureTests.cs` | 契约与门禁矛盾，以契约为准修正门禁 |
| 4 | 异步加载完成后 `ApplyPlanCatalog(catalog)` 丢弃参数，再经同步 `TryLoad()` 重新取目录 | `SettingsPlanCatalogCoordinator.cs:114-119` | 同 #2 |
| 5 | 同步 `TryLoad`/`LoadWithRetry`/`EnrichWithHiddenSchemes` 与 `PowerPlanCatalogParser` 在生产中只剩 Settings 这两处调用 | 全仓 grep | 迁移 UI 后可整体删除 |
| 6 | `LoadWithRetryAsync` 在 `maxAttempts` 次循环后**再追加一次加载**（`PowerPlanCatalogProvider.cs:144-146`），目录持续不完整时每次调用加载 `maxAttempts + 1` 次 | 同左 | 本计划**保留**该语义（设计要求“对外签名与重试语义不变”），测试与预算按 `maxAttempts + 1` 计算 |
| 7 | `PowerPlanComboItemsBuilder` 与 `PowerPlanMappingValidator` 只能表达“名称存在 / 不存在” | 两文件 | 无法区分“名称读取失败”与“本机不存在”，Task 2 引入快照后按存在性区分 |

---

## 目录状态模型

快照状态：

| 快照状态 | 进入条件 | 名称 |
|----------|----------|------|
| `Loading` | 进程内尚无一次加载完成（初始值） | 空 |
| `Failed` | 一次 `LoadWithRetryAsync` 的全部尝试都**未能列举**方案，且此前没有 `Loaded` 快照 | 空 |
| `Loaded` | 某次尝试列举成功（名称可能不全） | 已读到的名称 |

单个方案的存在性 `PresenceOf(guid)`：

| 存在性 | 条件 | 下拉框文案 | 映射校验 |
|--------|------|------------|----------|
| `Loading` | 快照为 `Loading` | `{角色}（正在加载）` | 状态栏“正在加载电源计划...” |
| `ListUnavailable` | 快照为 `Failed` | `{角色}（无法读取计划列表）` | 状态栏“无法读取电源计划列表，可重新打开设置重试” |
| `Named` | 名称已读到 | 名称 | 正常 |
| `NameUnreadable` | 已列举但读名失败；或为隐藏的内置方案、读名失败或因预算耗尽未读 | `{角色}（名称读取失败）` | 不报“未找到” |
| `ConfirmedAbsent` | 列举成功，未列举，且非内置方案或内置方案读名返回 Win32 2 | `{角色}（未在本机找到）` | “{角色}计划未在本机找到” |

状态转换规则：

- 一次 `Loaded` 之后的失败加载**不覆盖**已有 `Loaded` 快照，保留最近一次真实结果。
- `Failed` 之后再次加载成功即变为 `Loaded`（“失败后恢复”）。
- 加载过程中快照保持上一次的值；UI 仅在加载回调与配置变更时重读。

---

## Change Impact Matrix

| 维度 | 影响 |
|------|------|
| Function | 下拉框与映射状态按上表区分五种存在性，不再把“未加载/读不到”说成“未找到”；Tray 切换失败不再无限等待 |
| Owner / 真源 | 目录真源仍是 `PowerPlanCatalogProvider`（M-04、S-05）；电源方案调用唯一入口 `PowerSchemeService`；不新增 Owner |
| Concurrency & Time | Settings 加载保留单槽 `AsyncLoadGate`。每次尝试的列举与读名共享 5 秒总预算；每次调用至多 `maxAttempts + 1` 次尝试与 `maxAttempts` 次 300ms 间隔，默认 `maxAttempts = 3` 时总上界约 21 秒，全部在后台。Tray 切换截止 2 秒 |
| Performance | UI 线程目录 IO 从“最坏 4×15 秒”降为 0；后台单次加载约 1.1–1.6 秒（第一阶段基线） |
| Failure | 见状态模型；Tray 失败时配置已写入手动高性能，日志说明由守护程序在下一轮切换 |
| Compatibility | `IPowerPlanActivator`、`HighPerformanceBoost` 由 public 改为 internal（调用方只有 Tray，已有 IVT）；删除 public 的 `TryLoad`、`LoadWithRetry`、`EnrichWithHiddenSchemes`、`PowerPlanCatalogParser`（仓内无生产调用方）；`PowerPlanComboItemsBuilder.Build(dictionary, …)` 与 `PowerPlanMappingValidator.Validate(config, dictionary)` 保留，新增 internal 快照重载 |
| Observability | Tray 失败写入主日志一行中文说明；Settings 加载不新增日志 |
| Resource | 无新线程、队列、定时器；快照为不可变对象，每次加载替换一次 |

---

## 文件结构

| 文件 | 动作 | 责任 |
|------|------|------|
| `src/SmartGuard.Configuration/PowrProfNative.cs` | 修改 | 列举达到上限时报告“列表被截断”，不报告为完整列表（Task 0.5） |
| `Tests/SmartGuard.Configuration.Tests/PowrProfNativeEnumerationTests.cs` | 新建 | 列举上限边界 |
| `src/SmartGuard.Configuration/PowerPlanCatalogSnapshot.cs` | 新建 | 快照、状态、存在性 |
| `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs` | 修改 | 共享加载核心与快照发布；删除同步与 powercfg 路径 |
| `src/SmartGuard.Configuration/PowerPlanMappingValidator.cs` | 修改 | 快照重载，只对 `ConfirmedAbsent` 报缺失 |
| `src/SmartGuard.Configuration/PowerPlanCatalogParser.cs` | 删除 | powercfg 文本解析 |
| `src/SmartGuard.Configuration/IPowerPlanActivator.cs`、`PowerPlanActivator.cs`、`HighPerformanceBoost.cs`、`ManualHighPerformanceMessages.cs` | 修改 | 经 `PowerSchemeService` 切换、返回状态、失败说明 |
| `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs` | 修改 | 删除 powercfg 包装与覆盖钩子 |
| `src/SmartGuard.Settings/SettingsPlanCatalogCoordinator.cs` | 修改 | UI 只读快照 |
| `src/SmartGuard.Settings/PowerPlanComboItemsBuilder.cs` | 修改 | 快照重载与存在性文案 |
| `Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs` | 修改 | Task 1–4 封锁旧 powercfg 入口，Task 5 随钩子删除 |
| `Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs` | 修改 | 新增状态用例；迁移旧同步与 powercfg 覆盖用例 |
| `Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogParserTests.cs` | 删除 | 随解析器删除 |
| `Tests/SmartGuard.Configuration.Tests/LiveCatalogProbeTests.cs` | 修改 | 改用 `LoadWithRetryAsync` |
| `Tests/SmartGuard.Configuration.Tests/PowerPlanActivatorTests.cs` | 新建 | 切换经执行器、有界返回 |
| `Tests/SmartGuard.Configuration.Tests/HighPerformanceBoostTests.cs` | 修改 | 失败路径 |
| `Tests/SmartGuard.Settings.Tests/FakePowrProfNative.cs`、`PowerSchemeTestScope.cs` | 新建 | 替身与作用域（测试程序集互不引用，第三份） |
| `Tests/SmartGuard.Settings.Tests/SettingsWindowControllerTests.cs`、`SettingsPlanCatalogCoordinatorTests.cs`、`PowerPlanComboItemsBuilderTests.cs` | 修改 | UI 线程不加载、存在性文案、失败与恢复 |
| `Tests/SmartGuard.Engine.Tests/PowerSchemeTestScope.cs` | 修改 | Task 5 删除 powercfg 封锁 |
| `Tests/SmartGuard.Architecture.Tests/SettingsPlanCatalogArchitectureTests.cs`、`PowerSchemeApiArchitectureTests.cs` | 修改 | 修正与 S-05 矛盾的断言；门禁扩展 |
| `docs/ARCHITECTURE-CONTRACT.md`、迁移设计 | 修改 | 删除登记例外、更新 S-05、记录第二阶段 |
| `.work/power-scheme-api-phase2/watch-powercfg.ps1` | 新建（证据目录，不提交） | 验收期间记录 powercfg 进程启动 |

**通用约束：**
- 同第一阶段：UTF-8 无 BOM；精确补丁；只暂存本任务文件；Conventional Commits，结尾 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。
- 每个 RED 前先 `dotnet build` 受影响测试项目并确认编译状态；RED 必须因行为缺口失败。
- 测试只读真实电源设置，真实写入由 `PowerWriteGuard` 拒绝。
- **修改任何 `.ps1` 后核对 `git ls-files --eol` 为 `w/crlf`，并用 Windows PowerShell 5.1 解析一次。**
  - 根因是编码，不是换行。本仓库脚本按规则为 UTF-8 无 BOM，5.1 读取无 BOM 脚本时按系统 ANSI 代码页（本机 GBK）解码（见微软 [about_Character_Encoding](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_character_encoding?view=powershell-5.1)）。含中文的字符串在 5.1 运行时得到的本来就不是原文，改成 CRLF 不能修正这一点。
  - CRLF 只起防护作用：误解码有时会吞掉中文之后的一个字节。行尾是 CRLF 时，被吞的是 `\r`，脚本仍能解析；是 LF 时，被吞的是换行，下一行代码会并入注释，导致解析失败。Edit 工具会写 LF，第一阶段收尾时出现过这种失败。
  - 不改为加 BOM（项目规则要求无 BOM）。本计划新增的脚本只用 ASCII。
- 受影响测试项目：Configuration、Engine、Settings、Tray、Architecture。

---

### Task 0: 基线

- [ ] **Step 1: 记录基线**

```powershell
New-Item -ItemType Directory -Force .work\power-scheme-api-phase2 | Out-Null
foreach ($p in 'Configuration','Engine','Settings','Tray','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task0-baseline-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

Expected: 全部通过，记录各项目通过/跳过数（Settings 含 WpfUi 用例，耗时约 2 分钟）。

---

### Task 0.5: 列举达到上限时不报告为完整列表

Task 1 把“列举成功但未列出的自定义方案”判为本机不存在。这个结论要求列举结果完整。当前 `PowrProfNative.EnumerateSchemes` 读满 256 项后直接返回成功，若本机方案超过 256 个，上限之外的方案会被误判为不存在。本任务保留上限，把截断报告为 `ERROR_MORE_DATA`（234）：调用方按列举失败处理，界面显示“无法读取计划列表”，不会得出“不存在”的结论。

取舍：不给结果增加“是否完整”的标志。超过 256 个方案的机器极少见，按失败降级（Guardian 名称与 Settings 列表都不可用，并记录错误码 234）比让接口、三份替身和第一阶段调用方都多一个字段更简单，而且同样不会产生错误的“不存在”结论。

**Files:**
- Modify: `src/SmartGuard.Configuration/PowrProfNative.cs`
- Create: `Tests/SmartGuard.Configuration.Tests/PowrProfNativeEnumerationTests.cs`

- [ ] **Step 1: 抽出可测的列举循环（行为不变）**

`PowrProfNative` 中加入常量 `private const uint ErrorMoreData = 234;`，并把 `EnumerateSchemes` 替换为：

```csharp
  public uint EnumerateSchemes(out Guid[] schemes) => EnumerateBounded(EnumerateAt, MaxSchemes, out schemes);

  private static (uint Rc, Guid Scheme) EnumerateAt(uint index)
  {
    var buffer = new byte[16];
    var size = (uint)buffer.Length;
    var rc = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref size);
    return (rc, rc == 0 ? new Guid(buffer) : Guid.Empty);
  }

  // Scaffold: same behaviour as before (stops silently at the cap).
  internal static uint EnumerateBounded(Func<uint, (uint Rc, Guid Scheme)> enumerateAt, uint max, out Guid[] schemes)
  {
    var found = new List<Guid>();
    schemes = [];
    for (uint index = 0; index < max; index++)
    {
      var (rc, scheme) = enumerateAt(index);
      if (rc == ErrorNoMoreItems) break;
      if (rc != 0) return rc;
      found.Add(scheme);
    }
    schemes = found.ToArray();
    return 0;
  }
```

- [ ] **Step 2: 写失败测试**

`Tests/SmartGuard.Configuration.Tests/PowrProfNativeEnumerationTests.cs`：

```csharp
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class PowrProfNativeEnumerationTests
{
  private const uint NoMoreItems = 259;

  private static Func<uint, (uint Rc, Guid Scheme)> ListOf(int count, uint failAt = uint.MaxValue, uint failCode = 0)
    => index => index == failAt ? (failCode, Guid.Empty)
      : index < count ? (0u, new Guid((int)index + 1, 0, 0, new byte[8]))
      : (NoMoreItems, Guid.Empty);

  [Fact]
  public void A_list_that_ends_at_the_cap_is_complete()
  {
    PowrProfNative.EnumerateBounded(ListOf(3), 3, out var schemes).Should().Be(0u);
    schemes.Should().HaveCount(3);
  }

  [Fact]
  public void A_list_longer_than_the_cap_is_reported_as_cut_never_as_complete()
  {
    var calls = 0;
    var source = ListOf(5);
    PowrProfNative.EnumerateBounded(index => { calls++; return source(index); }, 3, out var schemes)
      .Should().Be(234u, "a cut list must not let callers conclude that unlisted schemes are absent");
    schemes.Should().BeEmpty();
    calls.Should().Be(4, "one read past the cap is enough to know the list was cut");
  }

  [Fact]
  public void An_api_error_is_returned_as_is()
  {
    PowrProfNative.EnumerateBounded(ListOf(5, failAt: 1, failCode: 5), 3, out var schemes).Should().Be(5u);
    schemes.Should().BeEmpty();
  }
}
```

- [ ] **Step 3: RED**

```powershell
dotnet build Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore -v q; "build exit=$LASTEXITCODE"
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-build --filter FullyQualifiedName~PowrProfNativeEnumerationTests --logger "trx;LogFileName=task05-red.trx" --results-directory .work/power-scheme-api-phase2
```

Expected: 编译退出码 0。`A_list_longer_than_the_cap…` 失败：骨架读满 3 项后返回 0 和 3 个方案。另外两例通过，属保护性用例：第一例证明恰好等于上限的列表仍算完整，第三例证明错误码原样返回。

- [ ] **Step 4: 实现**

`EnumerateBounded` 替换为：

```csharp
  // Reads at most `max` schemes. A list is complete only when the API reports ERROR_NO_MORE_ITEMS; an item
  // past the cap means the list was cut, reported as ERROR_MORE_DATA so callers treat the list as unknown
  // instead of concluding that unlisted schemes are absent. At most max + 1 native calls.
  internal static uint EnumerateBounded(Func<uint, (uint Rc, Guid Scheme)> enumerateAt, uint max, out Guid[] schemes)
  {
    var found = new List<Guid>();
    schemes = [];
    for (uint index = 0; ; index++)
    {
      var (rc, scheme) = enumerateAt(index);
      if (rc == ErrorNoMoreItems)
      {
        schemes = found.ToArray();
        return 0;
      }
      if (rc != 0) return rc;
      if (index == max) return ErrorMoreData;
      found.Add(scheme);
    }
  }
```

- [ ] **Step 5: GREEN**

```powershell
foreach ($p in 'Configuration','Engine','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task05-green-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

Expected: 全部通过。`PowerSchemeServiceLiveTests`（`Category=Live`，只读）若在本机运行，列举仍成功（本机方案远少于 256 个）。

- [ ] **Step 6: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowrProfNative.cs Tests/SmartGuard.Configuration.Tests/PowrProfNativeEnumerationTests.cs
git commit -m "fix(config): report a scheme list cut at the cap as incomplete" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 1: 共享加载核心与目录快照

**Files:**
- Create: `src/SmartGuard.Configuration/PowerPlanCatalogSnapshot.cs`
- Modify: `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs`
- Modify: `Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs`、`Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs`

- [ ] **Step 1: Configuration 作用域封锁旧 powercfg 入口**

`PowerSchemeTestScope` 类内加入：

```csharp
  private static readonly PowerCfgCommandResult Blocked =
    new(false, false, null, "", "powercfg is blocked inside PowerSchemeTestScope") { FailureKind = "start-failed" };
```

`WithNative` 中 `UseExecutorForTests(...)` 之后加入：

```csharp
    // RED may still fall into the legacy powercfg catalog path; block it instead of spawning powercfg.exe.
    PowerCfgProcessRunner.RunOverrideForTests = (_, _) => Blocked;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(Blocked);
```

`finally` 整体替换为（只有确认无在途调用后才解除封锁；超时照常抛出，封锁留在原处，迟到的调用也到不了 powercfg）：

```csharp
    finally
    {
      // A blocked fake must return before the scope ends: the gate belongs to the in-flight call.
      var idle = false;
      try
      {
        await WaitUntilIdleAsync();
        idle = true;
      }
      finally
      {
        // Reset in this frame (not in a nested async method) so the change applies to this flow.
        PowerSchemeService.UseExecutorForTests(null);
        PowerPlanCatalogProvider.ClearSessionCacheForTests();
        if (idle)
        {
          PowerCfgProcessRunner.RunOverrideForTests = null;
          PowerCfgProcessRunner.RunAsyncOverrideForTests = null;
        }
      }
    }
```

- [ ] **Step 2: 骨架（不改变行为）**

`src/SmartGuard.Configuration/PowerPlanCatalogSnapshot.cs`：

```csharp
namespace SmartGuard.Configuration;

internal enum PowerPlanCatalogState { Loading, Failed, Loaded }

internal enum PowerPlanPresence { Loading, ListUnavailable, Named, NameUnreadable, ConfirmedAbsent }

/// <summary>Scaffold only; presence rules arrive with their tests.</summary>
internal sealed class PowerPlanCatalogSnapshot
{
  private static readonly IReadOnlyDictionary<Guid, string> NoNames = new Dictionary<Guid, string>();
  private static readonly IReadOnlySet<Guid> NoPlans = new HashSet<Guid>();

  internal static readonly PowerPlanCatalogSnapshot Loading = new(PowerPlanCatalogState.Loading, NoNames, NoPlans, NoPlans);
  internal static readonly PowerPlanCatalogSnapshot Failed = new(PowerPlanCatalogState.Failed, NoNames, NoPlans, NoPlans);

  internal PowerPlanCatalogSnapshot(PowerPlanCatalogState state, IReadOnlyDictionary<Guid, string> names,
    IReadOnlySet<Guid> listed, IReadOnlySet<Guid> confirmedAbsent)
  {
    State = state;
    Names = names;
    Listed = listed;
    ConfirmedAbsent = confirmedAbsent;
  }

  internal PowerPlanCatalogState State { get; }
  internal IReadOnlyDictionary<Guid, string> Names { get; }
  internal IReadOnlySet<Guid> Listed { get; }
  internal IReadOnlySet<Guid> ConfirmedAbsent { get; }

  internal PowerPlanPresence PresenceOf(Guid plan) => PowerPlanPresence.Loading;
}
```

`PowerPlanCatalogProvider` 中加入：

```csharp
  internal static bool IsKnownScheme(Guid plan) => KnownSchemeGuids.Contains(plan);

  /// <summary>UI-safe: never performs IO. Scaffold only.</summary>
  internal static PowerPlanCatalogSnapshot Snapshot => PowerPlanCatalogSnapshot.Loading;

  // Test barrier: completes once no load holds the load gate. Every Settings load attempt, including its
  // snapshot publish, runs inside the gate, so a completed barrier means every earlier load has published.
  internal static async Task WhenLoadsIdleForTests()
  {
    await AsyncLoadGate.WaitAsync().ConfigureAwait(false);
    AsyncLoadGate.Release();
  }
```

（`WhenLoadsIdleForTests` 只供测试等待收敛，不改变行为；Task 2 的窗口用例与 Settings 作用域依赖它。）

- [ ] **Step 3: 写失败测试**（追加到 `PowerPlanCatalogProviderTests` 类末尾）

```csharp
    private static readonly Guid Custom = Guid.Parse("56c8d9b8-d34d-4d19-96a4-c177cf5f4882");

    [Fact]
    public async Task LoadWithRetryAsync_reads_names_through_the_scheme_service()
    {
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid, Custom,
                PowerPlanCatalogProvider.HighPerformancePlanGuid, PowerPlanCatalogProvider.PowerSaverPlanGuid]),
            ReadName = guid => (0, guid == Custom ? "自定义模式" : guid.ToString("N")),
        };
        IReadOnlyDictionary<Guid, string> catalog = new Dictionary<Guid, string>();
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Loading);
            catalog = await PowerPlanCatalogProvider.LoadWithRetryAsync();
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Loaded);
            PowerPlanCatalogProvider.Snapshot.PresenceOf(Custom).Should().Be(PowerPlanPresence.Named);
        });
        catalog.Should().ContainKey(Custom).WhoseValue.Should().Be("自定义模式");
        native.Calls.Count(call => call == "enumerate").Should().Be(1, "a complete catalog needs no retry");
    }

    [Fact]
    public async Task Enumeration_failing_on_every_attempt_publishes_a_failed_snapshot()
    {
        var native = new FakePowrProfNative { Enumerate = () => (5, []) };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            (await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1)).Should().BeEmpty();
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Failed,
                "an exhausted load is not 'still loading'");
            PowerPlanCatalogProvider.Snapshot.PresenceOf(Custom).Should().Be(PowerPlanPresence.ListUnavailable);
        });
        native.Calls.Count(call => call == "enumerate").Should().Be(2, "one attempt plus the existing final reload");
    }

    [Fact]
    public async Task A_later_failure_keeps_the_last_loaded_snapshot_and_a_later_success_recovers()
    {
        var failing = false;
        var native = new FakePowrProfNative
        {
            Enumerate = () => failing ? (5u, []) : (0u, [PowerPlanCatalogProvider.BalancedPlanGuid, Custom]),
            // Power saver is genuinely absent, so the catalog stays incomplete and is never cached.
            ReadName = guid => guid == PowerPlanCatalogProvider.PowerSaverPlanGuid ? (2u, string.Empty) : (0u, "n"),
        };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            var loaded = PowerPlanCatalogProvider.Snapshot;
            loaded.State.Should().Be(PowerPlanCatalogState.Loaded);

            failing = true;
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            PowerPlanCatalogProvider.Snapshot.Should().BeSameAs(loaded, "a failed reload must not erase a real result");

            failing = false;
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Loaded);
            PowerPlanCatalogProvider.Snapshot.Should().NotBeSameAs(loaded);
        });
    }

    [Fact]
    public async Task Failed_then_successful_load_moves_from_failed_to_loaded()
    {
        var failing = true;
        var native = new FakePowrProfNative
        {
            Enumerate = () => failing ? (5u, []) : (0u, [PowerPlanCatalogProvider.BalancedPlanGuid]),
        };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Failed);
            failing = false;
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Loaded);
        });
    }

    [Fact]
    public async Task Presence_separates_unreadable_names_from_confirmed_absence()
    {
        var unknownCustom = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid, Custom]),
            ReadName = guid =>
                guid == Custom ? (5u, string.Empty)                                     // listed, name unreadable
                : guid == PowerPlanCatalogProvider.PowerSaverPlanGuid ? (2u, string.Empty) // hidden, confirmed absent
                : guid == PowerPlanCatalogProvider.HighPerformancePlanGuid ? (5u, string.Empty) // hidden, unknown
                : (0u, "平衡"),
        };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            var snapshot = PowerPlanCatalogProvider.Snapshot;
            snapshot.PresenceOf(PowerPlanCatalogProvider.BalancedPlanGuid).Should().Be(PowerPlanPresence.Named);
            snapshot.PresenceOf(Custom).Should().Be(PowerPlanPresence.NameUnreadable, "it exists; only its name failed");
            snapshot.PresenceOf(PowerPlanCatalogProvider.PowerSaverPlanGuid).Should().Be(PowerPlanPresence.ConfirmedAbsent);
            snapshot.PresenceOf(PowerPlanCatalogProvider.HighPerformancePlanGuid).Should().Be(PowerPlanPresence.NameUnreadable,
                "a hidden built-in scheme whose read failed is unknown, not absent");
            snapshot.PresenceOf(unknownCustom).Should().Be(PowerPlanPresence.ConfirmedAbsent,
                "a non-built-in scheme that enumeration did not list does not exist");
        });
    }

    [Fact]
    public async Task LoadWithRetryAsync_propagates_cancellation_through_the_scheme_service()
    {
        using var release = new ManualResetEventSlim();
        var native = new FakePowrProfNative { Enumerate = () => { release.Wait(TimeSpan.FromSeconds(10)); return (0, []); } };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            try
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    PowerPlanCatalogProvider.LoadWithRetryAsync(cancellationToken: cancellation.Token));
            }
            finally { release.Set(); }
        });
        native.Calls.Should().Contain("enumerate");
    }

    [Fact]
    public async Task LoadWithRetryAsync_serializes_concurrent_loads_that_cannot_be_cached()
    {
        // High performance and power saver are genuinely absent, so no load is cached and each caller
        // loads maxAttempts + 1 = 2 times. Cache reuse is covered by LoadWithRetryAsync_reuses_completed_catalog….
        var native = new FakePowrProfNative
        {
            Enumerate = () => { Thread.Sleep(50); return (0, [PowerPlanCatalogProvider.BalancedPlanGuid]); },
            ReadName = guid => guid == PowerPlanCatalogProvider.BalancedPlanGuid ? (0u, "平衡") : (2u, string.Empty),
        };
        IReadOnlyDictionary<Guid, string>[] results = [];
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            results = await Task.WhenAll(
                PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1),
                PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1));
        });
        // Without the load gate the second caller would meet the executor's single-flight gate, get
        // call-pending without reaching native code, and the count would fall below 4.
        native.Calls.Count(call => call == "enumerate").Should().Be(4);
        results.Should().AllSatisfy(catalog => catalog.Should().ContainKey(PowerPlanCatalogProvider.BalancedPlanGuid));
    }
```

- [ ] **Step 4: RED**

```powershell
foreach ($p in 'Configuration','Engine','Settings','Tray','Architecture') { dotnet build "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore -v q; "$p build exit=$LASTEXITCODE" }
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-build --filter FullyQualifiedName~PowerPlanCatalogProviderTests --logger "trx;LogFileName=task1-red.trx" --results-directory .work/power-scheme-api-phase2
```

Expected: 编译退出码均为 0。新用例中：

- 依赖快照状态或存在性的 5 例（`reads_names…`、`Enumeration_failing…`、`A_later_failure…`、`Failed_then_successful…`、`Presence…`）失败：骨架快照恒为 `Loading`；旧路径走 powercfg，在作用域内被封锁，替身未被调用。
- `serializes…` 失败：替身未被调用。
- `propagates_cancellation…` 以 `native.Calls` 断言失败。

旧用例照常通过。

- [ ] **Step 5: 实现**

(a) `PowerPlanCatalogSnapshot.PresenceOf` 替换为：

```csharp
  // Absence is a conclusion only after a successful enumeration, which is always a complete list (a list
  // cut at the cap is reported as a failure, Task 0.5): a non-built-in scheme that was not listed does not
  // exist; a hidden built-in scheme is absent only when its name read returned Win32 2.
  internal PowerPlanPresence PresenceOf(Guid plan) => State switch
  {
    PowerPlanCatalogState.Loading => PowerPlanPresence.Loading,
    PowerPlanCatalogState.Failed => PowerPlanPresence.ListUnavailable,
    _ when Names.ContainsKey(plan) => PowerPlanPresence.Named,
    _ when Listed.Contains(plan) => PowerPlanPresence.NameUnreadable,
    _ when ConfirmedAbsent.Contains(plan) || !PowerPlanCatalogProvider.IsKnownScheme(plan) => PowerPlanPresence.ConfirmedAbsent,
    _ => PowerPlanPresence.NameUnreadable,
  };
```

并把类注释改为 `Immutable, IO-free view of the latest catalog load; Settings reads only this.`。

(b) `PowerPlanCatalogProvider`：删除 Step 2 的 `Snapshot` 骨架，加入：

```csharp
  private static volatile PowerPlanCatalogSnapshot _snapshot = PowerPlanCatalogSnapshot.Loading;

  /// <summary>UI-safe: never performs IO. The latest load's result; loads run on background tasks.</summary>
  internal static PowerPlanCatalogSnapshot Snapshot => _snapshot;

  private sealed record CatalogLoad(bool Enumerated, IReadOnlyDictionary<Guid, string> Names,
    IReadOnlySet<Guid> Listed, IReadOnlySet<Guid> ConfirmedAbsent, bool Complete);
```

(c) `GuardianLoadBudget` 更名为 `LoadBudget`（两条路径共享，5 秒）。`TryLoadOnceAsync` 改为：

```csharp
  // Guardian-only entry (PWR-INV-SCHEME-API).
  internal static async Task<IReadOnlyDictionary<Guid, string>> TryLoadOnceAsync(CancellationToken cancellationToken,
    Action<PowerSchemeStatus>? observe = null, TimeSpan? budget = null)
  {
    if (_sessionCache is not null) return _sessionCache;
    if (LoadImplementationForTests is { } loader) return loader();
    var load = await LoadThroughSchemeServiceAsync(cancellationToken, observe, budget ?? LoadBudget).ConfigureAwait(false);
    Publish(load);
    return load.Names;
  }
```

(d) 新增共享核心、测试加载器适配与发布：

```csharp
  // One load through PowerSchemeService: enumeration and every name read share one total budget, so the
  // chain is bounded regardless of the scheme count.
  private static async Task<CatalogLoad> LoadThroughSchemeServiceAsync(
    CancellationToken cancellationToken, Action<PowerSchemeStatus>? observe, TimeSpan budget)
  {
    var names = new Dictionary<Guid, string>();
    var absent = new HashSet<Guid>();
    using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    total.CancelAfter(budget);
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    HashSet<Guid>? listed = null;
    var complete = false;
    try
    {
      var schemes = await PowerSchemeService.EnumerateSchemesAsync(total.Token).ConfigureAwait(false);
      observe?.Invoke(schemes.Status);
      if (!schemes.Succeeded) return new(false, names, new HashSet<Guid>(), absent, false);

      var enumerated = schemes.Value!.ToHashSet();
      listed = enumerated;
      complete = true;
      foreach (var guid in enumerated.Concat(KnownSchemeGuids.Where(known => !enumerated.Contains(known))))
      {
        var name = await PowerSchemeService.ReadNameAsync(guid, total.Token).ConfigureAwait(false);
        if (name.Succeeded)
        {
          if (!string.IsNullOrWhiteSpace(name.Value)) names[guid] = name.Value!;
          continue;
        }
        if (!enumerated.Contains(guid) && name.Status is { FailureKind: "api-error", Win32Error: 2 })
        {
          absent.Add(guid);
          continue;
        }
        complete = false;
        observe?.Invoke(name.Status);
      }
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      complete = false;
      observe?.Invoke(new PowerSchemeStatus("timed-out", null,
        (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds));
    }
    // Enumerated even when the budget ran out during name reads: unread schemes stay NameUnreadable.
    return new(listed is not null, names, listed ?? new HashSet<Guid>(), absent, complete);
  }

  // Test loaders return the machine's complete list; built-in schemes they omit count as confirmed absent.
  private static CatalogLoad FromTestLoader(IReadOnlyDictionary<Guid, string> catalog)
    => new(true, catalog, catalog.Keys.ToHashSet(), KnownSchemeGuids.Where(g => !catalog.ContainsKey(g)).ToHashSet(), true);

  private static void Publish(CatalogLoad load)
  {
    if (load.Enumerated)
      _snapshot = new PowerPlanCatalogSnapshot(PowerPlanCatalogState.Loaded, load.Names, load.Listed, load.ConfirmedAbsent);
    if (load.Complete && load.Names.Count > 0 && !HasMissingKnownSchemes(load.Names)) _sessionCache = load.Names;
  }

  private static void PublishFailureUnlessLoaded()
  {
    if (_snapshot.State != PowerPlanCatalogState.Loaded) _snapshot = PowerPlanCatalogSnapshot.Failed;
  }

  // One Settings attempt: same core as the guardian; a failing test loader counts as a failed enumeration.
  private static async Task<CatalogLoad> LoadAttemptAsync(CancellationToken cancellationToken)
  {
    if (_sessionCache is { } cached) return FromTestLoader(cached);
    CatalogLoad load;
    if (LoadImplementationForTests is { } loader)
    {
      try { load = FromTestLoader(await Task.Run(loader, cancellationToken).ConfigureAwait(false)); }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
      catch { load = new(false, new Dictionary<Guid, string>(), new HashSet<Guid>(), new HashSet<Guid>(), false); }
    }
    else
    {
      load = await LoadThroughSchemeServiceAsync(cancellationToken, null, LoadBudget).ConfigureAwait(false);
    }
    cancellationToken.ThrowIfCancellationRequested();
    Publish(load);
    return load;
  }
```

（`TryLoadOnceAsync` 对测试加载器的处理保持第一阶段原样：直接返回，不发布快照。Guardian 不读快照。）

(e) `LoadWithRetryAsync` 中 `AsyncLoadGate` 内的循环与收尾替换为（保留 `maxAttempts + 1` 语义）：

```csharp
      CatalogLoad? last = null;
      for (var attempt = 0; attempt < maxAttempts; attempt++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (attempt > 0)
          InvalidateSessionCache();
        last = await LoadAttemptAsync(cancellationToken).ConfigureAwait(false);
        if (last.Names.Count > 0 && !HasMissingKnownSchemes(last.Names))
          return last.Names;

        if (attempt < maxAttempts - 1)
          await Task.Delay(300, cancellationToken).ConfigureAwait(false);
      }

      // Existing semantics: one final reload after the attempts (maxAttempts + 1 loads per call).
      cancellationToken.ThrowIfCancellationRequested();
      InvalidateSessionCache();
      last = await LoadAttemptAsync(cancellationToken).ConfigureAwait(false);
      if (!last.Enumerated) PublishFailureUnlessLoaded();
      return last.Names;
```

(f) `ClearSessionCacheForTests` 增加 `_snapshot = PowerPlanCatalogSnapshot.Loading;`。

同步 `TryLoad`、`LoadWithRetry`、`LoadCore`、`TryLoadAsync`、`LoadCoreAsync` 等本任务**暂不删除**（Task 3 处理）。

- [ ] **Step 6: GREEN**

```powershell
foreach ($p in 'Configuration','Engine','Settings') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task1-green-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

Expected: 新用例全部通过。

旧用例的处理：
- **删除 2 个：** `LoadWithRetryAsync_propagates_cancellation_to_powercfg` 与 `LoadWithRetryAsync_serializes_concurrent_powercfg_loaders` 经 `RunAsyncOverrideForTests` 模拟 powercfg，新实现不再调用 powercfg，它们失败。分别由 Step 3 的 `propagates_cancellation…` 与 `serializes_concurrent_loads_that_cannot_be_cached` 取代；后者与被删用例一样覆盖“目录不可缓存时并发加载仍串行”。
- **保留：** `LoadWithRetryAsync_reuses_completed_catalog_for_concurrent_callers` 使用 `LoadImplementationForTests`，不经 powercfg，在新实现上应继续通过（首个加载发布完整目录并写入缓存，第二个调用复用）。它是“完整目录被复用”的覆盖，不删除。
- 提交说明列明删除的 2 个用例及替代关系。若还有其他用例失败，按 systematic-debugging 定位，不改断言迁就实现。

- [ ] **Step 7: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowerPlanCatalogSnapshot.cs src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs
git commit -m "feat(config): load settings plan catalog through the scheme service with a state snapshot" -m "Removes LoadWithRetryAsync_propagates_cancellation_to_powercfg and LoadWithRetryAsync_serializes_concurrent_powercfg_loaders; scheme-service equivalents replace them. LoadWithRetryAsync_reuses_completed_catalog_for_concurrent_callers is kept." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Settings UI 只读快照并区分存在性

**Files:**
- Modify: `src/SmartGuard.Settings/SettingsPlanCatalogCoordinator.cs:81,187`、`src/SmartGuard.Settings/PowerPlanComboItemsBuilder.cs`、`src/SmartGuard.Configuration/PowerPlanMappingValidator.cs`
- Create: `Tests/SmartGuard.Settings.Tests/FakePowrProfNative.cs`、`Tests/SmartGuard.Settings.Tests/PowerSchemeTestScope.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/SettingsWindowControllerTests.cs`、`SettingsPlanCatalogCoordinatorTests.cs`、`PowerPlanComboItemsBuilderTests.cs`、`Tests/SmartGuard.Architecture.Tests/SettingsPlanCatalogArchitectureTests.cs`

- [ ] **Step 1: Settings 测试替身与作用域**

`Tests/SmartGuard.Settings.Tests/FakePowrProfNative.cs`：与 `Tests/SmartGuard.Configuration.Tests/FakePowrProfNative.cs` 内容相同，仅命名空间改为 `SmartGuard.Settings.Tests`。

`Tests/SmartGuard.Settings.Tests/PowerSchemeTestScope.cs`：

```csharp
using System.Runtime.ExceptionServices;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

internal static class PowerSchemeTestScope
{
  private static readonly TimeSpan ConvergenceTimeout = TimeSpan.FromSeconds(10);

  private static readonly PowerCfgCommandResult Blocked =
    new(false, false, null, "", "powercfg is blocked inside PowerSchemeTestScope") { FailureKind = "start-failed" };

  // Synchronous on purpose: WPF tests run on the STA thread; loads started inside inherit the
  // AsyncLocal executor through the execution context. Blocking the STA thread here is safe: loads use
  // ConfigureAwait(false) and never need the dispatcher.
  // Before the global hooks are reset, every load must have published (the load gate is free) and no
  // native call may be in flight. Otherwise the test fails and the hooks stay in place, so a late load
  // can neither reach powercfg.exe nor publish into the next test's cleared cache.
  internal static void WithNativeSync(IPowrProfNative native, Action run)
  {
    PowerSchemeService.UseExecutorForTests(new PowerSchemeExecutor(native));
    PowerCfgProcessRunner.RunOverrideForTests = (_, _) => Blocked;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(Blocked);
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    ExceptionDispatchInfo? failure = null;
    try { run(); }
    catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }

    var converged = PowerPlanCatalogProvider.WhenLoadsIdleForTests().Wait(ConvergenceTimeout)
      && SpinWait.SpinUntil(() => !PowerSchemeService.IsCallInFlight, ConvergenceTimeout);
    PowerSchemeService.UseExecutorForTests(null);
    if (converged)
    {
      PowerCfgProcessRunner.RunOverrideForTests = null;
      PowerCfgProcessRunner.RunAsyncOverrideForTests = null;
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }

    failure?.Throw();
    if (!converged)
      throw new TimeoutException(
        $"A catalog load or scheme-service call was still running after {ConvergenceTimeout.TotalSeconds:0} s; the global test hooks were left in place.");
  }
}
```

- [ ] **Step 2: 写失败测试**

(a) `SettingsWindowControllerTests`，在 `TryCreate_returns_before_slow_plan_catalog_load_completes` 之后加入：

```csharp
    [Fact]
    public void Plan_catalog_is_loaded_off_the_ui_thread_through_open_interact_and_close()
    {
        RunOnSta(() =>
        {
            var uiThread = Environment.CurrentManagedThreadId;
            var loaderThreads = new System.Collections.Concurrent.ConcurrentBag<int>();
            PowerPlanCatalogProvider.ClearSessionCacheForTests();
            PowerPlanCatalogProvider.LoadImplementationForTests = () =>
            {
                loaderThreads.Add(Environment.CurrentManagedThreadId);
                Thread.Sleep(100);
                return new Dictionary<Guid, string>
                {
                    [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能（测试）",
                    [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡（测试）",
                    [PowerPlanCatalogProvider.PowerSaverPlanGuid] = "节能（测试）",
                };
            };

            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsUiCatalog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Window? window = null;
            try
            {
                var repository = new GuardConfigRepository(Path.Combine(tempRoot, "SmartGuard.config.json"));
                var controller = SettingsWindowController.TryCreate(tempRoot, repository, repository.LoadOrDefault(tempRoot));
                controller.Should().NotBeNull();
                window = GetWindowField(controller!);

                // Open: show the real window, as the user does; names arrive only through the background
                // load and its UI callback.
                if (!window.IsVisible)
                    WpfStaTestHost.ShowAndWait(window);
                var active = (ComboBox)window.FindName("cmbActivePlan");
                WpfStaTestHost.WaitUntil(
                    () => active.Items.Cast<PowerPlanComboItem>().Any(item => item.DisplayName == "平衡（测试）"),
                    TimeSpan.FromSeconds(10), "the plan names to arrive through the background load");
                loaderThreads.Should().NotBeEmpty("the catalog was actually loaded");
                // Opening the window is where the old code loaded synchronously; assert before any interaction.
                loaderThreads.Should().NotContain(uiThread, "opening the window must not load on the UI thread");

                // Interact: the plan combos live on the advanced page (pageAdvanced), collapsed at start.
                // Navigate there as the user does, then the dropdown really opens and realizes its items;
                // picking a plan must not trigger a load on the UI thread.
                controller!.NavigateTo("advanced");
                WpfStaTestHost.WaitUntil(() => active.IsVisible, TimeSpan.FromSeconds(10), "the advanced page to show the plan combo");
                active.Focus();
                active.IsDropDownOpen = true;
                WpfStaTestHost.WaitUntil(
                    () => active.IsDropDownOpen
                        && active.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated,
                    TimeSpan.FromSeconds(10), "the plan dropdown to open and realize its items");
                active.SelectedItem = active.Items.Cast<PowerPlanComboItem>().First(item => item.DisplayName == "平衡（测试）");
                active.IsDropDownOpen = false;
                WpfStaTestHost.PumpDispatcher(window);

                // Close through the window; its Closing handler disposes the controller.
                window.Close();
                window = null;

                loaderThreads.Should().NotContain(uiThread, "interacting and closing must not load on the UI thread either");
            }
            finally
            {
                window?.Close();
                // Converge before the global hook is reset: every load that started has published (the load
                // gate is free), and any UI callback it queued has run or been dropped. A timeout throws.
                var idle = PowerPlanCatalogProvider.WhenLoadsIdleForTests();
                WpfStaTestHost.WaitUntil(() => idle.IsCompleted, TimeSpan.FromSeconds(10),
                    "every catalog load to finish after the window closed");
                WpfStaTestHost.WaitForRender();
                PowerPlanCatalogProvider.ClearSessionCacheForTests();
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }
```

（`GetWindowField` 是本测试文件已有的辅助方法，其他用例也用它取窗口并以 `ShowAndWait` 显示。`cmbActivePlan` 位于 XAML 的 `pageAdvanced`（初始折叠），`NavigateTo("advanced")` 对应导航第 1 项。展开下拉框的方式与 `LogWorkbenchCommandsTests` 中已通过的用例相同：先 `Focus()` 再设 `IsDropDownOpen`。`using System.Windows;`、`using System.Windows.Controls;` 以文件现有 using 为准。）

(b) `PowerPlanComboItemsBuilderTests` 中加入：

```csharp
  [Theory]
  [InlineData("Loading", "高性能（正在加载）")]
  [InlineData("Failed", "高性能（无法读取计划列表）")]
  [InlineData("Listed", "高性能（名称读取失败）")]
  [InlineData("Absent", "高性能（未在本机找到）")]
  public void Selected_plan_label_tells_loading_unavailable_unreadable_and_absent_apart(string situation, string expected)
  {
    var selected = Guid.Parse("56c8d9b8-d34d-4d19-96a4-c177cf5f4882");
    var none = new Dictionary<Guid, string>();
    var snapshot = situation switch
    {
      "Loading" => PowerPlanCatalogSnapshot.Loading,
      "Failed" => PowerPlanCatalogSnapshot.Failed,
      "Listed" => new PowerPlanCatalogSnapshot(PowerPlanCatalogState.Loaded, none, new HashSet<Guid> { selected }, new HashSet<Guid>()),
      _ => new PowerPlanCatalogSnapshot(PowerPlanCatalogState.Loaded, none, new HashSet<Guid>(), new HashSet<Guid>()),
    };
    PowerPlanComboItemsBuilder.Build(snapshot, selected, "高性能").Should().ContainSingle()
      .Which.DisplayName.Should().Be(expected);
  }
```

（参数为字符串，避免公开测试方法暴露 internal 类型，即 CS0051。）

(c) `SettingsPlanCatalogCoordinatorTests` 中加入两个用例（构造方式与现有 `Dispose_invalidates_catalog_generation…` 相同）：

```csharp
  [Fact]
  public void Failed_catalog_is_shown_as_unavailable_and_a_later_load_recovers()
  {
    WpfStaTestHost.Run(() =>
    {
      var failing = true;
      var native = new FakePowrProfNative
      {
        Enumerate = () => failing ? (5u, []) : (0u, [PowerPlanCatalogProvider.HighPerformancePlanGuid,
          PowerPlanCatalogProvider.BalancedPlanGuid, PowerPlanCatalogProvider.PowerSaverPlanGuid]),
        ReadName = guid => (0, guid == PowerPlanCatalogProvider.HighPerformancePlanGuid ? "高性能（真）" : "其他"),
      };
      PowerSchemeTestScope.WithNativeSync(native, () =>
      {
        var window = new Window();
        var (active, balanced, saver, status) = (new ComboBox(), new ComboBox(), new ComboBox(), new TextBlock());
        var config = GuardConfig.CreateDefault(Path.GetTempPath());
        using var coordinator = new SettingsPlanCatalogCoordinator(window, active, balanced, saver, status, () => config, () => config);

        coordinator.BeginLoadPlanCatalogAsync();
        WpfStaTestHost.WaitUntil(() => status.Text.StartsWith("无法读取电源计划列表", StringComparison.Ordinal),
          TimeSpan.FromSeconds(20), "an exhausted load to be shown as unavailable, not as still loading");
        active.Items.Cast<PowerPlanComboItem>().Single().DisplayName.Should().Be("高性能（无法读取计划列表）");

        failing = false;
        coordinator.BeginLoadPlanCatalogAsync();
        WpfStaTestHost.WaitUntil(() => active.Items.Cast<PowerPlanComboItem>().Any(item => item.DisplayName == "高性能（真）"),
          TimeSpan.FromSeconds(20), "a later successful load to recover");
        status.Text.Should().Be("三档计划映射正常");
      });
    });
  }

  [Fact]
  public void Unreadable_plan_name_is_not_reported_as_missing()
  {
    WpfStaTestHost.Run(() =>
    {
      var native = new FakePowrProfNative
      {
        Enumerate = () => (0, [PowerPlanCatalogProvider.HighPerformancePlanGuid, PowerPlanCatalogProvider.BalancedPlanGuid,
          PowerPlanCatalogProvider.PowerSaverPlanGuid]),
        ReadName = guid => guid == PowerPlanCatalogProvider.HighPerformancePlanGuid ? (5u, string.Empty) : (0u, "其他"),
      };
      PowerSchemeTestScope.WithNativeSync(native, () =>
      {
        var window = new Window();
        var (active, balanced, saver, status) = (new ComboBox(), new ComboBox(), new ComboBox(), new TextBlock());
        var config = GuardConfig.CreateDefault(Path.GetTempPath());
        using var coordinator = new SettingsPlanCatalogCoordinator(window, active, balanced, saver, status, () => config, () => config);

        coordinator.BeginLoadPlanCatalogAsync();
        WpfStaTestHost.WaitUntil(() => status.Text != "正在加载电源计划...", TimeSpan.FromSeconds(20), "the load to finish");
        active.Items.Cast<PowerPlanComboItem>().First().DisplayName.Should().Be("高性能（名称读取失败）");
        status.Text.Should().NotContain("未在本机找到", "the plan exists; only its name could not be read");
      });
    });
  }
```

（已核实：`SettingsPlanCatalogCoordinator` 实现 `IDisposable`，`WpfStaTestHost.PumpDispatcher(Window)` 已存在，Settings 已对 `SmartGuard.Settings.Tests` 开放 internal。）

(d) `SettingsPlanCatalogArchitectureTests` 中把

```csharp
    catalog.Should().Contain("PowerPlanCatalogProvider.TryLoad()");
```

改为

```csharp
    // S-05: the UI reads the IO-free snapshot; loads only run through the cancelable async path.
    catalog.Should().Contain("PowerPlanCatalogProvider.Snapshot");
    catalog.Should().NotContain("PowerPlanCatalogProvider.TryLoad()");
```

- [ ] **Step 3: 骨架并取 RED**

`PowerPlanComboItemsBuilder` 新增 internal 重载，行为暂同旧重载（只看名称）：

```csharp
  internal static IReadOnlyList<PowerPlanComboItem> Build(PowerPlanCatalogSnapshot snapshot, Guid selectedGuid,
    string? orphanRoleLabel = null)
    => Build(snapshot.Names, selectedGuid, orphanRoleLabel);
```

`PowerPlanMappingValidator` 新增 internal 重载，行为暂同旧重载：

```csharp
  internal static IReadOnlyList<string> Validate(GuardConfig config, PowerPlanCatalogSnapshot snapshot)
    => Validate(config, snapshot.Names);
```

```powershell
foreach ($p in 'Configuration','Settings','Architecture') { dotnet build "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore -v q; "$p build exit=$LASTEXITCODE" }
dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj --no-build --filter "FullyQualifiedName~Plan_catalog_is_loaded_off_the_ui_thread|FullyQualifiedName~Selected_plan_label_tells|FullyQualifiedName~Failed_catalog_is_shown|FullyQualifiedName~Unreadable_plan_name" --logger "trx;LogFileName=task2-red-settings.trx" --results-directory .work/power-scheme-api-phase2
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-build --filter FullyQualifiedName~SettingsPlanCatalogArchitectureTests --logger "trx;LogFileName=task2-red-arch.trx" --results-directory .work/power-scheme-api-phase2
```

Expected: 编译退出码均为 0。失败用例：

- UI 线程用例：`PopulatePlanCombo` 经同步 `TryLoad()` 在 STA 线程调用加载器，`NotContain(uiThread)` 失败。前面的 `NotBeEmpty` 与名称等待保证测试确实走到了加载路径，不会因加载器未被调用而假绿。线程断言紧跟名称等待、位于任何交互之前，所以 RED 由“打开窗口即在 UI 线程加载”产生，不会被后续下拉框操作的超时掩盖。旧实现的后台异步加载同样经过加载门，收尾的收敛等待照常完成。
- 文案 Theory：`Loading`、`Failed`、`Listed` 三例失败（仍为“未在本机找到”），`Absent` 通过，属保护性用例。
- 协调器失败用例：状态栏不会出现“无法读取…”（旧路径不产生 `Failed`），等待超时失败。
- 名称读取失败用例：下拉框为“（未在本机找到）”，失败。
- 架构用例：仍含 `TryLoad()`，失败。

- [ ] **Step 4: 实现**

(a) `PowerPlanComboItemsBuilder` 快照重载替换为：

```csharp
  // The label of a selected plan without a name states why: still loading, list unreadable,
  // name unreadable, or confirmed absent on this machine.
  internal static IReadOnlyList<PowerPlanComboItem> Build(PowerPlanCatalogSnapshot snapshot, Guid selectedGuid,
    string? orphanRoleLabel = null)
  {
    var items = snapshot.Names
      .Select(entry => new PowerPlanComboItem { PlanGuid = entry.Key, DisplayName = entry.Value })
      .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
      .ToList();
    if (selectedGuid == Guid.Empty || snapshot.Names.ContainsKey(selectedGuid)) return items;

    var suffix = snapshot.PresenceOf(selectedGuid) switch
    {
      PowerPlanPresence.Loading => "（正在加载）",
      PowerPlanPresence.ListUnavailable => "（无法读取计划列表）",
      PowerPlanPresence.NameUnreadable => "（名称读取失败）",
      _ => "（未在本机找到）",
    };
    var role = string.IsNullOrWhiteSpace(orphanRoleLabel) ? selectedGuid.ToString() : orphanRoleLabel;
    items.Insert(0, new PowerPlanComboItem { PlanGuid = selectedGuid, DisplayName = role + suffix });
    return items;
  }
```

(b) `PowerPlanMappingValidator` 快照重载替换为：

```csharp
  // Only a confirmed absence is a mapping error; loading, an unreadable list and unreadable names are not.
  internal static IReadOnlyList<string> Validate(GuardConfig config, PowerPlanCatalogSnapshot snapshot)
  {
    var messages = new List<string>(ValidateDuplicates(config));
    foreach (var (plan, role) in new[]
    {
      (config.ActivePlanGuid, PowerPlanCatalogProvider.HighPerformanceDisplayName),
      (config.BalancedPlanGuid, PowerPlanCatalogProvider.BalancedDisplayName),
      (config.PowerSaverPlanGuid, PowerPlanCatalogProvider.PowerSaverDisplayName),
    })
    {
      if (plan != Guid.Empty && snapshot.PresenceOf(plan) == PowerPlanPresence.ConfirmedAbsent)
        messages.Add($"{role}计划未在本机找到");
    }
    return messages;
  }
```

(c) `SettingsPlanCatalogCoordinator.UpdatePlanMappingStatus` 第 81 行起替换为：

```csharp
    config ??= _readConfigFromUi();
    var snapshot = PowerPlanCatalogProvider.Snapshot;
    if (snapshot.State == PowerPlanCatalogState.Loading)
    {
      _lblPlanMappingStatus.Text = "正在加载电源计划...";
      return;
    }
    if (snapshot.State == PowerPlanCatalogState.Failed)
    {
      _lblPlanMappingStatus.Text = "无法读取电源计划列表，可重新打开设置重试";
      return;
    }
    var messages = PowerPlanMappingValidator.Validate(config, snapshot);
```

第 187 行：

```csharp
      var items = PowerPlanComboItemsBuilder.Build(PowerPlanCatalogProvider.Snapshot, selectedGuid, orphanRoleLabel);
```

`ApplyPlanCatalog(catalog)` 不变：快照在加载回调之前已经发布。

- [ ] **Step 5: GREEN**

```powershell
foreach ($p in 'Configuration','Settings','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task2-green-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

Expected: 全部通过。若既有 Settings 用例断言窗口打开后**立即**出现目录名称，它依赖的正是 UI 线程同步加载：改为等待加载回调后再断言，不放宽为“任意结果”，并在提交说明中列明。

- [ ] **Step 6: 提交**

```powershell
git add -- src/SmartGuard.Settings/SettingsPlanCatalogCoordinator.cs src/SmartGuard.Settings/PowerPlanComboItemsBuilder.cs src/SmartGuard.Configuration/PowerPlanMappingValidator.cs Tests/SmartGuard.Settings.Tests/FakePowrProfNative.cs Tests/SmartGuard.Settings.Tests/PowerSchemeTestScope.cs Tests/SmartGuard.Settings.Tests/SettingsWindowControllerTests.cs Tests/SmartGuard.Settings.Tests/SettingsPlanCatalogCoordinatorTests.cs Tests/SmartGuard.Settings.Tests/PowerPlanComboItemsBuilderTests.cs Tests/SmartGuard.Architecture.Tests/SettingsPlanCatalogArchitectureTests.cs
git commit -m "fix(settings): load the plan catalog off the ui thread and tell missing from unreadable" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 删除同步与 powercfg 目录路径

**Files:**
- Modify: `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs`
- Delete: `src/SmartGuard.Configuration/PowerPlanCatalogParser.cs`、`Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogParserTests.cs`
- Modify: `Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs`、`Tests/SmartGuard.Configuration.Tests/LiveCatalogProbeTests.cs`、`Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs`

- [ ] **Step 1: 迁移依赖同步入口的用例（先改测试）**

以下用例使用同步 `TryLoad`/`LoadWithRetry`，改为等价的异步断言。加载器仍经 `LoadImplementationForTests`，调用次数按现有 `maxAttempts + 1` 语义计算：

| 旧用例 | 改写后的断言 |
|--------|--------------|
| `TryLoad_reuses_session_cache_without_calling_loader_again` | 加载器返回完整目录；两次 `await LoadWithRetryAsync()` 共调用加载器 **1** 次 |
| `TryLoad_retries_after_loader_failure_without_caching_empty_catalog` | 加载器第一次抛异常、之后返回完整目录；`await LoadWithRetryAsync(maxAttempts: 2)` 返回完整目录，共调用 **2** 次 |
| `TryLoad_does_not_cache_empty_catalog` | 加载器返回空；两次 `LoadWithRetryAsync(maxAttempts: 1)` 共调用 **4** 次（每次调用 1 次尝试加 1 次收尾重载） |
| `TryLoad_does_not_cache_incomplete_catalog` | 加载器返回缺节能方案的目录；两次 `maxAttempts: 1` 共调用 **4** 次；`Snapshot.State == Loaded`，`PresenceOf(PowerSaver) == ConfirmedAbsent` |
| `LoadWithRetry_returns_complete_catalog_from_loader` | `await LoadWithRetryAsync(maxAttempts: 2)` 返回完整目录 |

示例（第一行，其余按同一结构写出）：

```csharp
  [Fact]
  public async Task LoadWithRetryAsync_reuses_session_cache_without_calling_loader_again()
  {
    var calls = 0;
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      Interlocked.Increment(ref calls);
      return new Dictionary<Guid, string>
      {
        [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能",
        [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
        [PowerPlanCatalogProvider.PowerSaverPlanGuid] = "节能",
      };
    };
    try
    {
      await PowerPlanCatalogProvider.LoadWithRetryAsync();
      await PowerPlanCatalogProvider.LoadWithRetryAsync();
      calls.Should().Be(1);
    }
    finally { PowerPlanCatalogProvider.ClearSessionCacheForTests(); }
  }
```

`LiveCatalogProbeTests.TryLoad_reads_local_power_plans` 改名为 `LoadWithRetryAsync_reads_local_power_plans`，方法改为 `async Task`，`var catalog = PowerPlanCatalogProvider.TryLoad();` 改为 `var catalog = await PowerPlanCatalogProvider.LoadWithRetryAsync();`（保留 `Category=Live` 与原断言）。

- [ ] **Step 2: 确认迁移后的用例在当前实现上通过**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerPlanCatalogProviderTests" --logger "trx;LogFileName=task3-migrated.trx" --results-directory .work/power-scheme-api-phase2
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter "Category=Live" --logger "trx;LogFileName=task3-migrated-live.trx" --results-directory .work/power-scheme-api-phase2
```

Expected: 全部通过。迁移不改变行为，属回归保护，不冒称 RED。若次数与上表不符，以 `PowerPlanCatalogProvider.LoadWithRetryAsync` 的实际循环核对后再修正**表格与用例**，不修改生产语义。

- [ ] **Step 3: 门禁 RED**

`PowerSchemeApiArchitectureTests` 中加入：

```csharp
  [Fact]
  public void Plan_catalog_has_no_synchronous_or_powercfg_path()
  {
    var provider = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs");
    provider.Should().NotContain("PowerCfgProcessRunner").And.NotContain("PowerPlanCatalogParser");
    provider.Should().NotContain("public static IReadOnlyDictionary<Guid, string> TryLoad()");
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Configuration", "PowerPlanCatalogParser.cs"))
      .Should().BeFalse();
  }
```

```powershell
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --filter FullyQualifiedName~Plan_catalog_has_no_synchronous_or_powercfg_path --logger "trx;LogFileName=task3-red.trx" --results-directory .work/power-scheme-api-phase2
```

Expected: 1 failed。

- [ ] **Step 4: 删除**

从 `PowerPlanCatalogProvider` 删除以下成员，以及不再使用的 `using`（以编译为准）：

- 同步路径：`TryLoad`、`LoadWithRetry`、`EnrichWithHiddenSchemes`、`LoadCore`、`RunPowerCfg`、`TryQueryPlanName`
- 旧异步路径：`TryLoadAsync`、`LoadCoreAsync`、`EnrichWithHiddenSchemesAsync`、`TryQueryPlanNameAsync`

```powershell
git rm -q -- src/SmartGuard.Configuration/PowerPlanCatalogParser.cs Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogParserTests.cs
Get-ChildItem src,Tests -Recurse -Include *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-String -Pattern 'PowerPlanCatalogProvider\.(TryLoad\(\)|LoadWithRetry\(|EnrichWithHiddenSchemes)|PowerPlanCatalogParser'
```

Expected: 第二条只剩 `PowerPlanCatalogSingleSourceTests`、`GuardianStartupCatalogTests` 与 `SettingsPlanCatalogArchitectureTests` 中的 `NotContain` 断言。`PowerPlanCatalogParserTests` 中 `EnrichWithHiddenSchemes` 的覆盖语义（补读隐藏的内置方案）已由第一阶段 `TryLoadOnceAsync_reads_listed_and_hidden_known_names…` 与 Task 1 `Presence_separates…` 覆盖。

- [ ] **Step 5: GREEN**

```powershell
foreach ($p in 'Configuration','Engine','Settings','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task3-green-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

- [ ] **Step 6: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs Tests/SmartGuard.Configuration.Tests/LiveCatalogProbeTests.cs Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs
git commit -m "refactor(config): remove synchronous and powercfg plan catalog paths" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Tray 一键高性能经电源服务切换

**Files:**
- Modify: `src/SmartGuard.Configuration/IPowerPlanActivator.cs`、`PowerPlanActivator.cs`、`HighPerformanceBoost.cs`、`ManualHighPerformanceMessages.cs`
- Create: `Tests/SmartGuard.Configuration.Tests/PowerPlanActivatorTests.cs`
- Modify: `Tests/SmartGuard.Configuration.Tests/HighPerformanceBoostTests.cs`

- [ ] **Step 1: 隔离边界（保持默认行为）**

接口改为返回状态，并改为 internal（调用方只有 Tray，已有 IVT）：

```csharp
namespace SmartGuard.Configuration;

internal interface IPowerPlanActivator
{
  PowerSchemeStatus SetActivePlan(Guid planGuid);
}
```

`PowerPlanActivator` 保留旧实现，只把进程启动换成可替换边界；未设置替换时仍调用 `Process.Start`：

```csharp
using System.Diagnostics;

namespace SmartGuard.Configuration;

internal sealed class PowerPlanActivator : IPowerPlanActivator
{
  // Test seam around the legacy process start only; unset, it is Process.Start, so behaviour is unchanged.
  // AsyncLocal on purpose: a call keeps the starter of the context it was queued from, so resetting the
  // seam can never hand a test call that is still running (or not yet started) the real Process.Start.
  // Removed together with the legacy implementation in Step 6.
  private static readonly AsyncLocal<Func<ProcessStartInfo, Process?>?> ProcessStarterForTests = new();

  internal static void UseProcessStarterForTests(Func<ProcessStartInfo, Process?>? starter)
    => ProcessStarterForTests.Value = starter;

  public PowerSchemeStatus SetActivePlan(Guid planGuid)
  {
    var psi = new ProcessStartInfo("powercfg.exe", $"/setactive {planGuid:D}")
    {
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    using var proc = ProcessStarterForTests.Value is { } starter ? starter(psi) : Process.Start(psi);
    proc?.WaitForExit();
    return new PowerSchemeStatus("none", null, 0);
  }
}
```

`HighPerformanceBoost` 改为 `internal static class`，调用处改为 `_ = activator.SetActivePlan(config.ActivePlanGuid);`。

```powershell
foreach ($p in 'Configuration','Tray','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task4-seam-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

Expected: 全部通过（边界不改变行为）。`HighPerformanceBoostTests` 中的 `RecordingPowerPlanActivator` 需随接口改为返回 `new PowerSchemeStatus("none", null, 0)`。

- [ ] **Step 2: 写失败测试**

`Tests/SmartGuard.Configuration.Tests/PowerPlanActivatorTests.cs`：

```csharp
using System.Diagnostics;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("PowerPlanCatalogProvider")]
public class PowerPlanActivatorTests
{
  private static readonly Guid High = PowerPlanCatalogProvider.HighPerformancePlanGuid;

  private sealed class HarmlessStarts
  {
    internal List<Process> Children { get; } = new();
    // Set when the switch under test begins: by the legacy start below, or by the fake's SetActive.
    internal ManualResetEventSlim Entered { get; } = new();
  }

  // The legacy activator starts a process; in tests it starts a harmless child instead of powercfg, so no
  // RED ever changes this machine's power plan. The seam is AsyncLocal: a Task.Run queued after this call
  // captures the harmless starter even if it only starts running after the test has reset the seam.
  private static HarmlessStarts UseHarmlessChild(string cmdArguments)
  {
    var starts = new HarmlessStarts();
    PowerPlanActivator.UseProcessStarterForTests(_ =>
    {
      var child = Process.Start(new ProcessStartInfo("cmd.exe", cmdArguments) { UseShellExecute = false, CreateNoWindow = true });
      if (child is not null) lock (starts.Children) starts.Children.Add(child);
      starts.Entered.Set();
      return child;
    });
    return starts;
  }

  private static void KillChildren(HarmlessStarts starts)
  {
    lock (starts.Children)
      foreach (var child in starts.Children)
      {
        try
        {
          if (!child.HasExited)
          {
            child.Kill(entireProcessTree: true);
            child.WaitForExit(5000);
          }
        }
        catch (InvalidOperationException) { }
      }
  }

  // Order matters. The fake was already released inside the scope. Here our children end first, which
  // unblocks the legacy wait; children are killed repeatedly because a late call may still start one.
  // Only after the call under test has finished are the children disposed and the seam reset. If it never
  // finishes, nothing it may still use is disposed and the test fails.
  private static void Cleanup(HarmlessStarts starts, Task? call)
  {
    var watch = Stopwatch.StartNew();
    while (true)
    {
      KillChildren(starts);
      if (call is null || call.IsCompleted || watch.Elapsed > TimeSpan.FromSeconds(10)) break;
      Thread.Sleep(50);
    }
    var finished = call is null || call.IsCompleted;
    if (finished)
      lock (starts.Children)
        foreach (var child in starts.Children) child.Dispose();
    PowerPlanActivator.UseProcessStarterForTests(null);
    finished.Should().BeTrue("the call under test must finish before the test ends");
  }

  [Fact]
  public async Task Activation_switches_through_the_scheme_service()
  {
    var starts = UseHarmlessChild("/c exit 0");
    var native = new FakePowrProfNative();
    PowerSchemeStatus status = default;
    try
    {
      await PowerSchemeTestScope.WithNative(native, () =>
      {
        status = new PowerPlanActivator().SetActivePlan(High);
        return Task.CompletedTask;
      });
    }
    finally { Cleanup(starts, call: null); }
    status.Succeeded.Should().BeTrue();
    native.Calls.Should().Equal($"set-active {High:D}");
  }

  [Fact]
  public async Task Activation_returns_within_a_bound_when_the_switch_hangs()
  {
    // Legacy path: the child runs about 30 s and the activator waits for it without a bound.
    var starts = UseHarmlessChild("/c ping -n 30 127.0.0.1 >nul");
    using var release = new ManualResetEventSlim();
    // Scheme service path: the native call blocks; the executor's deadline must bound the wait.
    var native = new FakePowrProfNative
    {
      SetActive = _ =>
      {
        starts.Entered.Set();
        release.Wait(TimeSpan.FromSeconds(30));
        return 0;
      },
    };
    PowerSchemeStatus status = default;
    Task? call = null;
    var entered = false;
    var finished = false;
    try
    {
      await PowerSchemeTestScope.WithNative(native, () =>
      {
        try
        {
          call = Task.Run(() => status = new PowerPlanActivator().SetActivePlan(High));
          // The bound is measured from the switch itself, not from when the thread pool ran the task.
          entered = starts.Entered.Wait(TimeSpan.FromSeconds(10));
          finished = entered && call.Wait(TimeSpan.FromSeconds(4));
        }
        finally { release.Set(); }
        return Task.CompletedTask;
      }, deadline: TimeSpan.FromMilliseconds(100));
    }
    finally { Cleanup(starts, call); }
    entered.Should().BeTrue("the switch under test must have started");
    finished.Should().BeTrue("the tray must never wait without a bound for a power switch");
    status.FailureKind.Should().Be("timed-out");
  }
}
```

`HighPerformanceBoostTests` 中加入失败路径用例，并让 `RecordingPowerPlanActivator` 支持注入结果：

```csharp
  [Fact]
  public void Failed_activation_keeps_the_manual_boost_and_says_the_guardian_takes_over()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");
    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(GuardConfig.CreateDefault(dir)));
    try
    {
      var repo = new GuardConfigRepository(path);
      var activator = new RecordingPowerPlanActivator { Result = new PowerSchemeStatus("api-error", 5, 1) };

      HighPerformanceBoost.Apply(repo, dir, activator, TimeSpan.FromMinutes(45));

      var saved = repo.TryLoad()!;
      saved.ManualHighPerformanceUntil.Should().NotBeNull("the guardian enforces the boost even if the tray switch failed");
      // AppendInfoLog writes the structured main log; the startup log is only its fallback.
      var log = File.ReadAllText(SmartGuardPaths.ResolveLogFile(saved, dir), System.Text.Encoding.UTF8);
      log.Should().Contain("立即切换未完成").And.Contain("错误码 5").And.Contain("守护程序将在下一轮切换");
    }
    finally { Directory.Delete(dir, recursive: true); }
  }

  private sealed class RecordingPowerPlanActivator : IPowerPlanActivator
  {
    public Guid? LastGuid { get; private set; }
    public PowerSchemeStatus Result { get; init; } = new("none", null, 0);

    public PowerSchemeStatus SetActivePlan(Guid planGuid)
    {
      LastGuid = planGuid;
      return Result;
    }
  }
```

（已核实：`GuardConfigRepository.AppendInfoLog` 经 `LogStructuredSink.TryWriteBatch` 写入 `SmartGuardPaths.ResolveLogFile(config, root)` 指向的主日志，写入失败才回退到 `StartupLogFile`；主日志 JSONL 中中文不转义，可直接按文本断言。）

- [ ] **Step 3: RED**

```powershell
powercfg /getactivescheme | Out-File -Encoding utf8 .work/power-scheme-api-phase2/task4-red-machine-state.txt
dotnet build Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore -v q; "build exit=$LASTEXITCODE"
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-build --filter "FullyQualifiedName~PowerPlanActivatorTests|FullyQualifiedName~HighPerformanceBoostTests" --logger "trx;LogFileName=task4-red.trx" --results-directory .work/power-scheme-api-phase2
powercfg /getactivescheme | Out-File -Append -Encoding utf8 .work/power-scheme-api-phase2/task4-red-machine-state.txt
```

Expected: 3 例失败，且原因均为原缺陷：

- `Activation_switches…`：旧实现启动的是无害子进程并等待其退出，替身未被调用，`native.Calls` 为空。
- `Activation_returns_within_a_bound…`：`entered` 为 true（无害子进程已启动），之后 4 秒内未返回，`finished` 为 false。这直接证明旧实现没有等待上界。`Cleanup` 结束子进程，确认被测调用随之返回后才释放子进程对象并复位边界。
- `Failed_activation…`：激活结果被丢弃，日志中没有说明。

原用例 `Apply_sets_manual_boost…` 通过。前后两次 `getactivescheme` 一致：RED 未启动 powercfg。

- [ ] **Step 4: 实现**

`PowerPlanActivator` 的 `SetActivePlan` 替换为：

```csharp
  // Tray manual switch through PowerSchemeService (PWR-INV-SCHEME-API); bounded by its deadline.
  public PowerSchemeStatus SetActivePlan(Guid planGuid)
    => PowerSchemeService.SetActiveAsync(planGuid).GetAwaiter().GetResult().Status;
```

（`UseProcessStarterForTests` 此步暂留，Step 6 删除。）

`ManualHighPerformanceMessages.cs` 加入：

```csharp
  public static string FormatActivationDeferred(string failure)
    => $"托盘手动切换高性能：立即切换未完成（{failure}），守护程序将在下一轮切换";
```

`HighPerformanceBoost.Apply` 中把激活与日志改为：

```csharp
    var activation = activator.SetActivePlan(config.ActivePlanGuid);
    repository.AppendInfoLog(
      ManualHighPerformanceMessages.FormatApplied(until),
      root,
      SmartGuardPaths.StartupLogFile(root));
    if (!activation.Succeeded)
      repository.AppendInfoLog(
        ManualHighPerformanceMessages.FormatActivationDeferred(DescribeActivationFailure(activation)),
        root,
        SmartGuardPaths.StartupLogFile(root));
```

并加入：

```csharp
  // Same labels as the engine's PowerCfgExecutor.DescribeFailure; Configuration cannot reference Engine.
  private static string DescribeActivationFailure(PowerSchemeStatus status) => status.FailureKind switch
  {
    "timed-out" => "电源服务调用超时",
    "call-pending" => "上次电源服务调用仍未返回",
    _ => status.Win32Error is int code ? $"电源服务调用失败（错误码 {code}）" : "电源服务调用失败",
  };
```

- [ ] **Step 5: GREEN**

```powershell
foreach ($p in 'Configuration','Tray','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task4-green-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

- [ ] **Step 6: 重构：删除旧边界（保持全绿）**

删除 `PowerPlanActivator` 中的 `ProcessStarterForTests`、`UseProcessStarterForTests` 与 `using System.Diagnostics;`。

`PowerPlanActivatorTests` 删除 `HarmlessStarts`、`UseHarmlessChild`、`KillChildren`、`Cleanup` 与“Legacy path”注释；`using System.Diagnostics;` 不再需要也一并删除。两个用例改为下面的形式（替身断言不变，有界等待的握手改由替身完成，被测调用仍须在测试结束前返回）：

```csharp
  [Fact]
  public async Task Activation_switches_through_the_scheme_service()
  {
    var native = new FakePowrProfNative();
    PowerSchemeStatus status = default;
    await PowerSchemeTestScope.WithNative(native, () =>
    {
      status = new PowerPlanActivator().SetActivePlan(High);
      return Task.CompletedTask;
    });
    status.Succeeded.Should().BeTrue();
    native.Calls.Should().Equal($"set-active {High:D}");
  }

  [Fact]
  public async Task Activation_returns_within_a_bound_when_the_switch_hangs()
  {
    using var started = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    // The native call blocks; the executor's deadline must bound the wait.
    var native = new FakePowrProfNative
    {
      SetActive = _ =>
      {
        started.Set();
        release.Wait(TimeSpan.FromSeconds(30));
        return 0;
      },
    };
    PowerSchemeStatus status = default;
    Task? call = null;
    var entered = false;
    var finished = false;
    await PowerSchemeTestScope.WithNative(native, () =>
    {
      try
      {
        call = Task.Run(() => status = new PowerPlanActivator().SetActivePlan(High));
        // The bound is measured from the switch itself, not from when the thread pool ran the task.
        entered = started.Wait(TimeSpan.FromSeconds(10));
        finished = entered && call.Wait(TimeSpan.FromSeconds(4));
      }
      finally { release.Set(); }
      return Task.CompletedTask;
    }, deadline: TimeSpan.FromMilliseconds(100));
    call!.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the call under test must finish before the test ends");
    entered.Should().BeTrue("the switch under test must have started");
    finished.Should().BeTrue("the tray must never wait without a bound for a power switch");
    status.FailureKind.Should().Be("timed-out");
  }
```

```powershell
foreach ($p in 'Configuration','Tray') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task4-refactor-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

- [ ] **Step 7: 提交**

```powershell
git add -- src/SmartGuard.Configuration/IPowerPlanActivator.cs src/SmartGuard.Configuration/PowerPlanActivator.cs src/SmartGuard.Configuration/HighPerformanceBoost.cs src/SmartGuard.Configuration/ManualHighPerformanceMessages.cs Tests/SmartGuard.Configuration.Tests/PowerPlanActivatorTests.cs Tests/SmartGuard.Configuration.Tests/HighPerformanceBoostTests.cs
git commit -m "fix(tray): switch to high performance through the scheme service with a deadline" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 删除 powercfg 包装并把门禁扩展到全部 src

**Files:**
- Modify: `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs`
- Modify: `Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs`、`Tests/SmartGuard.Engine.Tests/PowerSchemeTestScope.cs`、`Tests/SmartGuard.Settings.Tests/PowerSchemeTestScope.cs`
- Modify: `Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs`

- [ ] **Step 1: 门禁 RED**

把 `Engine_never_starts_powercfg` 整体替换为：

```csharp
  [Fact]
  public void Product_never_starts_powercfg()
  {
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;
      var source = File.ReadAllText(file, Encoding.UTF8);
      source.Should().NotContainEquivalentOf("\"powercfg", $"{file} must use PowerSchemeService (PWR-INV-SCHEME-API)");
      source.Should().NotContain("RunPowerCfg", $"{file} must not keep a powercfg runner");
      source.Should().NotContain("\"/setactive").And.NotContain("\"/getactivescheme");
    }
  }
```

```powershell
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --filter FullyQualifiedName~Product_never_starts_powercfg --logger "trx;LogFileName=task5-red.trx" --results-directory .work/power-scheme-api-phase2
```

Expected: 1 failed（`PowerCfgProcessRunner.cs` 含 `"powercfg.exe"` 与 `RunPowerCfg`）。

- [ ] **Step 2: 删除**

从 `PowerCfgProcessRunner` 删除 `DefaultTimeout`、`RunOverrideForTests`、`RunAsyncOverrideForTests`、`RunPowerCfg`、`RunPowerCfgAsync`，保留 `RunProcess`、`RunProcessAsync`、`RunProcessCoreAsync` 及其私有辅助（以编译为准）。

三个 `PowerSchemeTestScope` 删除 `Blocked` 字段及封锁、复位语句，删除后的形式：
- **Engine：** 回到第一阶段的形式。
- **Configuration：** `finally` 不再需要 `idle` 变量（否则只剩空的 `if (idle) { }`），恢复为第一阶段的形式，超时仍由 `WaitUntilIdleAsync` 抛出：

```csharp
    finally
    {
      // A blocked fake must return before the scope ends: the gate belongs to the in-flight call.
      try { await WaitUntilIdleAsync(); }
      finally
      {
        // Reset in this frame (not in a nested async method) so the change applies to this flow.
        PowerSchemeService.UseExecutorForTests(null);
        PowerPlanCatalogProvider.ClearSessionCacheForTests();
      }
    }
```

- **Settings：** 只删 `Blocked` 字段与两处 `PowerCfgProcessRunner` 赋值；`if (converged)` 中保留 `ClearSessionCacheForTests()`，收敛等待与超时失败不变。

```powershell
Get-ChildItem src,Tests -Recurse -Include *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-String -Pattern 'RunPowerCfg|RunOverrideForTests|RunAsyncOverrideForTests'
```

Expected: 无输出。类名 `PowerCfgProcessRunner` 与 `PowerCfgCommandResult` 此后只服务 WMI 探测，名称已不准确；更名涉及 Engine WMI 全链，属范围外，只在设计文档记录。

- [ ] **Step 3: GREEN**

```powershell
foreach ($p in 'Configuration','Engine','Settings','Tray','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task5-green-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

- [ ] **Step 4: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowerCfgProcessRunner.cs Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs Tests/SmartGuard.Engine.Tests/PowerSchemeTestScope.cs Tests/SmartGuard.Settings.Tests/PowerSchemeTestScope.cs Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs
git commit -m "refactor(config): remove powercfg wrappers and gate the whole product" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 契约与设计文档

**Files:** `docs/ARCHITECTURE-CONTRACT.md`、`docs/implementation-plans/2026-11-19-power-scheme-api-migration.md`、`Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs`

- [ ] **Step 1: 门禁 RED**

`Contract_registers_the_scheme_api_invariant_and_owner` 末尾加入：

```csharp
    contract.Should().NotContain("第二阶段前的登记例外", "phase two removed the Settings/Tray exceptions");
    contract.Should().Contain("PowerPlanCatalogSnapshot");
```

Expected: 1 failed。

- [ ] **Step 2: 更新契约**

1. “电源方案操作”行：
   - 允许读者改为：`Engine 守护轮次、首次初始化、Guardian 计划名称、Settings 计划目录、Tray 一键高性能`。
   - 禁止项改为：`任何进程启动 powercfg.exe；在 PowrProfNative 之外导入电源方案函数；API 失败回退外部进程`。
2. `PowerPlanCatalogProvider` 行的说明末尾改为：`Engine 经 TryLoadOnceAsync、Settings 经 LoadWithRetryAsync，均走 PowerSchemeService；每次加载发布不可变的 PowerPlanCatalogSnapshot（Loading/Failed/Loaded 与单方案存在性），UI 只读快照`。
3. S-05 行改为：`Settings 计划目录由 SettingsPlanCatalogCoordinator 以可取消的 LoadWithRetryAsync 在后台加载，UI 线程只读 PowerPlanCatalogSnapshot，并区分正在加载、无法读取列表、名称读取失败与未在本机找到；不再有同步 TryLoad`。
4. `PWR-INV-SCHEME-API` 末尾的“门禁”前补一句：`门禁覆盖全部 src`。

迁移设计文档追加“第二阶段实施与验证记录”标题（内容在 Task 7 填写），并在第 2.2 节表格中把 Settings、Tray 两行的阶段列标为“二（已完成）”。

- [ ] **Step 3: GREEN 与文档校验**

```powershell
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-restore --logger "trx;LogFileName=task6-green.trx" --results-directory .work/power-scheme-api-phase2
foreach ($p in 'docs/ARCHITECTURE-CONTRACT.md','docs/implementation-plans/2026-11-19-power-scheme-api-migration.md') { $b=[IO.File]::ReadAllBytes($p); $null=[Text.UTF8Encoding]::new($false,$true).GetString($b); "$p bom=$($b[0] -eq 0xEF) ok" }
git diff --check -- docs
```

- [ ] **Step 4: 提交**

```powershell
git add -- docs/ARCHITECTURE-CONTRACT.md docs/implementation-plans/2026-11-19-power-scheme-api-migration.md Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs
git commit -m "docs(contract): close the settings and tray powercfg exceptions" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: 全量验证、安装与验收

- [ ] **Step 1: 全量测试**：`Run-Tests.ps1`，核对 summary JSON 四层计数；前后各记录一次本机方案与亮度快照（`task7-machine-state.txt`）。
- [ ] **Step 2: 安装包**：同第一阶段 Task 9 Step 2，记录 SHA256；安装前备份 `D:\Apps\SmartGuard\bin` 与配置到 `.work/power-scheme-api-phase2/original-install`。
- [ ] **Step 3: 用户安装**：确认 UAC；核对 Configuration/Settings/Tray/Engine DLL 与候选一致、配置哈希未变。
- [ ] **Step 4: powercfg 启动监视（证明“没有短命进程”，不靠采样）**

进程快照只能证明采样时刻没有 powercfg，不能排除短命启动。验收期间改用进程启动事件订阅。`Win32_ProcessStartTrace` 需要管理员权限，由用户在管理员 PowerShell 中运行。脚本保存为 `.work/power-scheme-api-phase2/watch-powercfg.ps1`：

```powershell
# Records every powercfg.exe start while it runs. Requires an elevated Windows PowerShell. ASCII only.
# Register-CimIndicationEvent has no -Filter parameter; the condition goes into a full WQL -Query.
# No -Action: an action block does not run while the script blocks in a .NET call, so events are queued
# and written by this loop instead. Any failure stops the script and is recorded in the stop line.
$ErrorActionPreference = 'Stop'
$log = Join-Path $PSScriptRoot 'powercfg-starts.log'
$source = 'SgPowercfgStart'
$query = "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName='powercfg.exe'"

function Write-WatchLine([string]$text)
{
  Add-Content -LiteralPath $log -Encoding utf8 -Value ('{0:o} {1}' -f (Get-Date), $text)
}

function Write-StartEvent($evt)
{
  $e = $evt.SourceEventArgs.NewEvent
  Write-WatchLine ('start pid={0} ppid={1}' -f $e.ProcessID, $e.ParentProcessID)
  Remove-Event -EventIdentifier $evt.EventIdentifier
}

Register-CimIndicationEvent -Query $query -SourceIdentifier $source
$failure = ''
try
{
  # Written only after registration succeeded: without this line the watch never ran.
  Write-WatchLine 'watch-started'
  'Watching powercfg.exe starts; press Q to stop.'
  while ($true)
  {
    $evt = Wait-Event -SourceIdentifier $source -Timeout 1
    if ($evt) { Write-StartEvent $evt }
    if ([Console]::KeyAvailable -and [Console]::ReadKey($true).Key -eq 'Q') { break }
  }
}
catch
{
  $failure = $_.ToString()
  throw
}
finally
{
  foreach ($evt in @(Get-Event -SourceIdentifier $source -ErrorAction SilentlyContinue)) { Write-StartEvent $evt }
  Write-WatchLine ('watch-stopping failure=[{0}]' -f $failure)
  Unregister-Event -SourceIdentifier $source
}
```

（已在本机 Windows PowerShell 5.1 核对：
- `Register-CimIndicationEvent` 的参数集只有 `-ClassName` 与 `-Query` 两类，没有 `-Filter`。
- 以非管理员身份运行上面的注册语句，失败原因为 `HRESULT 0x80041003`（WMI 拒绝访问），不是参数绑定错误。
- 不带 `-Action` 注册、再用 `Wait-Event -Timeout 1` 循环取事件的方式，用计时器事件验证可以取到事件。
- 带 `-Action` 时，事件作业状态在事件触发后仍为 `NotStarted`，不能作为“监视有效”的依据，所以不用。）

监视流程：

1. **启动：** 用户在管理员 Windows PowerShell 中运行脚本。注册失败时脚本因 `-ErrorAction Stop` 直接报错退出，日志中不会有 `watch-started`，此时验收标 `BLOCKED`，**不得**判为“没有启动 powercfg”。
2. **开始阳性对照：** 在另一个终端执行一次只读的 `powercfg /getactivescheme`，确认日志出现一行 `start`，且 `ppid` 为该终端。
3. **验收：** 在监视下完成 Step 5 的全部操作。
4. **结束阳性对照：** 停止监视前再执行一次只读的 `powercfg /getactivescheme`，确认日志再出现一行 `start`。这证明监视在整个验收期间一直有效。
5. **判定：** 只有同时满足以下条件才判为通过：
   - 日志有 `watch-started`；
   - 两次阳性对照各有一行 `start`；
   - `watch-stopping` 行显示 `failure=[]`；
   - 除两次阳性对照外没有其他 `start` 行。

   任一条件不满足：缺少前三项时标 `BLOCKED` 并重做；出现其他 `start` 行时判为失败，按 `ppid` 对照 `Get-Process SmartGuard*` 定位来源。

- [ ] **Step 5: 真实路径验收（需用户配合）：**
  - **打开设置窗口（冷启动，先结束 Settings 进程）：**
    - 窗口立即出现，状态栏先显示“正在加载电源计划...”，随后显示映射结果。
    - 三个下拉框显示本机计划名称（含自定义计划）。
    - 打开、切换下拉框无卡顿。
    - 记录从打开到名称出现的耗时，注明来源是用户观察还是日志时间戳。
  - **托盘“一键高性能”：**
    - 当前为平衡时点击，立即切到高性能，主日志出现“托盘手动切换高性能”。
    - Engine 随后的轮次不报外部变更。
  - **自动化覆盖、真机不可控的路径：** 以下标 `NOT RUN`，由 Task 0.5、1、2、4 的用例覆盖。
    - 目录“无法读取列表”与“名称读取失败”。
    - 方案超过 256 个时列举被截断。
    - 托盘切换超时、错误码。
- [ ] **Step 6: 记录并提交**：
  - 迁移设计追加“第二阶段实施与验证记录”，内容包括：
    - RED/GREEN 证据与基线对照
    - summary 文件名与四层计数
    - 安装哈希
    - 监视日志结论（含阳性对照）
    - 验收结果与 `NOT RUN` 项
  - 提交 `docs(engine): record power scheme api phase two verification`。

---

## 自查记录

- **设计覆盖：**
  - 第 2.2 节 Settings 目录 → Task 1–3。
  - Tray `PowerPlanActivator`（含无超时 `WaitForExit`）→ Task 4。
  - 删除 powercfg 包装与解析器 → Task 3、5。
  - 门禁收紧到全部 `src` → Task 5。
  - 契约 → Task 6。
- **超出设计的范围（已说明理由）：**
  - 列举达到上限时报告截断（Task 0.5）：Task 1 的“确认不存在”结论以完整列表为前提，否则会把上限之外的方案误报为不存在。
  - Settings UI 线程同步加载（新发现 #2–#4）与存在性区分（#7）属于同一目录真源的调用方式。
  - 迁移后若保留同步 `TryLoad`，会把 1.1–1.6 秒的 API 加载留在 UI 线程。
  - 只改成读快照而不区分状态，会把“未加载 / 读不到”显示成“未找到”。
- **RED 合规：**
  - 新成员都先以不改变行为的骨架或边界引入：列举循环 `EnumerateBounded`、快照与存在性、`Build`/`Validate` 快照重载、activator 返回状态与 `UseProcessStarterForTests`。
  - 每个 RED 都因原缺陷失败：
    - 列举用例直接测出达到上限时仍报告成功。
    - UI 线程用例先显示窗口并等待名称出现，再断言线程。
    - 托盘用例先确认切换已开始，再测出旧实现没有等待上界。
  - Task 3 的迁移用例在当前实现上通过，只算回归保护。
- **系统修改边界：**
  - Configuration/Settings 作用域在 Task 1–4 期间封锁 `PowerCfgProcessRunner`；只有确认加载与原生调用都已结束才解除封锁，超时则测试失败、封锁保留。
  - Tray 旧实现绕过该封锁，由 `UseProcessStarterForTests` 换成无害子进程。该边界是 `AsyncLocal`，迟到的被测调用沿用排队时的无害启动；收尾先结束子进程并确认被测调用返回，再释放和复位。
  - 真实写入由 `PowerWriteGuard` 拒绝。
- **测试收敛：** Settings 用例在复位全局钩子前，经 `WhenLoadsIdleForTests` 确认所有加载已发布快照，并确认没有在途原生调用；等待超时即失败，不静默复位。
- **类型一致：**
  - `PowerPlanCatalogState{Loading,Failed,Loaded}`。
  - `PowerPlanPresence{Loading,ListUnavailable,Named,NameUnreadable,ConfirmedAbsent}`。
  - `PowerPlanCatalogSnapshot(State, Names, Listed, ConfirmedAbsent).PresenceOf`。
  - `PowrProfNative.EnumerateBounded(Func<uint, (uint Rc, Guid Scheme)>, uint, out Guid[])`，截断返回 234。
  - `PowerPlanCatalogProvider.{Snapshot, IsKnownScheme, WhenLoadsIdleForTests, LoadBudget, LoadThroughSchemeServiceAsync, FromTestLoader, Publish, PublishFailureUnlessLoaded, LoadAttemptAsync}`。
  - `PowerPlanComboItemsBuilder.Build(PowerPlanCatalogSnapshot, Guid, string?)`、`PowerPlanMappingValidator.Validate(GuardConfig, PowerPlanCatalogSnapshot)`。
  - `IPowerPlanActivator.SetActivePlan(Guid) → PowerSchemeStatus`、`PowerPlanActivator.UseProcessStarterForTests(Func<ProcessStartInfo, Process?>?)`（仅 Task 4 Step 1–5 存在）。
  - `ManualHighPerformanceMessages.FormatActivationDeferred(string)`。
- **已知风险：**
  - Settings 与 Engine 进程各有一个执行器。两进程同时调用电源 API 互不影响门，但由操作系统串行化，单次耗时可能上升；第一阶段基线显示仍在预算内。
  - `Failed_catalog_is_shown…` 中失败加载按现有语义要经历 `3 + 1` 次尝试与 3 次 300ms 间隔，等待上限为 20 秒；超过即视为失败而非放宽。

---

## 外部审查修订记录（2026-11-19）

| # | 审查发现 | 核实结果 | 修订 |
|---|----------|----------|------|
| 1 | 加载状态会误导：全部失败后快照仍为 null、界面一直“正在加载”；部分读名失败被显示为“本机不存在” | 成立（原 Task 1 只发布非空目录；原 Builder 只区分有无名称） | 新增快照状态 `Loading/Failed/Loaded` 与单方案存在性；失败加载不覆盖已有 `Loaded`；新增全部失败、部分读名失败、失败后恢复的 Configuration 与 Settings 用例 |
| 2 | 托盘 RED 绕过原缺陷：骨架整体删除旧实现，RED 只证明骨架返回值不对 | 成立 | 改为保持默认行为的进程启动边界 `StartProcessForTests`；RED 用无害的 30 秒子进程直接测出旧实现无等待上界；GREEN 后在重构步骤删除边界 |
| 3 | 窗口测试存在假绿：后台加载尚未开始时空集合也能通过；清理时未证明后台任务结束 | 成立 | 先等待名称出现，断言加载确实发生；再打开并切换下拉框；关闭后等待加载器在途计数归零，再复位全局钩子 |
| 4 | 迁移用例的预期次数不成立：`maxAttempts: 1` 实际每次调用加载 2 次 | 成立（`PowerPlanCatalogProvider.cs:144-146` 循环后追加一次） | 明确**保留**现有 `maxAttempts + 1` 语义（设计要求重试语义不变）；表格与新用例按此计数；Change Impact Matrix 写明总上界约 21 秒 |
| — | 验收证据：多次进程快照为空只能证明采样时未发现 | 成立 | Task 7 改用 `Win32_ProcessStartTrace` 订阅记录 powercfg 启动，并以一次只读 `powercfg /getactivescheme` 作阳性对照 |

第三阶段相关的发现（#5–#7 与确认成本测试）已记入[子进程清理闩锁设计](2026-11-19-process-cleanup-reopen.md)第 3 版的“实施计划约束”。

第 3 版对上表 #2、#3 的修订方式又做了调整，见下一节。

---

## 复审修订记录（2026-11-20，审查对象为第 2 版 `fd5e492a`）

| # | 复审发现 | 核实结果 | 修订 |
|---|----------|----------|------|
| 1（P1） | 托盘 RED 可能执行真实电源切换：`Task.Run` 若 4 秒内未开始执行，`Restore` 先把启动入口恢复为 `Process.Start`，迟到的任务会真的运行 `powercfg /setactive`；任务已开始时，未等它结束就释放了它正在等待的 `Process` | 成立 | 边界改为 `AsyncLocal`（`UseProcessStarterForTests`），被测调用沿用排队时的无害启动，复位不影响它；先等“切换已开始”的握手（旧实现由无害启动发出，新实现由替身发出），再计 4 秒；收尾顺序固定为释放替身 → 反复结束自有子进程 → 确认被测调用返回 → 释放子进程对象 → 复位边界，被测调用未返回则不释放并判失败。重构后的用例形式也已写出 |
| 2（P1） | 监视脚本无法启动：`Register-CimIndicationEvent` 没有 `-Filter` 参数 | 成立（本机 5.1 命令元数据：无 `Filter`，有 `Query`） | 改用 `-Query` 提交完整 WQL；`-ErrorAction Stop`，注册成功后才写 `watch-started`；验收首尾各做一次阳性对照；停止时记录失败原因；缺少这些证据时标 `BLOCKED`，不得判为“没有启动”。另外发现 `-Action` 块在脚本阻塞于 `[Console]::ReadLine()` 时不会执行，且事件作业状态不可用作证据，改为不带 `-Action` 注册、用 `Wait-Event` 循环写日志。脚本已在本机 5.1 解析通过，非管理员运行时因 WMI 拒绝访问（0x80041003）退出 |
| 3（P2） | 窗口测试未覆盖完整用户路径：窗口未显示，直接改控件属性不能证明下拉框展开；加载器计数归零不代表目录续体、缓存发布与 Dispatcher 回调结束；Settings 作用域超时后静默复位 | 成立 | 用 `GetWindowField` 加 `ShowAndWait` 显示真实窗口，等待下拉框展开并生成项容器，再经 `window.Close()` 关闭；新增测试屏障 `WhenLoadsIdleForTests`（加载门空闲即所有加载已发布快照），收尾等待屏障并处理 Dispatcher 队列；`WithNativeSync` 确认加载与原生调用都结束才复位，超时即失败且保留全局钩子。Configuration 作用域同样改为确认空闲后才解除 powercfg 封锁 |
| 4（P2） | 删除说明自相矛盾：同一缓存复用用例既要删除又被列为替代覆盖，且它用的是 `LoadImplementationForTests`；新并发用例只覆盖可缓存目录 | 成立 | 只删除两个经 `RunAsyncOverrideForTests` 的用例，明确保留 `reuses_completed_catalog…`；并发用例改为“目录不可缓存”的情形，断言 4 次列举。没有加载门时，第二个调用会撞上执行器的单飞门而返回 `call-pending`，次数少于 4，所以该断言能区分有无串行 |
| 5（P2） | “未列举即确认不存在”缺少完整性前提：原生列举读满 256 项仍返回成功 | 成立（`PowrProfNative.cs:55-69`） | 新增 Task 0.5：保留上限，抽出可测的 `EnumerateBounded`，上限之外仍有方案时返回 `ERROR_MORE_DATA`（234），调用方按列举失败处理，显示“无法读取计划列表”；补上限边界用例。不增加“是否完整”标志的理由见 Task 0.5 |
| 技术说明 | 通用约束把 5.1 中文误读归因于 LF 不准确，根因是 BOM 与编码识别，CRLF 不能解决编码问题 | 成立 | 通用约束改写：根因是 5.1 按 ANSI 代码页解码无 BOM 的 UTF-8；CRLF 只防止误解码吞掉换行导致解析失败；按项目规则不加 BOM，新增脚本只用 ASCII |
