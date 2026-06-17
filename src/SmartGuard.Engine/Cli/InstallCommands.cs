using System.Diagnostics;
using System.Security.Principal;
using SmartGuard.Configuration;

namespace SmartGuard.Engine.Cli;

public static class InstallCommands
{
  public static int RunInstall(string root, bool skipPublish)
  {
    var startupLog = Path.Combine(root, "SmartGuard.startup.log");
    if (!IsAdministrator())
      return RerunElevated(root, "--install", skipPublish ? "--skip-publish" : null, startupLog);

    try
    {
      var engineExe = InstallPaths.GetEngineExe(root);
      if (!skipPublish && !File.Exists(engineExe))
      {
        WriteStartupLog(startupLog,
          $"WARN: Engine exe not found at {engineExe}. Run scripts\\Publish-Engine.ps1 or pass --skip-publish.");
      }

      WriteStartupLog(startupLog, "Install: registering SmartGuard Guardian...");
      var guardianCode = ScheduledTaskRegistrar.RegisterGuardian(root);
      if (guardianCode != 0)
        return Fail(startupLog, $"SmartGuard Guardian task registration failed with exit code {guardianCode}");

      WriteStartupLog(startupLog, "Install: registering SmartGuard Tray...");
      var trayCode = ScheduledTaskRegistrar.RegisterTray(root);
      if (trayCode != 0)
        return Fail(startupLog, $"SmartGuard Tray task registration failed with exit code {trayCode}");

      WriteStartupLog(startupLog, "Install: completed successfully.");
      Console.WriteLine("SmartGuard scheduled tasks installed.");
      return 0;
    }
    catch (Exception ex)
    {
      return Fail(startupLog, $"Install failed: {ex.Message}");
    }
  }

  public static int RunUninstall(string root)
  {
    var startupLog = Path.Combine(root, "SmartGuard.startup.log");
    if (!IsAdministrator())
      return RerunElevated(root, "--uninstall", null, startupLog);

    try
    {
      foreach (var taskName in InstallPaths.ScheduledTaskNames)
      {
        WriteStartupLog(startupLog, $"Uninstall: removing task {taskName}...");
        TryDeleteScheduledTask(taskName);
      }

      WriteStartupLog(startupLog, "Uninstall: completed successfully.");
      Console.WriteLine("SmartGuard scheduled tasks removed.");
      return 0;
    }
    catch (Exception ex)
    {
      return Fail(startupLog, $"Uninstall failed: {ex.Message}");
    }
  }

  private static int RerunElevated(string root, string command, string? extraArg, string startupLog)
  {
    WriteStartupLog(startupLog, "Requesting UAC elevation...");
    var exe = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(exe))
      return Fail(startupLog, "Unable to resolve current executable path for elevation.");

    var args = $"--root \"{root}\" {command}";
    if (!string.IsNullOrWhiteSpace(extraArg)) args += $" {extraArg}";

    using var proc = Process.Start(new ProcessStartInfo
    {
      FileName = exe,
      Arguments = args,
      Verb = "runas",
      UseShellExecute = true,
    });

    if (proc is null)
      return Fail(startupLog, "UAC was cancelled or elevation failed.");

    proc.WaitForExit();
    WriteStartupLog(startupLog, $"Elevated process exit code: {proc.ExitCode}");
    return proc.ExitCode;
  }

  private static void TryDeleteScheduledTask(string taskName)
  {
    try
    {
      RunProcess("schtasks.exe", $"/Delete /TN \"{taskName}\" /F");
    }
    catch
    {
      // task may already be absent
    }
  }

  private static int RunProcess(string fileName, string arguments)
  {
    using var proc = Process.Start(new ProcessStartInfo
    {
      FileName = fileName,
      Arguments = arguments,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
    });
    if (proc is null) throw new InvalidOperationException($"Failed to start {fileName}");
    proc.WaitForExit();
    return proc.ExitCode;
  }

  private static bool IsAdministrator()
  {
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
  }

  private static int Fail(string startupLog, string message)
  {
    WriteStartupLog(startupLog, message);
    Console.Error.WriteLine(message);
    return 1;
  }

  private static void WriteStartupLog(string startupLog, string message)
  {
    try
    {
      var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
      File.AppendAllText(startupLog, line);
    }
    catch
    {
      // ignore logging failures
    }
  }
}
