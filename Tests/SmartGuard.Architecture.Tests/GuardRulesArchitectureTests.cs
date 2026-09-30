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
  }
}
