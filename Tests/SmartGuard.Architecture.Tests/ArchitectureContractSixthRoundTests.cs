using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractSixthRoundTests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Section12_must_close_M20_through_E11b()
  {
    var content = ContractContent;
    content.Should().Contain("## 12. 第六轮治理关闭项");
    content.Should().Contain("| M-20 | **已关闭** |");
    content.Should().Contain("| M-21 | **已关闭** |");
    content.Should().Contain("| S-09 | **已关闭** |");
    content.Should().Contain("| E-11 | **已关闭** |");
    content.Should().Contain("| E-11b | **已关闭** |");
    content.Should().Contain("| DOC-05 | **已关闭** |");
  }

  [Fact]
  public void Section6_must_index_sixth_round_architecture_gates()
  {
    var content = ContractContent;
    content.Should().Contain("SmartGuardPathsExtensionsArchitectureTests");
    content.Should().Contain("ScriptPathConstantsParityArchitectureTests");
    content.Should().Contain("SettingsPauseDisplayArchitectureTests");
    content.Should().Contain("ElevationSingleSourceArchitectureTests");
    content.Should().Contain("InnoShortcutWrapperArchitectureTests");
    content.Should().Contain("RunTestsOptimizationArchitectureTests");
    content.Should().Contain("ArchitectureContractSixthRoundTests");
  }

  [Fact]
  public void Section1_must_register_ElevationHelper_and_Open_wrappers()
  {
    var content = ContractContent;
    content.Should().Contain("ElevationHelper");
    content.Should().Contain("Request-AdminElevation.cmd");
    content.Should().Contain("Open-Settings.cmd");
    content.Should().Contain("Open-Logs.cmd");
    content.Should().Contain("Open-Tray.cmd");
  }
}
