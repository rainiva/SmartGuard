# 电源方案 API 迁移 — 第二阶段（Settings / Tray）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**状态：** 草案，待外部审查与用户批准后实施。设计见 [2026-11-19-power-scheme-api-migration.md](2026-11-19-power-scheme-api-migration.md) 第 2.2 节“阶段二”，第一阶段记录见同文档末尾。

**Goal:** Settings 计划目录与 Tray“一键高性能”经 `PowerSchemeService` 调用 Windows 电源 API；删除 powercfg 包装与文本解析；整个产品不再启动 `powercfg.exe`，且 Settings 窗口不在 UI 线程上加载计划目录。

**Architecture:** `PowerPlanCatalogProvider` 的 Guardian 与 Settings 两条加载路径收敛为同一个经 `PowerSchemeService` 的私有核心（列举 + 读名，共享总预算），并新增只读快照 `LoadedSnapshot` 供 UI 使用，UI 不再触发任何加载 IO。Tray 的 `PowerPlanActivator` 改调 `PowerSchemeService.SetActiveAsync`（2 秒截止），失败时由守护程序按已写入的手动高性能期限接管。`PowerCfgProcessRunner` 只保留 WMI 探测使用的 `RunProcess`/`RunProcessAsync`，架构门禁从 Engine 扩展到全部 `src`。

**Tech Stack:** .NET 8、C#、WPF（Settings）、WinForms（Tray）、xUnit 2.5.3、FluentAssertions 8。

**证据目录：** `.work/power-scheme-api-phase2/`。

---

## 背景与新发现（调研 2026-11-19）

| # | 事实 | 位置 | 影响 |
|---|------|------|------|
| 1 | Tray“一键高性能”启动 `powercfg /setactive` 后 `WaitForExit()` **无超时** | `src/SmartGuard.Configuration/PowerPlanActivator.cs` | powercfg 卡住时托盘线程池任务永不结束，菜单项保持禁用 |
| 2 | Settings 打开窗口时，`ApplyInitialValues → ApplyPlanCombosToUi` 在 **UI 线程**上调用 3 次 `PopulatePlanCombo` 与 1 次 `UpdatePlanMappingStatus`，每次调用同步 `PowerPlanCatalogProvider.TryLoad()` | `SettingsPlanCatalogCoordinator.cs:81,187`；`SettingsPolicyCoordinator.cs:244`；`SettingsWindowController.cs:162-165` | 缓存为空时在 UI 线程执行 `powercfg /list` 与至多 3 次 `/query`（每次 15 秒超时）；缺少节能方案的机器每次重新填充都会重复，因为不完整目录不缓存。违反 AGENTS §5“UI 线程禁止大文件 IO、全量扫描和长计算” |
| 3 | 契约 S-05 写“Settings 只经可取消的 `LoadWithRetryAsync`”，`SettingsPlanCatalogArchitectureTests` 却**要求**协调器包含 `PowerPlanCatalogProvider.TryLoad()` | `docs/ARCHITECTURE-CONTRACT.md` S-05；`Tests/SmartGuard.Architecture.Tests/SettingsPlanCatalogArchitectureTests.cs` | 契约与门禁互相矛盾，本计划以契约为准并修正门禁 |
| 4 | 异步加载完成后 `ApplyPlanCatalog(catalog)` 丢弃参数（`_ = catalog;`），再经同步 `TryLoad()` 重新取目录 | `SettingsPlanCatalogCoordinator.cs:114-119` | 同上，不完整目录时在 UI 线程重新加载 |
| 5 | 同步 `TryLoad`/`LoadWithRetry`/`EnrichWithHiddenSchemes` 与 `PowerPlanCatalogParser` 在生产中只剩 Settings 这两处调用 | 全仓 grep | 迁移 UI 后可整体删除 |

---

## Change Impact Matrix（PROJECT-RULES 高影响改动）

| 维度 | 影响 |
|------|------|
| Function | Settings 计划下拉框与映射状态改读快照：加载完成前显示“（正在加载）”，不再显示“未在本机找到”的假结论；Tray 切换失败不再无限等待 |
| Owner / 真源 | 目录真源仍是 `PowerPlanCatalogProvider`（M-04、S-05）；电源方案调用唯一入口 `PowerSchemeService`；不新增 Owner |
| Concurrency & Time | Settings 加载保留单槽 `AsyncLoadGate`；每次尝试列举 + 读名共享 5 秒总预算；Tray 切换截止 2 秒；单飞执行器每进程一个（Settings、Tray 各自进程） |
| Performance | UI 线程目录 IO 从“最坏 4×15 秒”降为 0；后台加载实测约 1.1–1.6 秒/次（第一阶段基线），重试语义不变（至多 3 次 + 300ms 间隔） |
| Failure | 目录加载失败：快照保持上次值或 null，状态栏保持“正在加载电源计划...”直到有结果；Tray 切换失败：配置已写入手动高性能，日志说明由守护程序在下一轮切换 |
| Compatibility | `IPowerPlanActivator`、`HighPerformanceBoost` 由 public 改为 internal（调用方只有 Tray，已有 IVT）；删除 public 的 `TryLoad`、`LoadWithRetry`、`EnrichWithHiddenSchemes`、`PowerPlanCatalogParser`（仓内无生产调用方） |
| Observability | Tray 失败写入启动日志一行中文说明；Settings 加载不新增日志 |
| Resource | 无新线程、队列、定时器；Settings/Tray 进程各多一个执行器实例（静态，惰性） |

