using SmartGuard.Configuration;

namespace SmartGuard.Cli;

internal static class CliTrayHandlers
{
  public static int Start(string root, TextWriter error)
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

  public static int Restart(string root, TextWriter output, TextWriter error)
  {
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
}
