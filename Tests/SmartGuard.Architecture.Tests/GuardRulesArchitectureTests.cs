using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

/// <summary>
/// 统一自动守护规则门禁：自动决策唯一入口 = <c>PolicyEngine.Evaluate</c>；
/// Runner 只消费决策，不得内联第二套预期计划推导；决策/前台事实 Domain 值保持平台无关；
/// 架构契约登记唯一决策入口与本门禁。
/// </summary>
public class GuardRulesArchitectureTests
{
  [Fact]
  public void GuardianIterationRunner_consumes_unified_decision_and_does_not_rederive_expected_plan()
  {
    var runner = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs");

    runner.Should().Contain("PolicyEngine.Evaluate");

    // 退役决策入口与阈值/人工期限推导只允许存在于 PolicyEngine；
    // Runner 出现任一即视为第二套自动目标算法。
    runner.Should().NotContain("GetExpectedPlanGuid");
    runner.Should().NotContain("IsManualHighPerformanceActive");
    runner.Should().NotContain("PowerSaverThresholdSec");
    runner.Should().NotContain("BalancedThresholdSec");
    runner.Should().NotContain("LowBatteryPercent");
  }

  [Fact]
  public void Retired_GetExpectedPlanGuid_entry_no_longer_exists_under_src()
  {
    var offenders = Directory
      .EnumerateFiles(Path.Combine(SourceScanHelper.RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
      .Where(path => File.ReadAllText(path).Contains("GetExpectedPlanGuid", StringComparison.Ordinal))
      .Select(path => Path.GetRelativePath(SourceScanHelper.RepoRoot, path))
      .ToList();

    offenders.Should().BeEmpty(
      "GetExpectedPlanGuid 已退役，自动决策唯一入口 = PolicyEngine.Evaluate；残留于: "
      + string.Join(", ", offenders));
  }

  [Fact]
  public void Guard_decision_and_foreground_domain_values_stay_free_of_win32_and_wpf()
  {
    foreach (var relativePath in new[]
      {
        "src/SmartGuard.Engine/Domain/GuardDecision.cs",
        "src/SmartGuard.Engine/Domain/ForegroundObservation.cs",
      })
    {
      var source = SourceScanHelper.ReadSource(relativePath);
      source.Should().NotContain("DllImport");
      source.Should().NotContain("LibraryImport");
      source.Should().NotContain("System.Windows");
      source.Should().NotContain("GetForegroundWindow");
      source.Should().NotContain("GetWindowThreadProcessId");
      source.Should().NotContain("HWND");
    }
  }

  [Fact]
  public void Architecture_contract_registers_PolicyEngine_Evaluate_as_single_decision_entry()
  {
    var contract = File.ReadAllText(Path.Combine(SourceScanHelper.RepoRoot, "docs", "ARCHITECTURE-CONTRACT.md"));

    contract.Should().Contain("PolicyEngine.Evaluate");
    contract.Should().Contain("唯一自动决策入口");
    contract.Should().Contain("GuardRulesArchitectureTests");
    contract.Should().Contain("配置提交不得先删正式文件");
  }

  [Fact]
  public void Foreground_reader_is_the_single_win32_fact_collector_and_stays_bounded()
  {
    var reader = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Infrastructure/ForegroundProgramReader.cs");

    // 单次查询链与最小权限访问
    reader.Should().Contain("GetForegroundWindow");
    reader.Should().Contain("GetWindowThreadProcessId");
    reader.Should().Contain("OpenProcess");
    reader.Should().Contain("QueryFullProcessImageNameW");
    reader.Should().Contain("ProcessQueryLimitedInformation = 0x1000");

    // 每次查询在 finally 释放进程句柄；缓冲有上限
    reader.Should().Contain("finally");
    reader.Should().Contain("CloseHandle");
    reader.Should().Contain("32768");

    // 无 hook / 监听线程 / Timer / 事件队列 / 进程身份缓存 / 窗口标题 / 状态与日志写入
    reader.Should().NotContain("SetWinEventHook");
    reader.Should().NotContain("GetWindowText");
    reader.Should().NotContain("System.Threading.Timer");
    reader.Should().NotContain("ConcurrentQueue");
    reader.Should().NotContain("ConcurrentDictionary");
    reader.Should().NotContain("StatusPublisher");
    reader.Should().NotContain("PowerCfgExecutor");
    reader.Should().NotContain("SmartGuardLogWriter");
  }

  [Fact]
  public void Foreground_listener_only_submits_merged_signal_and_owns_one_hook_one_thread()
  {
    var listener = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Infrastructure/ForegroundEventListener.cs");

    // 一个 hook + 一个阻塞消息循环线程；注册线程卸载
    listener.Should().Contain("SetWinEventHook");
    listener.Should().Contain("UnhookWinEvent");
    listener.Should().Contain("EventSystemForeground = 0x0003");
    listener.Should().Contain("WineventOutOfContext = 0x0000");
    listener.Should().Contain("MsgWaitForMultipleObjectsEx");

    // 回调零 IO：不采集事实、不读配置、不写状态/日志、不触发电源动作、不重启自身
    listener.Should().NotContain("GetForegroundWindow");
    listener.Should().NotContain("GuardConfigRepository");
    listener.Should().NotContain("StatusPublisher");
    listener.Should().NotContain("PowerCfgExecutor");
    listener.Should().NotContain("SmartGuardLogWriter");
    listener.Should().NotContain("File.");
    listener.Should().NotContain("System.Threading.Timer");
    listener.Should().NotContain("Task.Run");
    listener.Should().NotContain("ThreadPool");
  }

  [Fact]
  public void Guardian_cycle_coordinator_uses_single_admission_rule_without_plan_derivation()
  {
    var coordinator = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianCycleCoordinator.cs");

    // 准入判定单一规则真源；配置读取带有效性结论
    coordinator.Should().Contain("PolicyEngine.EvaluateAdmission");
    coordinator.Should().Contain("GuardConfigReadResult");

    // 不做第二套目标推导、不查询电源计划、不写状态
    coordinator.Should().NotContain("GetExpectedPlanGuid");
    coordinator.Should().NotContain("PowerSaverThresholdSec");
    coordinator.Should().NotContain("BalancedThresholdSec");
    coordinator.Should().NotContain("LowBatteryPercent");
    coordinator.Should().NotContain("PowerCfgExecutor");
    coordinator.Should().NotContain("StatusPublisher");
  }

  [Fact]
  public void Architecture_contract_registers_foreground_fact_collector_and_listener_owners()
  {
    var contract = File.ReadAllText(Path.Combine(SourceScanHelper.RepoRoot, "docs", "ARCHITECTURE-CONTRACT.md"));

    contract.Should().Contain("ForegroundProgramReader");
    contract.Should().Contain("ForegroundEventListener");
    contract.Should().Contain("GuardianCycleCoordinator");
    contract.Should().Contain("前台事实采集");
    contract.Should().Contain("唯一前台事件监听");
    contract.Should().Contain("前台事件不得推迟截止时间");
    contract.Should().Contain("原始时间戳按 TimestampFrequency 换算");
  }
}
