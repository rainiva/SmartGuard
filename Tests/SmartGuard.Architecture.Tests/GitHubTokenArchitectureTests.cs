using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class GitHubTokenArchitectureTests
{
  [Fact]
  public void Architecture_contract_must_document_github_token_fields()
  {
    var content = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    content.Should().Contain("GitHubToken");
    content.Should().Contain("GitHubTokenProtected");
    content.Should().Contain("GitHubTokenProtector");
  }

  [Fact]
  public void GuardConfigRepository_must_not_write_plaintext_GitHubToken_key()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardConfigRepository.cs");
    source.Should().NotContain("node[\"GitHubToken\"]");
  }

  [Fact]
  public void GitHubTokenProtector_must_exist_in_configuration()
  {
    File.Exists(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Configuration",
        "GitHubTokenProtector.cs"))
      .Should().BeTrue();
  }

  [Fact]
  public void SettingsGitHubTokenCoordinator_must_exist()
  {
    File.Exists(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Settings",
        "SettingsGitHubTokenCoordinator.cs"))
      .Should().BeTrue();
  }
}
