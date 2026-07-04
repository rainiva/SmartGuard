using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsUpdateCheckCoordinatorLineCountTests
{
  [Fact]
  public void SettingsUpdateCheckCoordinator_must_stay_under_300_lines()
  {
    var path = Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "SettingsUpdateCheckCoordinator.cs");
    File.ReadAllLines(path).Length.Should().BeLessThan(300);
  }
}