---

## 文件结构

| 文件 | 动作 | 责任 |
|------|------|------|
| `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs` | 修改 | 共享加载核心、`LoadedSnapshot`；删除同步与 powercfg 路径 |
| `src/SmartGuard.Configuration/PowerPlanCatalogParser.cs` | 删除 | powercfg 文本解析 |
| `src/SmartGuard.Configuration/IPowerPlanActivator.cs`、`PowerPlanActivator.cs`、`HighPerformanceBoost.cs` | 修改 | 经 `PowerSchemeService` 切换、返回状态、失败说明 |
| `src/SmartGuard.Configuration/ManualHighPerformanceMessages.cs` | 修改 | 失败文案 |
| `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs` | 修改 | 删除 powercfg 包装与覆盖钩子，保留 `RunProcess`/`RunProcessAsync` |
| `src/SmartGuard.Settings/SettingsPlanCatalogCoordinator.cs` | 修改 | UI 只读 `LoadedSnapshot` |
| `src/SmartGuard.Settings/PowerPlanComboItemsBuilder.cs` | 修改 | 未加载时的占位文案 |
| `Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs` | 修改 | Task 1 起封锁旧 powercfg 入口，Task 5 随钩子删除 |
| `Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs` | 修改 | 迁移旧同步与 powercfg 覆盖用例 |
| `Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogParserTests.cs` | 删除 | 随解析器删除 |
| `Tests/SmartGuard.Configuration.Tests/LiveCatalogProbeTests.cs` | 修改 | 改用 `LoadWithRetryAsync` |
| `Tests/SmartGuard.Configuration.Tests/PowerPlanActivatorTests.cs` | 新建 | 切换经执行器、失败状态 |
| `Tests/SmartGuard.Configuration.Tests/HighPerformanceBoostTests.cs` | 修改 | 失败路径 |
| `Tests/SmartGuard.Configuration.Tests/PowerCfgProcessRunnerTests.cs` | 不变 | 只覆盖 `RunProcess*`，保持通过 |
| `Tests/SmartGuard.Engine.Tests/PowerSchemeTestScope.cs` | 修改 | Task 5 删除 powercfg 封锁 |
| `Tests/SmartGuard.Settings.Tests/SettingsWindowControllerTests.cs`、`SettingsPlanCatalogCoordinatorTests.cs`、`PowerPlanComboItemsBuilderTests.cs` | 修改 | UI 线程不加载、占位文案、状态栏 |
| `Tests/SmartGuard.Architecture.Tests/SettingsPlanCatalogArchitectureTests.cs` | 修改 | 修正与 S-05 矛盾的断言 |
| `Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs` | 修改 | 门禁扩展到全部 `src` |
| `docs/ARCHITECTURE-CONTRACT.md`、迁移设计 | 修改 | 删除登记例外、更新 S-05、记录第二阶段 |

