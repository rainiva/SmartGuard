using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public sealed class RetiredSecretFieldArchitectureTests
{
  [Fact]
  public void Removed_secret_feature_files_must_not_exist()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Configuration", "GitHubTokenProtector.cs"))
      .Should().BeFalse();
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Settings", "SettingsGitHubTokenCoordinator.cs"))
      .Should().BeFalse();
  }

  [Fact]
  public void Runtime_model_ui_and_update_requests_must_not_expose_removed_secret_feature()
  {
    SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardConfig.cs")
      .Should().NotContain("GitHubToken");
    SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsUpdateCheckCoordinator.cs")
      .Should().NotContain("Authorization");
    SourceScanHelper.ReadSource("src/SmartGuard.Settings/SmartGuard.Settings.xaml")
      .Should().NotContain("GitHub Token");
  }

  [Fact]
  public void Repository_and_diagnostics_keep_only_retired_field_cleanup_contract()
  {
    SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardConfigRepository.cs")
      .Should().Contain("RemoveRetiredSecretFields");
    SourceScanHelper.ReadSource("src/SmartGuard.Configuration/DiagnosticRedactor.cs")
      .Should().Contain("RetiredSecretFieldRegistry");
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md")
      .Should().Contain("退役敏感字段");
  }
}
