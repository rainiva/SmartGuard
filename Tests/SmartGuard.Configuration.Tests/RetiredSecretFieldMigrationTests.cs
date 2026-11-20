using System.Text.Json.Nodes;

namespace SmartGuard.Configuration.Tests;

public sealed class RetiredSecretFieldMigrationTests
{
  [Fact]
  public void TryLoad_removes_retired_secret_fields_without_losing_other_configuration()
  {
    var dir = CreateTempDir();
    var path = Path.Combine(dir, SmartGuardPaths.ConfigFileName);
    try
    {
      File.WriteAllText(path, """
        {
          "BalancedThresholdSec": 321,
          "GitHubToken": "legacy-plain-secret",
          "GitHubTokenProtected": "invalid-or-foreign-dpapi-value",
          "FutureSetting": "preserve-me"
        }
        """);

      var loaded = new GuardConfigRepository(path).TryLoad();

      loaded.Should().NotBeNull();
      loaded!.BalancedThresholdSec.Should().Be(321);
      var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
      node.ContainsKey("GitHubToken").Should().BeFalse();
      node.ContainsKey("GitHubTokenProtected").Should().BeFalse();
      node["FutureSetting"]!.GetValue<string>().Should().Be("preserve-me");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Save_removes_retired_secret_fields_from_existing_configuration()
  {
    var dir = CreateTempDir();
    var path = Path.Combine(dir, SmartGuardPaths.ConfigFileName);
    try
    {
      File.WriteAllText(path, """
        {
          "GitHubToken": "legacy-plain-secret",
          "GitHubTokenProtected": "legacy-protected-secret",
          "FutureSetting": 42
        }
        """);

      var repository = new GuardConfigRepository(path);
      repository.Save(GuardConfig.CreateDefault(dir));

      var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
      node.ContainsKey("GitHubToken").Should().BeFalse();
      node.ContainsKey("GitHubTokenProtected").Should().BeFalse();
      node["FutureSetting"]!.GetValue<int>().Should().Be(42);
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
