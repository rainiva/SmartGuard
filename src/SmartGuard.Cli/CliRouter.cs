using System.Text;
using SmartGuard.Configuration;
using SmartGuard.Configuration.Cli;

namespace SmartGuard.Cli;

internal static class CliRouter
{
  public static int Run(string[] args, TextWriter output, TextWriter error)
  {
    if (args.Length == 0)
    {
      PrintUsage(error);
      return 1;
    }

    string? root = null;
    var positional = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
      if (args[i] is "--root" or "-r" && i + 1 < args.Length)
      {
        root = args[++i];
        continue;
      }

      positional.Add(args[i]);
    }

    try
    {
      root ??= InstallRootResolver.Resolve(null, args);
    }
    catch (Exception ex)
    {
      error.WriteLine(ex.Message);
      return 1;
    }

    if (positional.Count == 0)
    {
      PrintUsage(error);
      return 1;
    }

    return positional[0].ToLowerInvariant() switch
    {
      "install" => RunInstall(root, positional, error),
      "uninstall" => InstallCommands.RunUninstall(root),
      "start" => RunStart(root, positional, args, error, output),
      "restart" => RunRestart(root, positional, error, output),
      "status" => RunStatus(root, output),
      _ => UnknownCommand(positional[0], error),
    };
  }

  private static int RunInstall(string root, List<string> positional, TextWriter error)
  {
    var skipPublish = positional.Contains("--skip-publish", StringComparer.OrdinalIgnoreCase);
    return InstallCommands.RunInstall(root, skipPublish);
  }

  private static int RunStart(string root, List<string> positional, string[] fullArgs, TextWriter error, TextWriter output)
  {
    if (positional.Count < 2)
    {
      error.WriteLine("Usage: SmartGuard.Cli start guardian|engine|tray [--root <dir>]");
      return 1;
    }

    return positional[1].ToLowerInvariant() switch
    {
      "guardian" => CliAdminRunner.RunElevatedIfNeeded(fullArgs, () =>
      {
        output.WriteLine("[SmartGuard] Starting Guardian scheduled task...");
        GuardianTaskRunner.Run();
        output.WriteLine("Guardian task started.");
        return 0;
      }),
      "engine" => CliAdminRunner.RunElevatedIfNeeded(fullArgs, () =>
      {
        output.WriteLine("[SmartGuard] Starting engine in foreground (dev only)...");
        return EngineLauncher.StartForeground(root);
      }),
      "tray" => RunTrayStart(root, error),
      _ => UnknownCommand(positional[1], error),
    };
  }

  private static int RunTrayStart(string root, TextWriter error)
  {
    try
    {
      TrayLauncher.Start(root);
      return 0;
    }
    catch (Exception ex)
    {
      error.WriteLine(ex.Message);
      return 1;
    }
  }

  private static int RunRestart(string root, List<string> positional, TextWriter error, TextWriter output)
  {
    if (positional.Count < 2 || !positional[1].Equals("tray", StringComparison.OrdinalIgnoreCase))
    {
      error.WriteLine("Usage: SmartGuard.Cli restart tray [--root <dir>]");
      return 1;
    }

    try
    {
      TrayLifecycle.Restart(root);
      output.WriteLine("Tray restarted. Check the notification area.");
      return 0;
    }
    catch (Exception ex)
    {
      error.WriteLine(ex.Message);
      return 1;
    }
  }

  private static int RunStatus(string root, TextWriter output)
  {
    output.Write(StatusReporter.FormatStatus(root));
    return 0;
  }

  private static int UnknownCommand(string command, TextWriter error)
  {
    error.WriteLine($"Unknown command: {command}");
    PrintUsage(error);
    return 1;
  }

  private static void PrintUsage(TextWriter error)
  {
    var usage = new StringBuilder();
    usage.AppendLine("SmartGuard.Cli usage:");
    usage.AppendLine("  install [--skip-publish] [--root <dir>]");
    usage.AppendLine("  uninstall [--root <dir>]");
    usage.AppendLine("  start guardian|engine|tray [--root <dir>]");
    usage.AppendLine("  restart tray [--root <dir>]");
    usage.AppendLine("  status [--root <dir>]");
    error.Write(usage.ToString());
  }
}
