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
}
