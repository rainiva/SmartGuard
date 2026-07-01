using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class LogViewerProgramArchitectureTests
{
  [Fact]
  public void LogViewer_main_must_redirect_to_settings_logs_page()
  {
    var program = SourceScanHelper.ReadSource("src/SmartGuard.LogViewer/Program.cs");
    program.Should().NotContain("Application.Run(form");
    program.Should().NotContain("new LogViewerForm");
    program.Should().Contain("LogViewerEntryRedirect.LaunchSettingsLogsPage");

    var redirect = SourceScanHelper.ReadSource("src/SmartGuard.LogViewer/LogViewerEntryRedirect.cs");
    redirect.Should().Contain("SmartGuardPaths.SettingsExe");
    redirect.Should().Contain("--page logs");
  }

  [Fact]
  public void Settings_program_must_support_logs_page_cli_routing()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/Program.cs");
    source.Should().Contain("ParseStartPage");
    source.Should().Contain("SetInitialPage");
    source.Should().Contain("--page");
  }
}
