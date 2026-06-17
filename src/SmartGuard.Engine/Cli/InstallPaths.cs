namespace SmartGuard.Engine.Cli;

public static class InstallPaths
{
  public static IReadOnlyList<string> ScheduledTaskNames { get; } =
    new[] { "SmartGuard Guardian", "SmartGuard Tray" };

  public static string GetGuardianRegisterScript(string root) =>
    Path.Combine(root, "Register-SmartGuardTask.ps1");

  public static string GetTrayRegisterScript(string root) =>
    Path.Combine(root, "Register-TrayTask.ps1");

  public static string GetEngineExe(string root) =>
    Path.Combine(root, "bin", "SmartGuard.Engine.exe");
}
