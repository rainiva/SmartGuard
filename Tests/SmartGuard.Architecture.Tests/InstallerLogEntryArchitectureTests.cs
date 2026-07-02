using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class InstallerLogEntryArchitectureTests
{
  [Fact]
  public void Inno_start_menu_log_shortcut_opens_settings_logs_page_not_logviewer()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    iss.Should().MatchRegex(@"\{group\}.*日志.*Open-Logs\.cmd",
      because: "installer log shortcut must use Open-Logs.cmd wrapper as the single user entry");
    iss.Should().NotMatchRegex(@"\{group\}.*日志.*SmartGuard\.LogViewer\.exe");
  }
}
