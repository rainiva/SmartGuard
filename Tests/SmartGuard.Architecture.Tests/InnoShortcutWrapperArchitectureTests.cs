using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class InnoShortcutWrapperArchitectureTests
{
  [Fact]
  public void Installer_must_not_own_user_shell_shortcuts()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    iss.Should().NotContain("[Icons]", "installer must delegate shell surfaces to DesktopShellRegistration");
    iss.Should().NotContain("SmartGuard 托盘.lnk");
    iss.Should().NotContain("SmartGuard 设置.lnk");
    iss.Should().NotContain("SmartGuard 日志.lnk");
  }

  [Fact]
  public void Shell_owner_publishes_one_start_menu_entry_and_optional_desktop_shortcut()
  {
    var shellOwner = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Shell/DesktopShellRegistration.cs");
    var liveStartMenu = SourceScanHelper.ExtractMethodBody(shellOwner, "GetStartMenuShortcuts(string root)");

    liveStartMenu.Should().Contain("SmartGuard.lnk");
    liveStartMenu.Should().NotContain("SmartGuard 托盘.lnk", "live shell surface must not include legacy tray shortcut");
    liveStartMenu.Should().NotContain("SmartGuard 设置.lnk", "live shell surface must not include dedicated settings shortcut");
    liveStartMenu.Should().NotContain("SmartGuard 日志.lnk", "live shell surface must not include dedicated logs shortcut");
    shellOwner.Should().Contain("createDesktopShortcut");
  }

  [Fact]
  public void InstallCommands_must_delegate_shell_registration_to_DesktopShellRegistration()
  {
    var installCommands = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Cli/InstallCommands.cs");
    installCommands.Should().Contain("DesktopShellRegistration.Apply");
    installCommands.Should().Contain("DesktopShellRegistration.Remove");
  }

  [Fact]
  public void Installer_must_pass_desktop_shortcut_choice_to_install_command()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var runSection = ExtractSection(iss, "[Run]");
    runSection.Should().Contain("--create-desktop-shortcut");
    runSection.Should().Contain("desktopicon");
  }

  [Fact]
  public void Runtime_shell_registration_hooks_must_be_absent_from_tray_sources()
  {
    var tray = SourceScanHelper.ReadSource("src/SmartGuard.Tray/TrayApplicationContext.cs");
    var notifier = SourceScanHelper.ReadSource("src/SmartGuard.Tray/Toast/WinRtToastNotifier.cs");
    tray.Should().NotContain("ToastAumidRegistrar.EnsureRegistered");
    notifier.Should().NotContain("ToastAumidRegistrar.EnsureRegistered");
  }

  [Fact]
  public void Cli_open_commands_must_delegate_to_launchers()
  {
    var router = string.Join(
      Environment.NewLine,
      Directory.EnumerateFiles(Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Cli"), "*.cs")
        .Select(File.ReadAllText));
    var mainLauncher = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SettingsMainPageLauncher.cs");
    var logsLauncher = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SettingsLogsPageLauncher.cs");

    router.Should().Contain("SettingsMainPageLauncher.Open");
    router.Should().Contain("SettingsLogsPageLauncher.Open");
    router.Should().Contain("TrayLauncher.Start");
    mainLauncher.Should().Contain("Arguments = $\"--root \\\"{root}\\\"\"");
    logsLauncher.Should().Contain("Arguments = $\"--root \\\"{root}\\\" --page logs\"");
  }

  private static string ExtractSection(string iss, string header)
  {
    var start = iss.IndexOf(header, StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1, $"expected section {header}");
    var next = iss.IndexOf("\r\n[", start + header.Length, StringComparison.Ordinal);
    if (next < 0)
      next = iss.IndexOf("\n[", start + header.Length, StringComparison.Ordinal);
    return next > start ? iss[start..next] : iss[start..];
  }
}
