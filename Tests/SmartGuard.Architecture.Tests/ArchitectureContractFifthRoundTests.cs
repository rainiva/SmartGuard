using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractFifthRoundTests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Section2_must_list_StopForUninstall_not_Stop()
  {
    ContractContent.Should().Contain("EngineLifecycle.StopForUninstall");
    ContractContent.Should().NotContain("| `EngineLifecycle.Stop` |");
  }

  [Fact]
  public void Section11_must_close_M17_through_M19_and_god_modules()
  {
    var content = ContractContent;
    content.Should().Contain("## 11. 第五轮治理关闭项");
    content.Should().Contain("| M-17 | **已关闭** |");
    content.Should().Contain("| M-18 | **已关闭** |");
    content.Should().Contain("| M-19 | **已关闭** |");
    content.Should().Contain("| GOD-02 | **已关闭** |");
    content.Should().Contain("| GOD-03 | **已关闭** |");
    content.Should().Contain("| GOD-04 | **已关闭** |");
    content.Should().Contain("| GOD-05 | **已关闭** |");
    content.Should().Contain("| E-11 | **登记** |");
    content.Should().Contain("| E-11b | **登记** |");
  }

  [Fact]
  public void Section6_must_index_fifth_round_architecture_gates()
  {
    var content = ContractContent;
    content.Should().Contain("SmartGuardPathsSingleSourceArchitectureTests");
    content.Should().Contain("SettingsUpdateCheckCoordinatorLineCountTests");
    content.Should().Contain("SettingsUpdateCheckCoordinatorArchitectureTests");
    content.Should().Contain("ToastAumidRegistrarArchitectureTests");
    content.Should().Contain("GuardianLoopArchitectureTests");
  }
}
