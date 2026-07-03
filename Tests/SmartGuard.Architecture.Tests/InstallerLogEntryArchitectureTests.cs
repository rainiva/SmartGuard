using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class InstallerLogEntryArchitectureTests
{
  [Fact]
  public void Log_start_menu_entry_must_be_owned_by_DesktopShellRegistration_not_Inno()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var shellOwner = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Shell/DesktopShellRegistration.cs");

    shellOwner.Should().Contain("SmartGuard 日志.lnk");
    shellOwner.Should().Contain("SmartGuardPaths.SettingsExe(root)");
    shellOwner.Should().Contain("--page logs");
    shellOwner.Should().NotContain("SmartGuard.Cli.exe");

    iss.Should().NotContain("[Icons]");
    iss.Should().NotMatchRegex(@"\{group\}.*日志");
    iss.Should().NotContain("SmartGuard.LogViewer.exe");
  }
}
