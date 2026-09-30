using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

/// <summary>
/// 前台程序限制设置门禁：名单编辑经共享 <c>GuardConfigValidator</c> 与既有防抖保存链，
/// UI 不直接写配置仓库、不做第二套状态解析；架构契约登记 Owner 与门禁。
/// </summary>
public class SettingsForegroundCoordinatorArchitectureTests
{
  [Fact]
  public void Settings_foreground_coordinator_edits_config_only_through_shared_chain()
  {
    var coordinator = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsForegroundCoordinator.cs");

    // 路径规则单源 + 共享状态读取链
    coordinator.Should().Contain("GuardConfigValidator");
    coordinator.Should().Contain("StatusJsonReader");

    // UI 不直接拥有持久化：不触碰配置仓库、不做第二套状态解析
    coordinator.Should().NotContain("GuardConfigRepository");
    coordinator.Should().NotContain("repository.Save");
    coordinator.Should().NotContain("JsonSerializer");

    // 保存仍经 SettingsPolicyCoordinator 的既有防抖保存链（QueueSave）
    var policyCoordinator = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    policyCoordinator.Should().Contain("SettingsForegroundCoordinator");
    policyCoordinator.Should().Contain("QueueSave");
  }

  [Fact]
  public void Architecture_contract_registers_foreground_settings_owner()
  {
    var contract = File.ReadAllText(Path.Combine(SourceScanHelper.RepoRoot, "docs", "ARCHITECTURE-CONTRACT.md"));

    contract.Should().Contain("SettingsForegroundCoordinator");
    contract.Should().Contain("ForegroundGuardStatusText");
  }
}
