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
  public void Section1_must_register_ElevationHelper_in_sixth_round_closure()
  {
    var content = ContractContent;
    content.Should().Contain("ElevationHelper");
    var section12 = ExtractSection(content, "## 12.");
    section12.Should().Contain("E-11b");
    section12.Should().Contain("Open-*.cmd");
  }

  private static string ExtractSection(string content, string header)
  {
    var start = content.IndexOf(header, StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1);
    var next = content.IndexOf("\n## ", start + header.Length, StringComparison.Ordinal);
    return next > start ? content[start..next] : content[start..];
  }
}
