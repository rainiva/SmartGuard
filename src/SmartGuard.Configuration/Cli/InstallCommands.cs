using System.Diagnostics;
using System.Security.Principal;
using SmartGuard.Configuration.Shell;

namespace SmartGuard.Configuration.Cli;

public static class ElevationDeclinedMarker
{
  public static bool Exists(string root)
  {
    try
    {
      return File.Exists(SmartGuardPaths.ElevationDeclinedMarkerFile(root));
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
      File.WriteAllText(SmartGuardPaths.ElevationDeclinedMarkerFile(root), string.Empty);
    }
    catch
    {
      // marker creation is best-effort
    }
  }
}

public static class InstallCommands
{
  public static int RunInstall(string root, bool skipPublish, bool createDesktopShortcut = false, bool skipScheduledTasks = false)
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
      return RerunElevated(
        root,
        "--install",
        startupLog,
        skipPublish ? "--skip-publish" : null,
        createDesktopShortcut ? "--create-desktop-shortcut" : null,
        skipScheduledTasks ? "--skip-scheduled-tasks" : null);
    }

    try
    {
      LegacyTaskCleaner.CleanLegacyTasks();
      var engineExe = SmartGuardPaths.EngineExe(root);
      if (!skipPublish && !File.Exists(engineExe))
      {
        WriteStartupLog(startupLog,
          $"WARN: Engine exe not found at {engineExe}. Run build.cmd, or pass --skip-publish.");
      }

      if (ScheduledTaskInstallGate.ShouldSkipFromEnvironment(skipScheduledTasks))
      {
        WriteStartupLog(startupLog,
          "Install: skipping scheduled task registration and desktop shell (SMARTGUARD_SKIP_SCHEDULED_TASKS / --skip-scheduled-tasks).");
        WriteStartupLog(startupLog, "Install: completed successfully.");
        Console.WriteLine("SmartGuard install completed (scheduled tasks and desktop shell skipped).");
        return 0;
      }

      WriteStartupLog(startupLog, "Install: registering SmartGuard Guardian...");
      var guardianCode = ScheduledTaskRegistrar.RegisterGuardian(root);
      if (guardianCode != 0)
        return Fail(startupLog, $"SmartGuard Guardian task registration failed with exit code {guardianCode}");

      WriteStartupLog(startupLog, "Install: registering SmartGuard Tray...");
      var trayCode = ScheduledTaskRegistrar.RegisterTray(root);
      if (trayCode != 0)
        return Fail(startupLog, $"SmartGuard Tray task registration failed with exit code {trayCode}");

      WriteStartupLog(startupLog, "Install: registering desktop shell integration...");
      DesktopShellRegistration.Apply(root, createDesktopShortcut);

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
      return RerunElevated(root, "--uninstall", startupLog);
    }

    try
    {
      LegacyTaskCleaner.CleanLegacyTasks();

      WriteStartupLog(startupLog, "Uninstall: stopping running processes and removing tasks...");
      EngineLifecycle.StopForUninstall();
      DesktopShellRegistration.Remove(root);

      WriteStartupLog(startupLog, "Uninstall: completed successfully.");
      Console.WriteLine("SmartGuard scheduled tasks removed.");
      return 0;
    }
    catch (Exception ex)
    {
      return Fail(startupLog, $"Uninstall failed: {ex.Message}");
    }
  }

  private static int RerunElevated(string root, string command, string startupLog, params string?[] extraArgs)
  {
    WriteStartupLog(startupLog, "Requesting UAC elevation...");
    var args = $"--root \"{root}\" {command}";
    foreach (var extraArg in extraArgs)
    {
      if (!string.IsNullOrWhiteSpace(extraArg))
        args += $" {extraArg}";
    }

    var engineExe = SmartGuardPaths.EngineExe(root);
    using var proc = ElevationHelper.StartElevated(engineExe, args);

    if (proc is null)
    {
      ElevationDeclinedMarker.Create(root);
      return Fail(startupLog, "UAC was cancelled or elevation failed.");
    }

    proc.WaitForExit();
    WriteStartupLog(startupLog, $"Elevated process exit code: {proc.ExitCode}");
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
    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
    SmartGuardLogWriter.TryAppendSupplementalLines(startupLog, [line]);
  }
}
