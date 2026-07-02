using System.Diagnostics;
using System.Security.Principal;
using SmartGuard.Configuration;

namespace SmartGuard.Engine.Cli;

public static class ElevationDeclinedMarker
{
  private const string MarkerFileName = ".SmartGuard.elevation-declined";

  public static bool Exists(string root)
  {
    try
    {
      return File.Exists(Path.Combine(root, MarkerFileName));
    }
    catch
    {
      return false;
    }
  }

  public static void Create(string root)
  {
    try
    {
      File.WriteAllText(Path.Combine(root, MarkerFileName), string.Empty);
    }
    catch
    {
      // marker creation is best-effort
    }
  }
}

public static class InstallCommands
{
  public static int RunInstall(string root, bool skipPublish)
  {
    var startupLog = SmartGuardPaths.StartupLogFile(root);
    if (!IsAdministrator())
    {
      if (ElevationDeclinedMarker.Exists(root))
      {
        WriteStartupLog(startupLog, "Install: elevation was previously declined, skipping UAC prompt.");
        Console.Error.WriteLine("Install requires administrator privileges. Run as administrator or delete the elevation marker file to retry.");
        return 1;
      }
      return RerunElevated(root, "--install", skipPublish ? "--skip-publish" : null, startupLog);
    }

    try
    {
      LegacyTaskCleaner.CleanLegacyTasks();
      var engineExe = InstallPaths.GetEngineExe(root);
      if (!skipPublish && !File.Exists(engineExe))
      {
        WriteStartupLog(startupLog,
          $"WARN: Engine exe not found at {engineExe}. Run build.cmd, or pass --skip-publish.");
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
    var startupLog = SmartGuardPaths.StartupLogFile(root);
    if (!IsAdministrator())
    {
      if (ElevationDeclinedMarker.Exists(root))
      {
        WriteStartupLog(startupLog, "Uninstall: elevation was previously declined, skipping UAC prompt.");
        Console.Error.WriteLine("Uninstall requires administrator privileges. Run as administrator or delete the elevation marker file to retry.");
        return 1;
      }
      return RerunElevated(root, "--uninstall", null, startupLog);
    }

    try
    {
      LegacyTaskCleaner.CleanLegacyTasks();

      WriteStartupLog(startupLog, "Uninstall: stopping running processes and removing tasks...");
      EngineLifecycle.StopForUninstall();

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
    {
      ElevationDeclinedMarker.Create(root);
      return Fail(startupLog, "UAC was cancelled or elevation failed.");
    }

    proc.WaitForExit();
    WriteStartupLog(startupLog, $"Elevated process exit code: {proc.ExitCode}");
    return proc.ExitCode;
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
