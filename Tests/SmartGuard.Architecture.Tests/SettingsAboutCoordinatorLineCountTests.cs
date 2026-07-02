using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsAboutCoordinatorLineCountTests
{
  [Fact]
  public void SettingsAboutCoordinator_must_stay_under_300_lines()
  {
    var path = Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "SettingsAboutCoordinator.cs");
    File.ReadAllLines(path).Length.Should().BeLessThan(300);
  }

  [Fact]
  public void SettingsUpdateCheckCoordinator_must_exist_as_split_module()
  {
    var path = Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "SettingsUpdateCheckCoordinator.cs");
    File.Exists(path).Should().BeTrue();
    File.ReadAllLines(path).Length.Should().BeLessThan(300);
  }
}
