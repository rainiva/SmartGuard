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

  [Fact]
  public void Product_must_not_keep_LogViewer_test_or_namespace_identity()
  {
    var slnx = SourceScanHelper.ReadSource("SmartGuard.slnx");
    var settingsProject = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SmartGuard.Settings.csproj");
    var source = string.Join(
      Environment.NewLine,
      Directory.EnumerateFiles(Path.Combine(SourceScanHelper.RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
        .Select(File.ReadAllText));

    slnx.Should().NotContain("SmartGuard.LogViewer.Tests");
    settingsProject.Should().NotContain("InternalsVisibleTo Include=\"SmartGuard.LogViewer.Tests\"");
    source.Should().NotContain("namespace SmartGuard.LogViewer");
  }
}
