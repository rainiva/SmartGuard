using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractFourthRoundTests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Section9_ME03_must_describe_StopProcesses_not_uninstall()
  {
    var content = ContractContent;
    var me03Line = content
      .Split('\n')
      .First(line => line.Contains("| ME-03 |", StringComparison.Ordinal));

    me03Line.Should().Contain("EngineLifecycle.StopProcesses");
    me03Line.Should().NotContain("--uninstall");
  }

  [Fact]
  public void Section2_must_list_SettingsLogsPageLauncher_as_log_spawn_truth()
  {
    ContractContent.Should().Contain("SettingsLogsPageLauncher.Open");
  }

  [Fact]
  public void Section6_must_index_third_round_architecture_gates()
  {
    var content = ContractContent;
    content.Should().Contain("SettingsLogsPageLauncherArchitectureTests");
    content.Should().Contain("SettingsThemeSaveArchitectureTests");
    content.Should().Contain("ToastNotificationLineCountTests");
    content.Should().Contain("TrayApplicationContextLineCountTests");
    content.Should().Contain("SettingsPolicyCoordinatorLineCountTests");
    content.Should().Contain("PublishAllReferenceArchitectureTests");
  }

  [Fact]
  public void Contract_must_register_E10_dev_tray_scripts()
  {
    var content = ContractContent;
    content.Should().Contain("E-10");
    content.Should().Contain("Start-Tray.cmd");
    content.Should().Contain("Restart-Tray.cmd");
  }

  [Fact]
  public void Contract_must_have_fourth_round_closure_section()
  {
    ContractContent.Should().Contain("## 10. 第四轮治理关闭项");
  }
}
