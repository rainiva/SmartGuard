namespace SmartGuard.Tray.Tests;

public class ExternalToolLauncherTests
{
  [Fact]
  public void OpenSettings_tries_activate_before_starting_process()
  {
    var launcher = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Configuration", "SettingsMainPageLauncher.cs")));

    launcher.Should().Contain("SingleInstanceActivation.TryNotifyExisting(\"Settings\")");
    launcher.IndexOf("TryNotifyExisting(\"Settings\")")
      .Should()
      .BeLessThan(launcher.IndexOf("SmartGuardPaths.SettingsExe", StringComparison.Ordinal));
  }

  [Fact]
  public void OpenSettings_launches_via_main_page_launcher()
  {
    var tray = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Tray", "Infrastructure.cs")));
    tray.Should().Contain("SettingsMainPageLauncher.Open");

    var launcher = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Configuration", "SettingsMainPageLauncher.cs")));
    launcher.Should().NotContain("--page logs");
  }

  [Fact]
  public void OpenLogViewer_tries_activate_settings_before_starting_process()
  {
    var launcher = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Configuration", "SettingsLogsPageLauncher.cs")));

    launcher.Should().Contain("SingleInstanceActivation.TryNotifyExisting(\"Settings\", \"logs\")");
    launcher.IndexOf("TryNotifyExisting(\"Settings\", \"logs\")")
      .Should()
      .BeLessThan(launcher.IndexOf("SmartGuardPaths.SettingsExe", StringComparison.Ordinal));
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
    var tray = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Tray", "Infrastructure.cs")));
    tray.Should().Contain("SettingsLogsPageLauncher.Open");

    var launcher = File.ReadAllText(
      Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "SmartGuard.Configuration", "SettingsLogsPageLauncher.cs")));
    launcher.Should().Contain("--page logs");
    launcher.Should().NotContain("SmartGuard.LogViewer.exe");
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
