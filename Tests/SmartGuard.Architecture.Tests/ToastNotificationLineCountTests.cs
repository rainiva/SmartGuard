using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ToastNotificationLineCountTests
{
  [Theory]
  [InlineData("InlineToastNotification.cs")]
  [InlineData("FloatingToastNotification.cs")]
  [InlineData("ToastNotificationService.cs")]
  public void Toast_module_files_must_stay_under_300_lines(string fileName)
  {
    var path = Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Settings", fileName);
    File.Exists(path).Should().BeTrue($"expected split toast file {fileName}");
    File.ReadAllLines(path).Length.Should().BeLessThan(300);
  }

  [Fact]
  public void Legacy_monolithic_ToastNotification_cs_must_be_removed()
  {
    var path = Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Settings", "ToastNotification.cs");
    File.Exists(path).Should().BeFalse("toast UI was split into dedicated files");
  }
}
