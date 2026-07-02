using System.Diagnostics;
using System.Text;

namespace SmartGuard.Configuration;

public enum ScheduledTaskRunLevel
{
  Highest,
  Limited,
}

public sealed record ScheduledTaskLaunchSpec(string ExecutePath, string Arguments, string WorkingDirectory);

public static class ScheduledTaskRegistrar
{
  public const string GuardianTaskName = "SmartGuard Guardian";
  public const string TrayTaskName = "SmartGuard Tray";

  public static IReadOnlyList<string> TaskNames { get; } = [GuardianTaskName, TrayTaskName];

  public static ScheduledTaskLaunchSpec BuildGuardianLaunchSpec(string root)
  {
    var engineExe = SmartGuardPaths.EngineExe(root);
    if (!File.Exists(engineExe))
    {
      throw new FileNotFoundException(
        "SmartGuard.Engine.exe not found. Run build.cmd, or reinstall SmartGuard.",
        engineExe);
    }

    return new ScheduledTaskLaunchSpec(engineExe, $"--root \"{root}\"", root);
  }

  public static ScheduledTaskLaunchSpec BuildTrayLaunchSpec(string root)
  {
    var trayExe = SmartGuardPaths.TrayExe(root);
    if (!File.Exists(trayExe))
    {
      throw new FileNotFoundException(
        "SmartGuard.Tray.exe not found. Run build.cmd, or reinstall SmartGuard.",
        trayExe);
    }

    return new ScheduledTaskLaunchSpec(trayExe, $"--root \"{root}\"", root);
  }

  public static string BuildTaskXml(string taskName, ScheduledTaskLaunchSpec launch, ScheduledTaskRunLevel runLevel)
  {
    var runLevelXml = runLevel == ScheduledTaskRunLevel.Highest
      ? "HighestAvailable"
      : "LeastPrivilege";

    return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>{XmlEscape(taskName)}</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>{runLevelXml}</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <RestartOnFailure>
                  <Interval>PT1M</Interval>
                  <Count>3</Count>
                </RestartOnFailure>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{XmlEscape(launch.ExecutePath)}</Command>
                  <Arguments>{XmlEscape(launch.Arguments)}</Arguments>
                  <WorkingDirectory>{XmlEscape(launch.WorkingDirectory)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
  }

  public static int RegisterGuardian(string root)
    => RegisterTask(GuardianTaskName, BuildGuardianLaunchSpec(root), ScheduledTaskRunLevel.Highest);

  public static int RegisterTray(string root)
    => RegisterTask(TrayTaskName, BuildTrayLaunchSpec(root), ScheduledTaskRunLevel.Limited);

  public static int RegisterAll(string root)
  {
    var guardianCode = RegisterGuardian(root);
    if (guardianCode != 0) return guardianCode;
    return RegisterTray(root);
  }

  public static void RegisterIfMissing(string taskName, string root)
  {
    if (TaskExists(taskName)) return;

    if (string.Equals(taskName, GuardianTaskName, StringComparison.Ordinal))
      RegisterGuardian(root);
    else if (string.Equals(taskName, TrayTaskName, StringComparison.Ordinal))
      RegisterTray(root);
  }

  private static int RegisterTask(string taskName, ScheduledTaskLaunchSpec launch, ScheduledTaskRunLevel runLevel)
  {
    var xml = BuildTaskXml(taskName, launch, runLevel);
    var tempFile = Path.Combine(Path.GetTempPath(), $"smartguard-task-{Guid.NewGuid():N}.xml");
    try
    {
      File.WriteAllText(tempFile, xml, Encoding.Unicode);
      return RunSchTasks($"/Create /TN \"{taskName}\" /XML \"{tempFile}\" /F");
    }
    finally
    {
      try { File.Delete(tempFile); } catch { /* ignore */ }
    }
  }

  private static bool TaskExists(string taskName)
  {
    try
    {
      using var process = Process.Start(new ProcessStartInfo
      {
        FileName = "schtasks.exe",
        Arguments = $"/Query /TN \"{taskName}\" /FO LIST",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        CreateNoWindow = true,
      });
      if (process is null) return false;
      process.WaitForExit();
      return process.ExitCode == 0;
    }
    catch
    {
      return false;
    }
  }

  private static int RunSchTasks(string arguments)
  {
    using var process = Process.Start(new ProcessStartInfo
    {
      FileName = "schtasks.exe",
      Arguments = arguments,
      UseShellExecute = false,
      CreateNoWindow = true,
    });
    if (process is null) return 1;
    process.WaitForExit();
    return process.ExitCode;
  }

  private static string XmlEscape(string value) =>
    value
      .Replace("&", "&amp;", StringComparison.Ordinal)
      .Replace("<", "&lt;", StringComparison.Ordinal)
      .Replace(">", "&gt;", StringComparison.Ordinal)
      .Replace("\"", "&quot;", StringComparison.Ordinal);
}
