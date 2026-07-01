using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsLogPageHostLineCountTests
{
  [Fact]
  public void SettingsLogPageHost_must_stay_under_300_lines()
  {
    var path = Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "SettingsLogPageHost.cs");
    File.ReadAllLines(path).Length.Should().BeLessThan(300);
  }
}
