using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractSixthRoundTests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Section12_must_register_shell_blockers_for_former_exception_items()
  {
    var section12 = ExtractSection(ContractContent, "## 12.");
    section12.Should().Contain("| E-10 |");
    section12.Should().Contain("| E-11 |");
    section12.Should().Contain("| E-11b |");
    section12.Should().Contain("DesktopShellRegistration");
    section12.Should().Contain("阻断");
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
  public void Section1_must_register_shell_registration_as_installer_owned()
  {
    var section1 = ExtractSection(ContractContent, "## 1.");
    section1.Should().Contain("DesktopShellRegistration");
    section1.Should().NotContain(".smartguard-toast-");
    section1.Should().NotContain("ToastAumidRegistrar");
  }

  private static string ExtractSection(string content, string header)
  {
    var start = content.IndexOf(header, StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1);
    var next = content.IndexOf("\n## ", start + header.Length, StringComparison.Ordinal);
    return next > start ? content[start..next] : content[start..];
  }
}
