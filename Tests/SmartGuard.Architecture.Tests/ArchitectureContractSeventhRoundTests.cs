using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractSeventhRoundTests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Section13_must_close_R7_cmd_migration_items()
  {
    var content = ContractContent;
    content.Should().Contain("## 13. 第七轮治理关闭项");
    content.Should().Contain("| R7-CLI | **已关闭** |");
    content.Should().Contain("| R7-OPS | **已关闭** |");
    content.Should().Contain("| R7-INNO | **已关闭** |");
    content.Should().Contain("| R7-PUBLISH | **已关闭** |");
    content.Should().Contain("| DOC-06 | **已关闭** |");
  }

  [Fact]
  public void Section6_must_index_seventh_round_architecture_gates()
  {
    var content = ContractContent;
    content.Should().Contain("GuardianTaskRunnerArchitectureTests");
    content.Should().Contain("CliThinFacadeArchitectureTests");
    content.Should().Contain("InnoShortcutWrapperArchitectureTests");
    content.Should().Contain("PublishSingleSourceArchitectureTests");
    content.Should().Contain("StatusReporterArchitectureTests");
    content.Should().Contain("ArchitectureContractSeventhRoundTests");
  }

  [Fact]
  public void Section2_must_register_SmartGuard_Cli_entry_points()
  {
    var section2 = ExtractSection(ContractContent, "## 2.");
    section2.Should().Contain("SmartGuard.Cli.exe");
    section2.Should().Contain("DesktopSuitePublisher");
    section2.Should().NotContain("Open-Settings.cmd");
    section2.Should().NotContain("Request-AdminElevation.cmd");
  }

  private static string ExtractSection(string content, string header)
  {
    var start = content.IndexOf(header, StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1);
    var next = content.IndexOf("\n## ", start + header.Length, StringComparison.Ordinal);
    return next > start ? content[start..next] : content[start..];
  }
}
