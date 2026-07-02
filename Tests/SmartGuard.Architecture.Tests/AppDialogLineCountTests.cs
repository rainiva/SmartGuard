using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class AppDialogLineCountTests
{
  [Fact]
  public void AppDialog_must_stay_under_300_lines()
  {
    var path = Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Settings", "AppDialog.cs");
    File.ReadAllLines(path).Length.Should().BeLessThan(300);
  }

  [Fact]
  public void AppDialogButtonFactory_must_exist_as_split_module()
  {
    var path = Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Settings", "AppDialogButtonFactory.cs");
    File.Exists(path).Should().BeTrue();
    File.ReadAllLines(path).Length.Should().BeLessThan(300);
  }
}
