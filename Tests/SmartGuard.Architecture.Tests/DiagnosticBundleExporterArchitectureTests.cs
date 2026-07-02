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
    source.Should().Contain("GitHubTokenProtector.LegacyFieldName");
    source.Should().Contain("GitHubTokenProtector.ProtectedFieldName");
  }

  [Fact]
  public void SettingsDiagnosticExportCoordinator_must_delegate_to_exporter()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsDiagnosticExportCoordinator.cs");
    source.Should().Contain("DiagnosticBundleExporter.Export");
    source.Should().NotContain("GitHubTokenProtector");
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
