using SmartGuard.Configuration;
using SmartGuard.Configuration.Cli;

namespace SmartGuard.Cli;

internal static class CliStartHandlers
{
  public static int Run(string root, List<string> positional, string[] fullArgs, TextWriter output, TextWriter error)
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
      "tray" => CliTrayHandlers.Start(root, error),
      _ => CliUsage.UnknownCommand(positional[1], error),
    };
  }
}
