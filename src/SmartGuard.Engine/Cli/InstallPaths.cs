namespace SmartGuard.Engine.Cli;

public static class InstallPaths
{
  public static IReadOnlyList<string> ScheduledTaskNames =>
    SmartGuard.Configuration.ScheduledTaskRegistrar.TaskNames;

  public static string GetEngineExe(string root) =>
    Path.Combine(root, "bin", "SmartGuard.Engine.exe");
}
