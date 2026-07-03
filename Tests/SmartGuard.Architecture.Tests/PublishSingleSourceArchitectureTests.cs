using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class PublishSingleSourceArchitectureTests
{
  [Fact]
  public void Single_entry_rule_doc_must_describe_one_start_menu_entry_not_three()
  {
    var doc = SourceScanHelper.ReadSource("docs/architecture/single-entry-single-source.md");

    doc.Should().Contain("SmartGuard.lnk");
    doc.Should().NotContain("SmartGuard 日志.lnk");
    doc.Should().NotContain("SmartGuard 设置.lnk");
    doc.Should().NotContain("SmartGuard 托盘.lnk");
  }

  [Fact]
  public void DesktopDistributionManifest_must_exist_as_the_only_distribution_truth()
  {
    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Configuration",
      "Distribution",
      "DesktopDistributionManifest.cs")).Should().BeTrue();

    var buildCmd = SourceScanHelper.ReadSource("build.cmd");
    buildCmd.Should().NotContain("dotnet publish");
    buildCmd.Should().Contain("SmartGuard.Cli.exe");
    buildCmd.Should().Contain("publish");
  }

  [Fact]
  public void Publish_stage_and_installer_must_delegate_to_DesktopDistributionManifest()
  {
    var manifest = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Distribution/DesktopDistributionManifest.cs");
    manifest.Should().Contain("LegacyCleanupPaths");
    manifest.Should().NotContain("SmartGuard.LogViewer.csproj");
    manifest.Should().NotContain("LogViewerExeFileName");

    var publisher = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Build/DesktopSuitePublisher.cs");
    publisher.Should().Contain("DesktopDistributionManifest.PublishedProjects");

    var copier = SourceScanHelper.ReadSource("src/SmartGuard.Packaging/Staging/PayloadCopier.cs");
    copier.Should().Contain("DesktopDistributionManifest.PayloadEntries");

    var layout = SourceScanHelper.ReadSource("src/SmartGuard.Packaging/Staging/StagingLayout.cs");
    layout.Should().Contain("DesktopDistributionManifest.RequiredStagingPaths");

    var installer = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    installer.Should().Contain(@"generated\SmartGuard.Distribution.Files.iss.inc");
    installer.Should().Contain(@"generated\SmartGuard.Distribution.Processes.iss.inc");
    installer.Should().NotContain("SmartGuard.LogViewer.exe");
    installer.Should().NotContain("Source: \"{#StagingDir}\\\\bin\\\\*\"");
    installer.Should().NotContain("Pos('SmartGuard.Engine.exe'");
  }
}
