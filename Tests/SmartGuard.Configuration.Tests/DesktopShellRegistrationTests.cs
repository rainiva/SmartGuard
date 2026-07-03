using SmartGuard.Configuration.Shell;
using System.Reflection;

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

  [Fact]
  public void Delete_directory_tree_removes_legacy_shortcuts_with_unicode_names()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-shell-delete-" + Guid.NewGuid().ToString("N"));
    var nested = Path.Combine(root, "SmartGuard");
    Directory.CreateDirectory(nested);
    foreach (var name in new[] { "SmartGuard 托盘.lnk", "SmartGuard 设置.lnk", "SmartGuard 日志.lnk", "卸载 SmartGuard.lnk" })
      File.WriteAllBytes(Path.Combine(nested, name), []);

    var method = typeof(DesktopShellRegistration).GetMethod(
      "DeleteDirectoryTree",
      BindingFlags.NonPublic | BindingFlags.Static);

    method.Should().NotBeNull();
    method!.Invoke(null, [nested]);

    Directory.Exists(nested).Should().BeFalse();
    Directory.Exists(root).Should().BeTrue();

    Directory.Delete(root, false);
  }

  [Fact]
  public void Shell_link_app_user_model_id_loads_shortcuts_in_writeable_mode()
  {
    var repoRoot = Path.GetFullPath(Path.Combine(
      Path.GetDirectoryName(typeof(DesktopShellRegistrationTests).Assembly.Location)!,
      "..", "..", "..", "..", ".."));
    var sourcePath = Path.Combine(repoRoot, "src", "SmartGuard.Configuration", "Shell", "ShellLinkAppUserModelId.cs");

    File.ReadAllText(sourcePath).Should().Contain("persist.Load(shortcutPath, StgmReadWrite");
    File.ReadAllText(sourcePath).Should().NotContain("persist.Load(shortcutPath, 0);");
  }

  [Fact]
  public void Desktop_shell_registration_releases_wscript_shortcut_com_objects_before_app_id_rewrite()
  {
    var repoRoot = Path.GetFullPath(Path.Combine(
      Path.GetDirectoryName(typeof(DesktopShellRegistrationTests).Assembly.Location)!,
      "..", "..", "..", "..", ".."));
    var sourcePath = Path.Combine(repoRoot, "src", "SmartGuard.Configuration", "Shell", "DesktopShellRegistration.cs");
    var source = File.ReadAllText(sourcePath);

    source.Should().Contain("Marshal.FinalReleaseComObject(link);");
    source.Should().Contain("Marshal.FinalReleaseComObject(shell);");
    source.Should().Contain("ShellLinkAppUserModelId.Apply(shortcut.ShortcutPath, shortcut.AppUserModelId);");
  }
}
