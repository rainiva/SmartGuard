using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class AdversaryArchitectureTests
{
  [Fact]
  public void Adversary_entry_and_untrusted_observation_boundary_are_registered()
    => SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md").Should().Contain("ADV-A1-OBSERVATION");

  [Fact]
  public void Product_projects_do_not_reference_development_adversary_tool()
  {
    var root = SourceScanHelper.RepoRoot;
    foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
      File.ReadAllText(file, System.Text.Encoding.UTF8).Should().NotContain("SmartGuard.Adversary");
  }
}