**通用约束：** 同第一阶段（UTF-8 无 BOM、精确补丁、只暂存本任务文件、Conventional Commits 加 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`；每个 RED 前先 `dotnet build` 受影响测试项目并确认编译状态；RED 必须因行为缺口失败；测试只读真实电源设置，真实写入由 `PowerWriteGuard` 拒绝）。受影响测试项目：Configuration、Engine、Settings、Tray、Architecture。

---

### Task 0: 基线

- [ ] **Step 1: 记录基线**

```powershell
New-Item -ItemType Directory -Force .work\power-scheme-api-phase2 | Out-Null
foreach ($p in 'Configuration','Engine','Settings','Tray','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task0-baseline-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

Expected: 全部通过，记录各项目通过/跳过数（Settings 含 WpfUi 用例，耗时约 2 分钟）。

---

### Task 1: 共享加载核心与只读快照

**Files:**
- Modify: `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs`
- Modify: `Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs`
- Modify: `Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs`

- [ ] **Step 1: Configuration 作用域封锁旧 powercfg 入口**

`PowerSchemeTestScope.WithNative` 中，`UseExecutorForTests(...)` 之后加入两行封锁，`finally` 内层的复位处加入两行复位：

```csharp
    PowerSchemeService.UseExecutorForTests(new PowerSchemeExecutor(native, deadline, scheduler));
    // RED may still fall into the legacy powercfg catalog path; block it instead of spawning powercfg.exe.
    PowerCfgProcessRunner.RunOverrideForTests = (_, _) => Blocked;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(Blocked);
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
```

```csharp
        PowerSchemeService.UseExecutorForTests(null);
        PowerCfgProcessRunner.RunOverrideForTests = null;
        PowerCfgProcessRunner.RunAsyncOverrideForTests = null;
        PowerPlanCatalogProvider.ClearSessionCacheForTests();
```

并在类内加入：

```csharp
  private static readonly PowerCfgCommandResult Blocked =
    new(false, false, null, "", "powercfg is blocked inside PowerSchemeTestScope") { FailureKind = "start-failed" };
```

- [ ] **Step 2: 写失败测试**（追加到 `PowerPlanCatalogProviderTests` 类末尾）

```csharp
    [Fact]
    public async Task LoadWithRetryAsync_reads_names_through_the_scheme_service()
    {
        var custom = Guid.Parse("56c8d9b8-d34d-4d19-96a4-c177cf5f4882");
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid, custom,
                PowerPlanCatalogProvider.HighPerformancePlanGuid, PowerPlanCatalogProvider.PowerSaverPlanGuid]),
            ReadName = guid => (0, guid == custom ? "自定义模式" : guid.ToString("N")),
        };
        IReadOnlyDictionary<Guid, string> catalog = new Dictionary<Guid, string>();
        await PowerSchemeTestScope.WithNative(native, async () =>
            catalog = await PowerPlanCatalogProvider.LoadWithRetryAsync());
        catalog.Should().ContainKey(custom).WhoseValue.Should().Be("自定义模式");
        native.Calls.Count(call => call == "enumerate").Should().Be(1, "a complete catalog needs no retry");
    }

    [Fact]
    public async Task Loaded_snapshot_holds_the_last_load_even_when_a_known_scheme_is_absent()
    {
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid, PowerPlanCatalogProvider.HighPerformancePlanGuid]),
            ReadName = guid => guid == PowerPlanCatalogProvider.PowerSaverPlanGuid ? (2u, string.Empty) : (0u, "n"),
        };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            PowerPlanCatalogProvider.LoadedSnapshot.Should().BeNull("nothing has been loaded in this scope");
            var catalog = await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            PowerPlanCatalogProvider.LoadedSnapshot.Should().BeSameAs(catalog,
                "the UI must see an incomplete but real catalog instead of reloading it on the UI thread");
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
    public async Task LoadWithRetryAsync_serializes_concurrent_loads_through_one_enumeration()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var native = new FakePowrProfNative
        {
            Enumerate = () =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return (0, [PowerPlanCatalogProvider.BalancedPlanGuid, PowerPlanCatalogProvider.HighPerformancePlanGuid,
                    PowerPlanCatalogProvider.PowerSaverPlanGuid]);
            },
        };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            var first = PowerPlanCatalogProvider.LoadWithRetryAsync();
            entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            var second = PowerPlanCatalogProvider.LoadWithRetryAsync();
            release.Set();
            (await second).Should().BeSameAs(await first, "the second caller reuses the completed catalog");
        });
        native.Calls.Count(call => call == "enumerate").Should().Be(1);
    }
```

（`LoadedSnapshot` 的 null 断言依赖作用域在进入时清空快照：Step 4 的 `ClearSessionCacheForTests` 同时清空 `_loadedSnapshot`。）

- [ ] **Step 3: 骨架并取 RED**

`PowerPlanCatalogProvider` 中加入（不改变现有行为）：

```csharp
  /// <summary>UI-safe: never performs IO. Scaffold only.</summary>
  public static IReadOnlyDictionary<Guid, string>? LoadedSnapshot => null;
```

```powershell
foreach ($p in 'Configuration','Engine','Settings','Tray','Architecture') { dotnet build "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore -v q; "$p build exit=$LASTEXITCODE" }
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-build --filter FullyQualifiedName~PowerPlanCatalogProviderTests --logger "trx;LogFileName=task1-red.trx" --results-directory .work/power-scheme-api-phase2
```

Expected: 编译退出码均为 0。4 个新用例的预期结果：

- `reads_names…`、`serializes…` 失败：旧路径走 powercfg，在作用域内被封锁，替身未被调用。
- `Loaded_snapshot…` 失败：骨架快照恒为 null。
- `propagates_cancellation…` 可能通过：封锁后旧路径立即返回空目录，之后在 300ms 重试等待中取消。若通过，这是保护性用例，如实记录；`native.Calls` 断言会使它失败，以实际结果为准。

旧用例照常通过。

- [ ] **Step 4: 实现**

(a) 字段与快照（替换 Step 3 的骨架）：

```csharp
  private static volatile IReadOnlyDictionary<Guid, string>? _loadedSnapshot;

  /// <summary>
  /// UI-safe snapshot: never performs IO. The latest catalog any load produced (complete or not); null
  /// before the first load in this process. Settings reads only this; loads run on background tasks.
  /// </summary>
  public static IReadOnlyDictionary<Guid, string>? LoadedSnapshot => _loadedSnapshot;
```

(b) `GuardianLoadBudget` 更名为 `LoadBudget`（两条路径共享，5 秒），`TryLoadOnceAsync` 中的引用同步改名。

(c) 把 `TryLoadOnceAsync` 改为调用共享核心，并新增核心与发布方法：

```csharp
  // Guardian-only entry (PWR-INV-SCHEME-API).
  internal static async Task<IReadOnlyDictionary<Guid, string>> TryLoadOnceAsync(CancellationToken cancellationToken,
    Action<PowerSchemeStatus>? observe = null, TimeSpan? budget = null)
  {
    if (_sessionCache is not null) return _sessionCache;
    if (LoadImplementationForTests is { } loader) return loader();
    var (catalog, complete) = await LoadThroughSchemeServiceAsync(cancellationToken, observe, budget ?? LoadBudget)
      .ConfigureAwait(false);
    Publish(catalog, complete);
    return catalog;
  }

  // One load through PowerSchemeService: enumeration and every name read share one total budget, so the
  // chain is bounded regardless of the scheme count. Only a confirmed absence (Win32 2) of a hidden known
  // scheme is normal; any other failure makes the result incomplete.
  private static async Task<(IReadOnlyDictionary<Guid, string> Catalog, bool Complete)> LoadThroughSchemeServiceAsync(
    CancellationToken cancellationToken, Action<PowerSchemeStatus>? observe, TimeSpan budget)
  {
    var catalog = new Dictionary<Guid, string>();
    using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    total.CancelAfter(budget);
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    var complete = false;
    try
    {
      var schemes = await PowerSchemeService.EnumerateSchemesAsync(total.Token).ConfigureAwait(false);
      observe?.Invoke(schemes.Status);
      if (!schemes.Succeeded) return (catalog, false);

      var listed = schemes.Value!;
      complete = true;
      foreach (var guid in listed.Concat(KnownSchemeGuids.Where(known => !listed.Contains(known))))
      {
        var name = await PowerSchemeService.ReadNameAsync(guid, total.Token).ConfigureAwait(false);
        if (name.Succeeded)
        {
          if (!string.IsNullOrWhiteSpace(name.Value)) catalog[guid] = name.Value!;
          continue;
        }
        var confirmedAbsent = !listed.Contains(guid) && name.Status is { FailureKind: "api-error", Win32Error: 2 };
        if (confirmedAbsent) continue;
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
    return (catalog, complete);
  }

  private static void Publish(IReadOnlyDictionary<Guid, string> catalog, bool complete)
  {
    if (catalog.Count > 0) _loadedSnapshot = catalog;
    if (complete && catalog.Count > 0 && !HasMissingKnownSchemes(catalog)) _sessionCache = catalog;
  }
```

(d) `LoadWithRetryAsync` 中两处 `await TryLoadAsync(cancellationToken)` 改为 `await LoadAttemptAsync(cancellationToken)`，并新增：

```csharp
  // One Settings attempt: same core as the guardian; a failing test loader degrades to an empty catalog.
  private static async Task<IReadOnlyDictionary<Guid, string>> LoadAttemptAsync(CancellationToken cancellationToken)
  {
    if (_sessionCache is not null) return _sessionCache;
    IReadOnlyDictionary<Guid, string> catalog;
    var complete = true;
    if (LoadImplementationForTests is { } loader)
    {
      try { catalog = await Task.Run(loader, cancellationToken).ConfigureAwait(false); }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
      catch { catalog = new Dictionary<Guid, string>(); complete = false; }
    }
    else
    {
      (catalog, complete) = await LoadThroughSchemeServiceAsync(cancellationToken, null, LoadBudget).ConfigureAwait(false);
    }
    cancellationToken.ThrowIfCancellationRequested();
    Publish(catalog, complete);
    return catalog;
  }
```

(e) `ClearSessionCacheForTests` 增加 `_loadedSnapshot = null;`。

同步 `TryLoad`、`LoadWithRetry`、`LoadCore`、`TryLoadAsync`、`LoadCoreAsync` 等本任务**暂不删除**（Task 3 处理），`TryLoadAsync` 此后无调用方。

- [ ] **Step 5: GREEN**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --logger "trx;LogFileName=task1-green.trx" --results-directory .work/power-scheme-api-phase2
dotnet test Tests/SmartGuard.Engine.Tests/SmartGuard.Engine.Tests.csproj --no-restore --logger "trx;LogFileName=task1-green-engine.trx" --results-directory .work/power-scheme-api-phase2
dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj --no-restore --logger "trx;LogFileName=task1-green-settings.trx" --results-directory .work/power-scheme-api-phase2
```

Expected: 全部通过。已知需复核的旧用例：`LoadWithRetryAsync_propagates_cancellation_to_powercfg`、`LoadWithRetryAsync_serializes_concurrent_powercfg_loaders`、`LoadWithRetryAsync_reuses_completed_catalog_for_concurrent_callers` 使用 `RunAsyncOverrideForTests`，新实现不再调用 powercfg，它们会失败。**删除这三个旧用例**，由 Step 2 的三个新用例取代（覆盖相同的取消、串行与复用语义），并在提交说明中列明。

- [ ] **Step 6: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs
git commit -m "feat(config): load settings plan catalog through the scheme service with a ui snapshot" -m "Replaces the powercfg-based concurrency tests with scheme-service equivalents." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Settings UI 只读快照

**Files:**
- Modify: `src/SmartGuard.Settings/SettingsPlanCatalogCoordinator.cs:81,187`
- Modify: `src/SmartGuard.Settings/PowerPlanComboItemsBuilder.cs`
- Modify: `Tests/SmartGuard.Settings.Tests/SettingsWindowControllerTests.cs`、`Tests/SmartGuard.Settings.Tests/PowerPlanComboItemsBuilderTests.cs`、`Tests/SmartGuard.Architecture.Tests/SettingsPlanCatalogArchitectureTests.cs`

- [ ] **Step 1: 写失败测试**

`SettingsWindowControllerTests` 中，在 `TryCreate_returns_before_slow_plan_catalog_load_completes` 之后加入：

```csharp
    [Fact]
    public void TryCreate_never_loads_the_plan_catalog_on_the_ui_thread()
    {
        RunOnSta(() =>
        {
            var uiThread = Environment.CurrentManagedThreadId;
            var loaderThreads = new System.Collections.Concurrent.ConcurrentBag<int>();
            PowerPlanCatalogProvider.ClearSessionCacheForTests();
            PowerPlanCatalogProvider.LoadImplementationForTests = () =>
            {
                loaderThreads.Add(Environment.CurrentManagedThreadId);
                // Incomplete on purpose: an incomplete catalog is never cached, which is what made the
                // old synchronous path reload on every combo refresh.
                return new Dictionary<Guid, string> { [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡" };
            };

            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsUiCatalog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            SettingsWindowController? controller = null;
            try
            {
                var repository = new GuardConfigRepository(Path.Combine(tempRoot, "SmartGuard.config.json"));
                controller = SettingsWindowController.TryCreate(tempRoot, repository, repository.LoadOrDefault(tempRoot));
                controller.Should().NotBeNull();
                loaderThreads.Should().NotContain(uiThread, "catalog loads run on background tasks only");
            }
            finally
            {
                controller?.Dispose();
                PowerPlanCatalogProvider.ClearSessionCacheForTests();
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }
```

`PowerPlanComboItemsBuilderTests` 中加入：

```csharp
  [Fact]
  public void Selected_plan_is_labelled_loading_before_the_catalog_arrives()
  {
    var selected = PowerPlanCatalogProvider.HighPerformancePlanGuid;
    var items = PowerPlanComboItemsBuilder.Build(null, selected, "高性能");
    items.Should().ContainSingle().Which.DisplayName.Should().Be("高性能（正在加载）",
      "an absent snapshot is not evidence that the plan is missing on this machine");
  }
```

`SettingsPlanCatalogArchitectureTests` 中把

```csharp
    catalog.Should().Contain("PowerPlanCatalogProvider.TryLoad()");
```

改为

```csharp
    // S-05: the UI reads the IO-free snapshot; loads only run through the cancelable async path.
    catalog.Should().Contain("PowerPlanCatalogProvider.LoadedSnapshot");
    catalog.Should().NotContain("PowerPlanCatalogProvider.TryLoad()");
```

- [ ] **Step 2: RED**

```powershell
foreach ($p in 'Settings','Architecture') { dotnet build "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore -v q; "$p build exit=$LASTEXITCODE" }
```

Expected: Settings 编译失败（`Build(null, …)` 无法把 null 传给不可空参数）。先在 `PowerPlanComboItemsBuilder.Build` 把第一个参数改为 `IReadOnlyDictionary<Guid, string>? catalog`，方法开头加入 `catalog ??= new Dictionary<Guid, string>();`，行为不变，再重新编译并取 RED：

```powershell
dotnet test Tests/SmartGuard.Settings.Tests/SmartGuard.Settings.Tests.csproj --no-build --filter "FullyQualifiedName~TryCreate_never_loads_the_plan_catalog_on_the_ui_thread|FullyQualifiedName~Selected_plan_is_labelled_loading" --logger "trx;LogFileName=task2-red-settings.trx" --results-directory .work/power-scheme-api-phase2
dotnet test Tests/SmartGuard.Architecture.Tests/SmartGuard.Architecture.Tests.csproj --no-build --filter FullyQualifiedName~SettingsPlanCatalogArchitectureTests --logger "trx;LogFileName=task2-red-arch.trx" --results-directory .work/power-scheme-api-phase2
```

Expected: 3 例失败。

- UI 线程用例：`PopulatePlanCombo` 经同步 `TryLoad()` 在 STA 线程调用加载器。如果测试窗口未加载下拉框导致加载器根本未被调用，该用例会意外通过；这时在 RED 记录中写明，并改为断言 `loaderThreads` 非空，证明后台加载确实发生，以确定测试真实触达路径。
- 占位文案用例：仍为“未在本机找到”。
- 架构用例：仍含 `TryLoad()`。

- [ ] **Step 3: 实现**

`PowerPlanComboItemsBuilder.Build`：

```csharp
  public static IReadOnlyList<PowerPlanComboItem> Build(
    IReadOnlyDictionary<Guid, string>? catalog,
    Guid selectedGuid,
    string? orphanRoleLabel = null)
  {
    // A null catalog means "not loaded yet", never "missing on this machine".
    var loaded = catalog is not null;
    catalog ??= new Dictionary<Guid, string>();
    var items = catalog
      .Select(entry => new PowerPlanComboItem
      {
        PlanGuid = entry.Key,
        DisplayName = entry.Value,
      })
      .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
      .ToList();

    if (selectedGuid != Guid.Empty && !catalog.ContainsKey(selectedGuid))
    {
      var suffix = loaded ? "（未在本机找到）" : "（正在加载）";
      var displayName = string.IsNullOrWhiteSpace(orphanRoleLabel)
        ? $"{selectedGuid}{suffix}"
        : $"{orphanRoleLabel}{suffix}";
      items.Insert(0, new PowerPlanComboItem
      {
        PlanGuid = selectedGuid,
        DisplayName = displayName,
      });
    }

    return items;
  }
```

`SettingsPlanCatalogCoordinator.UpdatePlanMappingStatus` 第 81 行起替换为：

```csharp
    config ??= _readConfigFromUi();
    var snapshot = PowerPlanCatalogProvider.LoadedSnapshot;
    if (snapshot is null)
    {
      _lblPlanMappingStatus.Text = "正在加载电源计划...";
      return;
    }
    var messages = PowerPlanMappingValidator.Validate(config, snapshot);
```

第 187 行：

```csharp
      var items = PowerPlanComboItemsBuilder.Build(PowerPlanCatalogProvider.LoadedSnapshot, selectedGuid, orphanRoleLabel);
```

`ApplyPlanCatalog(catalog)` 保持不变：加载在发布快照之后才回调，`RepopulatePlanCombos` 读到的即为本次结果。

- [ ] **Step 4: GREEN**

```powershell
foreach ($p in 'Configuration','Settings','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task2-green-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

Expected: 全部通过。若 Settings 中有用例断言窗口打开后立即出现目录名称，它依赖的正是 UI 线程同步加载，改为等待 `BeginLoadPlanCatalogAsync` 完成后断言（不放宽为“任意结果”）。

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Settings/SettingsPlanCatalogCoordinator.cs src/SmartGuard.Settings/PowerPlanComboItemsBuilder.cs Tests/SmartGuard.Settings.Tests/SettingsWindowControllerTests.cs Tests/SmartGuard.Settings.Tests/PowerPlanComboItemsBuilderTests.cs Tests/SmartGuard.Architecture.Tests/SettingsPlanCatalogArchitectureTests.cs
git commit -m "fix(settings): never load the plan catalog on the ui thread" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 删除同步与 powercfg 目录路径

**Files:**
- Modify: `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs`
- Delete: `src/SmartGuard.Configuration/PowerPlanCatalogParser.cs`、`Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogParserTests.cs`
- Modify: `Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogProviderTests.cs`、`Tests/SmartGuard.Configuration.Tests/LiveCatalogProbeTests.cs`

- [ ] **Step 1: 迁移依赖同步入口的用例（先改测试）**

`PowerPlanCatalogProviderTests` 中以下用例使用同步 `TryLoad`/`LoadWithRetry`，改为等价的异步断言（语义不变，加载器仍经 `LoadImplementationForTests`）：

| 旧用例 | 改写 |
|--------|------|
| `TryLoad_reuses_session_cache_without_calling_loader_again` | 两次 `await LoadWithRetryAsync()`，加载器调用 1 次 |
| `TryLoad_retries_after_loader_failure_without_caching_empty_catalog` | 加载器第一次抛异常、第二次返回完整目录；`await LoadWithRetryAsync(maxAttempts: 2)` 返回完整目录，调用 2 次 |
| `TryLoad_does_not_cache_empty_catalog` | 加载器返回空；连续两次 `LoadWithRetryAsync(maxAttempts: 1)`，调用 2 次 |
| `TryLoad_does_not_cache_incomplete_catalog` | 加载器返回缺节能方案的目录；连续两次 `maxAttempts: 1`，调用 2 次，且 `LoadedSnapshot` 为该目录 |
| `LoadWithRetry_returns_complete_catalog_from_loader` | `await LoadWithRetryAsync(maxAttempts: 2)` |

每个改写用例的代码按下例结构写出（以第一行为例）：

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

- [ ] **Step 2: 运行确认迁移后的用例在旧实现上通过**

```powershell
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-restore --filter "FullyQualifiedName~PowerPlanCatalogProviderTests|FullyQualifiedName~LiveCatalogProbeTests" --logger "trx;LogFileName=task3-migrated.trx" --results-directory .work/power-scheme-api-phase2
```

Expected: 全部通过（迁移不改变行为，属回归保护，不冒称 RED）。

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

从 `PowerPlanCatalogProvider` 删除：`TryLoad`、`LoadWithRetry`、`EnrichWithHiddenSchemes`、`LoadCore`、`TryLoadAsync`、`LoadCoreAsync`、`EnrichWithHiddenSchemesAsync`、`RunPowerCfg`、`TryQueryPlanName`、`TryQueryPlanNameAsync`，以及不再使用的 `using System.Diagnostics;`、`System.Globalization`、`System.Text`（以编译为准）。

```powershell
git rm -q -- src/SmartGuard.Configuration/PowerPlanCatalogParser.cs Tests/SmartGuard.Configuration.Tests/PowerPlanCatalogParserTests.cs
Get-ChildItem src,Tests -Recurse -Include *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-String -Pattern 'PowerPlanCatalogProvider\.(TryLoad\(\)|LoadWithRetry\(|EnrichWithHiddenSchemes)|PowerPlanCatalogParser'
```

Expected: 第二条只剩 `PowerPlanCatalogSingleSourceTests`、`GuardianStartupCatalogTests` 与 Task 2 改写后架构测试中的 `NotContain` 断言。

`PowerPlanCatalogParserTests` 中 `EnrichWithHiddenSchemes` 的覆盖语义（补读隐藏的内置方案）已由第一阶段 `TryLoadOnceAsync_reads_listed_and_hidden_known_names…` 与本计划 Task 1 用例覆盖。

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

- [ ] **Step 1: 写失败测试**

`Tests/SmartGuard.Configuration.Tests/PowerPlanActivatorTests.cs`：

```csharp
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("PowerPlanCatalogProvider")]
public class PowerPlanActivatorTests
{
  private static readonly Guid High = PowerPlanCatalogProvider.HighPerformancePlanGuid;

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
  public async Task Activation_past_its_deadline_returns_instead_of_waiting_forever()
  {
    using var release = new ManualResetEventSlim();
    var native = new FakePowrProfNative { SetActive = _ => { release.Wait(TimeSpan.FromSeconds(10)); return 0; } };
    PowerSchemeStatus status = default;
    await PowerSchemeTestScope.WithNative(native, () =>
    {
      try { status = new PowerPlanActivator().SetActivePlan(High); }
      finally { release.Set(); }
      return Task.CompletedTask;
    }, deadline: TimeSpan.FromMilliseconds(100));
    status.FailureKind.Should().Be("timed-out", "the tray must never wait without a bound for a power switch");
  }
}
```

`HighPerformanceBoostTests`：把 `RecordingPowerPlanActivator` 改为实现新接口，并新增失败路径用例：

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

- [ ] **Step 2: 骨架并取 RED**

把接口与实现改为返回状态（行为暂不变）：

```csharp
namespace SmartGuard.Configuration;

internal interface IPowerPlanActivator
{
  PowerSchemeStatus SetActivePlan(Guid planGuid);
}
```

`PowerPlanActivator` 改为 `internal sealed class`，方法体**整体替换**为 `return new PowerSchemeStatus("api-error", null, 0);`：
- 旧实现直接 `Process.Start("powercfg.exe", "/setactive …")`，不经 `PowerCfgProcessRunner`，作用域封锁对它无效。
- 若骨架保留旧方法体，RED 会在本机真实执行一次切换。

`HighPerformanceBoost` 改为 `internal static class`，调用处改为 `_ = activator.SetActivePlan(config.ActivePlanGuid);`。

```powershell
powercfg /getactivescheme | Out-File -Encoding utf8 .work/power-scheme-api-phase2/task4-red-machine-state.txt
foreach ($p in 'Configuration','Tray','Architecture') { dotnet build "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore -v q; "$p build exit=$LASTEXITCODE" }
dotnet test Tests/SmartGuard.Configuration.Tests/SmartGuard.Configuration.Tests.csproj --no-build --filter "FullyQualifiedName~PowerPlanActivatorTests|FullyQualifiedName~HighPerformanceBoostTests" --logger "trx;LogFileName=task4-red.trx" --results-directory .work/power-scheme-api-phase2
powercfg /getactivescheme | Out-File -Append -Encoding utf8 .work/power-scheme-api-phase2/task4-red-machine-state.txt
```

Expected: 编译通过（Tray 经 IVT 访问 internal 类型）。3 例失败：

- `Activation_switches…`：骨架未调用替身，`native.Calls` 为空。
- `Activation_past_its_deadline…`：骨架立即返回 `api-error`，不是 `timed-out`。
- `Failed_activation…`：骨架返回的失败被丢弃，日志中没有说明。

原用例 `Apply_sets_manual_boost…` 通过。前后两次 `getactivescheme` 一致。

- [ ] **Step 3: 实现**

`PowerPlanActivator.cs`：

```csharp
namespace SmartGuard.Configuration;

/// <summary>Tray manual switch through PowerSchemeService (PWR-INV-SCHEME-API); bounded by its deadline.</summary>
internal sealed class PowerPlanActivator : IPowerPlanActivator
{
  public PowerSchemeStatus SetActivePlan(Guid planGuid)
    => PowerSchemeService.SetActiveAsync(planGuid).GetAwaiter().GetResult().Status;
}
```

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
  private static string DescribeActivationFailure(PowerSchemeStatus status) => status.FailureKind switch
  {
    "timed-out" => "电源服务调用超时",
    "call-pending" => "上次电源服务调用仍未返回",
    _ => status.Win32Error is int code ? $"电源服务调用失败（错误码 {code}）" : "电源服务调用失败",
  };
```

（文案与 Engine `PowerCfgExecutor.DescribeFailure` 一致；Configuration 不能引用 Engine，故在调用点按同一映射给出，文案由本用例锁定。）

- [ ] **Step 4: GREEN**

```powershell
foreach ($p in 'Configuration','Tray','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task4-green-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

- [ ] **Step 5: 提交**

```powershell
git add -- src/SmartGuard.Configuration/IPowerPlanActivator.cs src/SmartGuard.Configuration/PowerPlanActivator.cs src/SmartGuard.Configuration/HighPerformanceBoost.cs src/SmartGuard.Configuration/ManualHighPerformanceMessages.cs Tests/SmartGuard.Configuration.Tests/PowerPlanActivatorTests.cs Tests/SmartGuard.Configuration.Tests/HighPerformanceBoostTests.cs
git commit -m "fix(tray): switch to high performance through the scheme service with a deadline" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 删除 powercfg 包装并把门禁扩展到全部 src

**Files:**
- Modify: `src/SmartGuard.Configuration/PowerCfgProcessRunner.cs`
- Modify: `Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs`、`Tests/SmartGuard.Engine.Tests/PowerSchemeTestScope.cs`
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

从 `PowerCfgProcessRunner` 删除 `DefaultTimeout`、`RunOverrideForTests`、`RunAsyncOverrideForTests`、`RunPowerCfg`、`RunPowerCfgAsync`，保留 `RunProcess`、`RunProcessAsync`、`RunProcessCoreAsync` 及其私有辅助（以编译为准）。两个 `PowerSchemeTestScope` 删除 `Blocked` 字段与封锁、复位各两行。

```powershell
Get-ChildItem src,Tests -Recurse -Include *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-String -Pattern 'RunPowerCfg|RunOverrideForTests|RunAsyncOverrideForTests'
```

Expected: 无输出。

（类名 `PowerCfgProcessRunner` 与 `PowerCfgCommandResult` 此后只服务 WMI 探测，名称已不准确；更名涉及 Engine WMI 全链，属范围外，只在设计文档记录。）

- [ ] **Step 3: GREEN**

```powershell
foreach ($p in 'Configuration','Engine','Settings','Tray','Architecture') { dotnet test "Tests/SmartGuard.$p.Tests/SmartGuard.$p.Tests.csproj" --no-restore --logger "trx;LogFileName=task5-green-$p.trx" --results-directory .work/power-scheme-api-phase2 }
```

- [ ] **Step 4: 提交**

```powershell
git add -- src/SmartGuard.Configuration/PowerCfgProcessRunner.cs Tests/SmartGuard.Configuration.Tests/PowerSchemeTestScope.cs Tests/SmartGuard.Engine.Tests/PowerSchemeTestScope.cs Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs
git commit -m "refactor(config): remove powercfg wrappers and gate the whole product" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 契约与设计文档

**Files:** `docs/ARCHITECTURE-CONTRACT.md`、`docs/implementation-plans/2026-11-19-power-scheme-api-migration.md`、`Tests/SmartGuard.Architecture.Tests/PowerSchemeApiArchitectureTests.cs`

- [ ] **Step 1: 门禁 RED**

`Contract_registers_the_scheme_api_invariant_and_owner` 末尾加入：

```csharp
    contract.Should().NotContain("第二阶段前的登记例外", "phase two removed the Settings/Tray exceptions");
    contract.Should().Contain("LoadedSnapshot");
```

Expected: 1 failed。

- [ ] **Step 2: 更新契约**

1. “电源方案操作”行的允许读者改为：`Engine 守护轮次、首次初始化、Guardian 计划名称、Settings 计划目录、Tray 一键高性能`。
2. 禁止项改为：`任何进程启动 powercfg.exe；在 PowrProfNative 之外导入电源方案函数；API 失败回退外部进程`。
3. `PowerPlanCatalogProvider` 行的说明末尾改为：`Engine 经 TryLoadOnceAsync、Settings 经 LoadWithRetryAsync，均走 PowerSchemeService；UI 只读 IO-free 的 LoadedSnapshot`。
4. S-05 行改为：`Settings 计划目录由 SettingsPlanCatalogCoordinator 以可取消的 LoadWithRetryAsync 在后台加载，UI 线程只读 LoadedSnapshot（未加载时显示“正在加载”）；不再有同步 TryLoad`。
5. `PWR-INV-SCHEME-API` 末尾的“门禁”前补一句：`门禁覆盖全部 src`。

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
- [ ] **Step 3: 用户安装**：确认 UAC；核对 Configuration/Settings/Tray DLL 与候选一致、配置哈希未变。
- [ ] **Step 4: 真实路径验收（需用户配合）：**
  - **打开设置窗口（冷启动，先结束 Settings 进程）：**
    - 窗口立即出现，状态栏先显示“正在加载电源计划...”，随后显示映射结果。
    - 三个下拉框显示本机计划名称，含自定义计划。
    - 记录从打开到名称出现的耗时（`Stopwatch` 不可用时以用户观察为准，标明来源）。
  - **托盘“一键高性能”：**
    - 当前为平衡时点击，立即切到高性能，日志出现“托盘手动切换高性能”。
    - Engine 随后的轮次不报外部变更。
  - **无 powercfg 进程：** 打开设置、切换计划、使用托盘期间，多次检查 `Get-CimInstance Win32_Process -Filter "Name='powercfg.exe'"` 均为空。
  - **失败路径：** 托盘切换失败路径（超时/错误码）无法在真机可控触发，标 `NOT RUN`，由 Task 4 自动化用例覆盖。
- [ ] **Step 5: 记录并提交**：迁移设计追加“第二阶段实施与验证记录”（RED/GREEN 证据、基线对照、summary、安装哈希、验收与 `NOT RUN` 项），提交 `docs(engine): record power scheme api phase two verification`。

---

## 自查记录

- **设计覆盖：**
  - 第 2.2 节 Settings 目录 → Task 1–3
  - Tray `PowerPlanActivator`（含无超时 `WaitForExit` 修复）→ Task 4
  - 删除 powercfg 包装与解析器 → Task 3、5
  - 门禁收紧到全部 `src` → Task 5
  - 契约 → Task 6
- **超出设计的范围（已说明理由）：** Settings UI 线程同步加载（新发现 #2–#4）属于同一目录真源的调用方式。迁移后若保留同步 `TryLoad`，会把 1.1–1.6 秒的 API 加载留在 UI 线程上。因此并入 Task 2，并修正与契约矛盾的门禁。
- **RED 合规：**
  - 新成员先以骨架引入：`LoadedSnapshot`、`Build` 可空参数、activator 返回状态。
  - Task 3 的迁移用例在旧实现上通过，只算回归保护。
  - Task 4 的骨架移除旧 `Process.Start`，避免 RED 在本机执行真实切换。
- **系统修改边界：**
  - Configuration 作用域在 Task 1–4 期间封锁 `PowerCfgProcessRunner`。
  - Tray activator 旧实现绕过该封锁，因此由骨架处理。
  - 真实写入由 `PowerWriteGuard` 拒绝。
- **类型一致：**
  - `PowerPlanCatalogProvider`：`LoadedSnapshot`、`LoadBudget`（由 `GuardianLoadBudget` 更名）、`LoadThroughSchemeServiceAsync`、`Publish`、`LoadAttemptAsync`
  - `IPowerPlanActivator.SetActivePlan(Guid) → PowerSchemeStatus`
  - `ManualHighPerformanceMessages.FormatActivationDeferred(string)`
  - `PowerPlanComboItemsBuilder.Build(IReadOnlyDictionary<Guid,string>?, Guid, string?)`
- **已知风险：**
  - Settings 进程与 Engine 进程各有一个执行器。两进程同时调用电源 API 互不影响门，但由操作系统串行化，单次耗时可能上升。第一阶段基线显示单次调用为毫秒级，列举约 1.1–1.6 秒，仍在预算内。
  - `TryCreate_never_loads…` 依赖测试窗口确实包含三个下拉框；Task 2 Step 2 已给出核实办法。
