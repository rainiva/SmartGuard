namespace SmartGuard.Tray.Tests;

using SmartGuard.Tray.Toast;

public class ToastShortcutResolverTests
{
  [Fact]
  public void Resolve_prefers_tray_exe_with_root_argument()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardToastTests", Guid.NewGuid().ToString("N"));
    var bin = Path.Combine(root, "bin");
    Directory.CreateDirectory(bin);
    var trayExe = Path.Combine(bin, "SmartGuard.Tray.exe");
    File.WriteAllText(trayExe, string.Empty);

    try
    {
      var target = ToastShortcutResolver.Resolve(root);
      target.TargetPath.Should().Be(trayExe);
      target.Arguments.Should().Be($"--root \"{root}\"");
      target.WorkingDirectory.Should().Be(root);
    }
    finally
    {
      if (Directory.Exists(root)) Directory.Delete(root, true);
    }
  }

  [Fact]
  public void Resolve_falls_back_to_start_tray_cmd_when_exe_missing()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardToastTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var startTray = Path.Combine(root, "Start-Tray.cmd");
    File.WriteAllText(startTray, "@echo off\r\n");

    try
    {
      var target = ToastShortcutResolver.Resolve(root);
      target.TargetPath.Should().Be(startTray);
      target.Arguments.Should().BeEmpty();
      target.WorkingDirectory.Should().Be(root);
    }
    finally
    {
      if (Directory.Exists(root)) Directory.Delete(root, true);
    }
  }
}
