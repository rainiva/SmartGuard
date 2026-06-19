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
  public void OpenLogViewer_tries_activate_settings_before_starting_process()
  {
    var source = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Tray", "Infrastructure.cs")));

    // LogViewer now opens through Settings window (navigated to logs page)
    source.Should().Contain("SingleInstanceActivation.TryNotifyExisting(\"Settings\")");
    source.IndexOf("TryNotifyExisting(\"Settings\")")
      .Should()
      .BeLessThan(source.IndexOf("SmartGuard.Settings.exe", StringComparison.Ordinal));
  }

  [Fact]
  public void OpenSettings_does_not_fall_back_to_powershell_scripts()
  {
    var source = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Tray", "Infrastructure.cs")));

    source.Should().NotContain("SmartGuard.Settings.ps1");
    source.Should().NotContain("powershell.exe");
  }

  [Fact]
  public void OpenLogViewer_launches_settings_with_log_page_argument()
  {
    var source = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Tray", "Infrastructure.cs")));

    // LogViewer now opens Settings with --page logs argument
    source.Should().Contain("--page logs");
    source.Should().NotContain("SmartGuard.LogViewer.exe");
  }

  [Fact]
  public void OpenLogViewer_does_not_fall_back_to_powershell_scripts()
  {
    var source = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Tray", "Infrastructure.cs")));

    source.Should().NotContain("Show-LogViewer.ps1");
    source.Should().NotContain("powershell.exe");
  }
}
