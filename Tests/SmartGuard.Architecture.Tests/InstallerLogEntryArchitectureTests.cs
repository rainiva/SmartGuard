using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class InstallerLogEntryArchitectureTests
{
  [Fact]
  public void Shell_owner_must_not_publish_dedicated_logs_shortcut_any_more()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var shellOwner = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Shell/DesktopShellRegistration.cs");
    var liveStartMenu = SourceScanHelper.ExtractMethodBody(shellOwner, "GetStartMenuShortcuts(string root)");

    liveStartMenu.Should().Contain("SmartGuard.lnk");
    liveStartMenu.Should().NotContain("SmartGuard 日志.lnk");
    liveStartMenu.Should().NotContain("SmartGuard 设置.lnk");
    liveStartMenu.Should().NotContain("SmartGuard 托盘.lnk");
    liveStartMenu.Should().NotContain("--page logs");

    iss.Should().NotContain("[Icons]");
    iss.Should().NotMatchRegex(@"\{group\}.*日志");
    iss.Should().NotContain("SmartGuard.LogViewer.exe");
  }
}
