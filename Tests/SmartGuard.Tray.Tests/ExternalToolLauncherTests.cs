namespace SmartGuard.Tray.Tests;

public class ExternalToolLauncherTests
{
  [Fact]
  public void OpenSettings_tries_activate_before_starting_process()
  {
    var source = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Tray", "Infrastructure.cs")));

    source.Should().Contain("SingleInstanceActivation.TryNotifyExisting(\"Settings\")");
    source.IndexOf("TryNotifyExisting(\"Settings\")")
      .Should()
      .BeLessThan(source.IndexOf("SmartGuard.Settings.exe", StringComparison.Ordinal));
  }

  [Fact]
  public void OpenLogViewer_tries_activate_before_starting_process()
  {
    var source = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Tray", "Infrastructure.cs")));

    source.Should().Contain("SingleInstanceActivation.TryNotifyExisting(\"LogViewer\")");
    source.IndexOf("TryNotifyExisting(\"LogViewer\")")
      .Should()
      .BeLessThan(source.IndexOf("SmartGuard.LogViewer.exe", StringComparison.Ordinal));
  }
}
