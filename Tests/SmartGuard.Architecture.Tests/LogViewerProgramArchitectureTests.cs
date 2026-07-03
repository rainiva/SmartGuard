using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class LogViewerProgramArchitectureTests
{
  [Fact]
  public void Product_must_not_ship_a_LogViewer_host_executable()
  {
    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.LogViewer",
      "SmartGuard.LogViewer.csproj")).Should().BeFalse();
    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.LogViewer",
      "Program.cs")).Should().BeFalse();
    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.LogViewer",
      "LogViewerEntryRedirect.cs")).Should().BeFalse();
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
