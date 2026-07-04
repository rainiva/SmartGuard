using FluentAssertions;
using SmartGuard.Configuration;
using System.Text.Json.Nodes;

namespace SmartGuard.Configuration.Tests;

public class GuardConfigRepositoryTokenTests
{
  [Fact]
  public void Save_writes_GitHubTokenProtected_and_not_plaintext()
  {
    var dir = CreateTempDir();
    var path = Path.Combine(dir, SmartGuardPaths.ConfigFileName);

    try
    {
      var repo = new GuardConfigRepository(path);
      var config = GuardConfig.CreateDefault(dir);
      config.GitHubToken = "ghp_secretSaveToken1234567890";
      repo.Save(config);

      var json = File.ReadAllText(path);
      json.Should().Contain("GitHubTokenProtected");
      json.Should().NotContain("ghp_secretSaveToken1234567890");
      json.Should().NotContain("\"GitHubToken\"");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void TryLoad_decrypts_protected_token()
  {
    var dir = CreateTempDir();
    var path = Path.Combine(dir, SmartGuardPaths.ConfigFileName);
    const string token = "ghp_protectedLoadToken1234567890";
    var protectedValue = GitHubTokenProtector.Protect(token)!;

    try
    {
      File.WriteAllText(path, $$"""
        {
          "BalancedThresholdSec": 300,
          "GitHubTokenProtected": "{{protectedValue}}"
        }
        """);

      var repo = new GuardConfigRepository(path);
      repo.TryLoad()!.GitHubToken.Should().Be(token);
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void TryLoad_migrates_legacy_plaintext_and_rewrites_config()
  {
    var dir = CreateTempDir();
    var path = Path.Combine(dir, SmartGuardPaths.ConfigFileName);
    const string token = "ghp_legacyPlaintextToken1234567890";

    try
    {
      File.WriteAllText(path, $$"""
        {
          "BalancedThresholdSec": 300,
          "GitHubToken": "{{token}}"
        }
        """);

      var repo = new GuardConfigRepository(path);
      repo.TryLoad()!.GitHubToken.Should().Be(token);

      var json = File.ReadAllText(path);
      json.Should().Contain("GitHubTokenProtected");
      json.Should().NotContain("ghp_legacyPlaintextToken1234567890");
      json.Should().NotContain("\"GitHubToken\"");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Save_clears_token_when_empty()
  {
    var dir = CreateTempDir();
    var path = Path.Combine(dir, SmartGuardPaths.ConfigFileName);

    try
    {
      var repo = new GuardConfigRepository(path);
      var config = GuardConfig.CreateDefault(dir);
      config.GitHubToken = "ghp_toBeClearedToken1234567890";
      repo.Save(config);

      config.GitHubToken = string.Empty;
      repo.Save(config);

      var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
      node.ContainsKey("GitHubTokenProtected").Should().BeFalse();
      node.ContainsKey("GitHubToken").Should().BeFalse();
      repo.TryLoad()!.GitHubToken.Should().BeEmpty();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  private static string CreateTempDir()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }
}
