using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class LogSearchFilterBarLineCountTests
{
  [Fact]
  public void LogSearchFilterBar_must_stay_under_300_lines()
  {
    var path = Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "LogSearchFilterBar.cs");
    File.ReadAllLines(path).Length.Should().BeLessThan(300);
  }
}
