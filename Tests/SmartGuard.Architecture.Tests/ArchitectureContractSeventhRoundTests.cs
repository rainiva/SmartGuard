using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractSeventhRoundTests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Section13_must_register_distribution_blockers_for_former_closure_rows()
  {
    var section13 = ExtractSection(ContractContent, "## 13.");
    section13.Should().Contain("| R7-INNO |");
    section13.Should().Contain("| R7-PUBLISH |");
    section13.Should().Contain("DesktopShellRegistration");
    section13.Should().Contain("DesktopDistributionManifest");
    section13.Should().Contain("阻断");
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
  public void Section2_must_register_direct_user_entrypoints_not_cli_shortcut_wrappers()
  {
    var section2 = ExtractSection(ContractContent, "## 2.");
    section2.Should().Contain("SmartGuard.Tray.exe");
    section2.Should().Contain("SmartGuard.Settings.exe --page logs");
    section2.Should().NotContain("SmartGuard.Cli.exe open");
  }

  private static string ExtractSection(string content, string header)
  {
    var start = content.IndexOf(header, StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1);
    var next = content.IndexOf("\n## ", start + header.Length, StringComparison.Ordinal);
    return next > start ? content[start..next] : content[start..];
  }
}
