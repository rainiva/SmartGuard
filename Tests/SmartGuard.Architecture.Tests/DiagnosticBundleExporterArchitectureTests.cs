using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class DiagnosticBundleExporterArchitectureTests
{
  [Fact]
  public void DiagnosticBundleExporter_must_exist_in_configuration()
  {
    File.Exists(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Configuration",
        "DiagnosticBundleExporter.cs"))
      .Should().BeTrue();
  }

  [Fact]
  public void DiagnosticBundleExporter_must_strip_github_token_fields()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/DiagnosticBundleExporter.cs");
    source.Should().Contain("DiagnosticRedactor");
    source.Should().Contain("RedactConfigJson");
  }

  [Fact]
  public void DiagnosticBundleExporter_must_not_open_configured_log_archives_directly()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/DiagnosticBundleExporter.cs");
    source.Should().NotContain("ReadTailBytes");
    source.Should().NotContain("ResolveLogFile");
    source.Should().NotContain("DefaultLogFile(");
    source.Should().Contain("StreamAsync");
  }

  [Fact]
  public void SettingsDiagnosticExportCoordinator_must_delegate_to_exporter()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsDiagnosticExportCoordinator.cs");
    source.Should().Contain("DiagnosticBundleExporter.ExportAsync");
    source.Should().Contain("DiagnosticBundleRequest");
    source.Should().NotContain("GitHubTokenProtector");
  }

  [Fact]
  public void SettingsLogExportCoordinator_must_build_diagnostic_bundle_request()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsLogExportCoordinator.cs");
    source.Should().Contain("DiagnosticBundleRequest");
    source.Should().Contain("DiagnosticBundleExporter.ExportAsync");
    source.Should().NotContain("ZipArchive");
  }

  [Fact]
  public void Architecture_contract_must_index_diagnostic_bundle_exporter()
  {
    var content = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    content.Should().Contain("DiagnosticBundleExporter");
    content.Should().Contain("DiagnosticBundleExporterArchitectureTests");
    content.Should().Contain("DiagnosticBundleExporterTests");
  }
}
