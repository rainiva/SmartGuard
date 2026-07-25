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
    buildCmd.Should().Contain("SmartGuard.Cli.csproj");
    buildCmd.Should().Contain("publish");
    buildCmd.Should().Contain("dotnet run --project");
    buildCmd.Should().Contain("--root \"%~dp0.\"");
    buildCmd.Should().NotContain("if exist \"%~dp0bin\\SmartGuard.Cli.exe\"");
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
    installer.Should().Contain("Exec(EngineExe");
    installer.Should().Contain("--uninstall");
    installer.Should().NotContain("SmartGuard.LogViewer.exe");
    installer.Should().NotContain("Source: \"{#StagingDir}\\\\bin\\\\*\"");
    installer.Should().NotContain("Pos('SmartGuard.Engine.exe'");
  }

  [Fact]
  public void Installer_iss_must_include_generated_constants_and_run_includes()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    iss.Should().Contain(@"generated\SmartGuard.Distribution.Constants.iss.inc");
    iss.Should().Contain(@"generated\SmartGuard.Distribution.Run.iss.inc");
  }

  [Fact]
  public void Installer_iss_must_not_handwrite_live_exe_or_task_literals()
  {
    var installer = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    installer.Should().NotContain(@"#define MyAppExeName ""SmartGuard.Tray.exe""");
    installer.Should().NotContain(@"UninstallDisplayIcon={app}\bin\SmartGuard.Tray.exe");
    installer.Should().NotContain(@"Filename: ""{app}\bin\SmartGuard.Engine.exe""");
    installer.Should().NotContain(@"Filename: ""{app}\bin\SmartGuard.Cli.exe""");
    installer.Should().NotContain(@"/TN ""SmartGuard Guardian""");
  }

  [Fact]
  public void Installer_iss_must_not_keep_live_engine_literal_outside_run_section_either()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    iss.Should().NotContain(@"RootPath + '\bin\SmartGuard.Engine.exe'");
  }

  [Fact]
  public void Publish_and_staging_must_use_SystemEventsRidAssetGuard()
  {
    var publisher = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Build/DesktopSuitePublisher.cs");
    publisher.Should().Contain("SystemEventsRidAssetGuard.EnsureAligned");

    var stagingValidator = SourceScanHelper.ReadSource("src/SmartGuard.Packaging/Staging/StagingLayoutValidator.cs");
    stagingValidator.Should().Contain("SystemEventsRidAssetGuard.Validate");

    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    contract.Should().Contain("SystemEventsRidAssetGuard");
  }

  [Fact]
  public void Contract_docs_must_record_that_inno_live_exe_and_task_literals_are_generated_not_handwritten()
  {
    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    contract.Should().Contain("generated/SmartGuard.Distribution.Constants.iss.inc");
    contract.Should().Contain("generated/SmartGuard.Distribution.Run.iss.inc");
    contract.Should().Contain("LegacyScheduledTaskCatalog");
    contract.Should().NotContain("见 MIGRATION");
  }
}
