using SmartGuard.Configuration;

namespace SmartGuard.Cli;

internal static class CliOpenHandlers
{
  public static int Run(string root, List<string> positional, TextWriter error)
  {
    if (positional.Count < 2)
    {
      error.WriteLine("Usage: SmartGuard.Cli open tray|settings|logs [--root <dir>]");
      return 1;
    }

    try
    {
      return positional[1].ToLowerInvariant() switch
      {
        "tray" => CliTrayHandlers.Start(root, error),
        "settings" => RunSettings(root),
        "logs" => RunLogs(root),
        _ => CliUsage.UnknownCommand(positional[1], error),
      };
    }
    catch (Exception ex)
    {
      error.WriteLine(ex.Message);
      return 1;
    }
  }

  private static int RunSettings(string root)
  {
    SettingsMainPageLauncher.Open(root);
    return 0;
  }

  private static int RunLogs(string root)
  {
    SettingsLogsPageLauncher.Open(root);
    return 0;
  }
}
