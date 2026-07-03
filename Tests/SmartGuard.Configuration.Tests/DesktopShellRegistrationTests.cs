using SmartGuard.Configuration.Shell;

namespace SmartGuard.Configuration.Tests;

public class DesktopShellRegistrationTests
{
  [Fact]
  public void Start_menu_shortcuts_point_to_tray_settings_and_logs_entrypoints()
  {
    var root = @"D:\Apps\SmartGuard";

    var shortcuts = DesktopShellRegistration.GetStartMenuShortcuts(root);

    shortcuts.Should().HaveCount(3);
    shortcuts.Select(x => x.TargetPath).Should().ContainInOrder(
      SmartGuardPaths.TrayExe(root),
      SmartGuardPaths.SettingsExe(root),
      SmartGuardPaths.SettingsExe(root));
    shortcuts.Select(x => x.Arguments).Should().ContainInOrder(
      $"--root \"{root}\"",
      $"--root \"{root}\"",
      $"--root \"{root}\" --page logs");
    shortcuts.Should().OnlyContain(x => x.AppUserModelId == SmartGuardToastAppId.AppId);
  }

  [Fact]
  public void Desktop_shortcut_points_to_tray_entrypoint()
  {
    var root = @"D:\Apps\SmartGuard";

    var shortcut = DesktopShellRegistration.GetDesktopShortcut(root);

    shortcut.TargetPath.Should().Be(SmartGuardPaths.TrayExe(root));
    shortcut.Arguments.Should().Be($"--root \"{root}\"");
    shortcut.AppUserModelId.Should().Be(SmartGuardToastAppId.AppId);
  }
}
