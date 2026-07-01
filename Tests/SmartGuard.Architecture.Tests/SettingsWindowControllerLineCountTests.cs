using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsWindowControllerLineCountTests
{
  [Fact]
  public void SettingsWindowController_must_stay_under_300_lines()
  {
    var path = Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "SettingsWindowController.cs");
    var lineCount = File.ReadAllLines(path).Length;
    lineCount.Should().BeLessThan(300, "controller should be a thin shell delegating to coordinators");
  }
}
