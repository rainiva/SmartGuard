using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

/// <summary>
/// 守护状态跨进程链门禁：Engine 只发布 <see cref="SmartGuard.Contracts.GuardStatusCodes"/>
/// 登记的稳定代码、不携带展示文案；Publisher 相等判定与 Tray 相等判定 + Clone 覆盖守护字段。
/// 架构契约登记于"守护状态稳定代码"真源行、STATUS-GUARD-CODES 不变量与 §6 门禁表。
/// </summary>
public class GuardStatusCodesArchitectureTests
{
  [Fact]
  public void Foreground_observation_has_one_runtime_owner_and_a_registered_contract()
  {
    var contract = File.ReadAllText(Path.Combine(SourceScanHelper.RepoRoot, "docs", "ARCHITECTURE-CONTRACT.md"));
    contract.Should().Contain("ForegroundObservationSec");
    contract.Should().Contain("foreground-observation");
    var runner = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs");
    var cycle = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianCycleCoordinator.cs");
    runner.Should().Contain("UpdateForegroundObservation");
    cycle.Should().Contain("UpdateForegroundObservation");
    var loop = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs");
    loop.Should().Contain("timeProvider: _observationTime", "light and full paths share one monotonic observation clock");
  }

  private static readonly string[] DisplayOnlyTexts =
  {
    "添加程序后生效",
    "等待指定程序",
    "暂时无法识别前台程序",
    "临时高性能",
  };

  [Fact]
  public void Engine_publishes_stable_codes_and_never_carries_guard_display_text()
  {
    foreach (var source in Directory.EnumerateFiles(
      Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Engine"), "*.cs", SearchOption.AllDirectories)
      .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
    {
      var content = File.ReadAllText(source);
      foreach (var displayText in DisplayOnlyTexts)
      {
        content.Should().NotContain(
          displayText,
          $"Engine 只发布登记代码，展示文案只允许在 UI 层映射（{source}）");
      }
    }

    var runner = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs");
    runner.Should().Contain("guardReason");
    runner.Should().Contain("GuardDecisionCodes.ToReasonCode");
    runner.Should().Contain("GuardStatusCodes.Reasons.ConfigUnavailable");
    runner.Should().Contain("GuardStatusCodes.Reasons.ProbeFailed");
    runner.Should().Contain("GuardStatusCodes.Reasons.BlockedByLifecycle");
  }

  [Fact]
  public void Status_payload_consumers_update_equality_and_clone_with_guard_fields()
  {
    var publisher = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs");
    publisher.Should().Contain("a.guardReason == b.guardReason");

    var trayState = SourceScanHelper.ReadSource("src/SmartGuard.Tray/TrayDisplayState.cs");
    trayState.Should().Contain("left.guardReason == right.guardReason");
    trayState.Should().Contain("guardReason = status.guardReason");
  }

  [Fact]
  public void Architecture_contract_registers_guard_status_codes()
  {
    var contract = File.ReadAllText(Path.Combine(SourceScanHelper.RepoRoot, "docs", "ARCHITECTURE-CONTRACT.md"));

    contract.Should().Contain("GuardStatusCodes");
    contract.Should().Contain("GuardDecisionCodes");
    contract.Should().Contain("STATUS-GUARD-CODES");
    contract.Should().Contain("GuardStatusCodesArchitectureTests");
  }

  [Fact]
  public void Status_file_contract_allows_snapshot_readers_and_preserves_failed_replacements()
  {
    var reader = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/StatusJsonReader.cs");
    reader.Should().Contain("FileShare.Delete");
    reader.Should().NotContain("File.ReadAllText");
    var contract = File.ReadAllText(Path.Combine(SourceScanHelper.RepoRoot, "docs", "ARCHITECTURE-CONTRACT.md"));
    contract.Should().Contain("STATUS-SNAPSHOT");
    contract.Should().Contain("替换失败保留旧正式状态");
  }

  [Fact]
  public void Status_backup_path_has_one_owner_and_replacement_retains_recovery_copy()
  {
    var paths = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SmartGuardPaths.cs");
    paths.Should().Contain("StatusBackupForFile");
    var publisher = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Infrastructure/StatusPublisher.cs");
    publisher.Should().Contain("SmartGuardPaths.StatusBackupForFile");
    publisher.Should().NotContain("destinationBackupFileName: null");
  }
}
