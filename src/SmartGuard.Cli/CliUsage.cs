using System.Text;

namespace SmartGuard.Cli;

internal static class CliUsage
{
  public static int UnknownCommand(string command, TextWriter error)
  {
    error.WriteLine($"Unknown command: {command}");
    Print(error);
    return 1;
  }

  public static void Print(TextWriter error)
  {
    var usage = new StringBuilder();
    usage.AppendLine("SmartGuard.Cli usage:");
    usage.AppendLine("  install [--skip-publish] [--skip-scheduled-tasks] [--root <dir>]");
    usage.AppendLine("  uninstall [--root <dir>]");
    usage.AppendLine("  start guardian|engine|tray [--root <dir>]");
    usage.AppendLine("  restart tray [--root <dir>]");
    usage.AppendLine("  open tray|settings|logs [--root <dir>]");
    usage.AppendLine("  publish [Release|Debug] [--root <dir>]");
    usage.AppendLine("  status [--root <dir>]");
    error.Write(usage.ToString());
  }
}
