using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsPolicyCoordinatorLineCountTests
{
  [Fact]
  public void SettingsPolicyCoordinator_must_stay_under_300_lines()
  {
    var path = Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "SettingsPolicyCoordinator.cs");
    File.ReadAllLines(path).Length.Should().BeLessThan(300);
  }

  [Fact]
  public void SettingsPlanCatalogCoordinator_must_exist_as_split_module()
  {
    var path = Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "SettingsPlanCatalogCoordinator.cs");
    File.Exists(path).Should().BeTrue();
    File.ReadAllLines(path).Length.Should().BeLessThan(300);
  }
}
