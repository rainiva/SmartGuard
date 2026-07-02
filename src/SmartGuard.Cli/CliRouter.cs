using SmartGuard.Configuration;
using SmartGuard.Configuration.Cli;

namespace SmartGuard.Cli;

internal static class CliRouter
{
  public static int Run(string[] args, TextWriter output, TextWriter error)
  {
    if (args.Length == 0)
    {
      CliUsage.Print(error);
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
      CliUsage.Print(error);
      return 1;
    }

    return positional[0].ToLowerInvariant() switch
    {
      "install" => RunInstall(root, positional),
      "uninstall" => InstallCommands.RunUninstall(root),
      "start" => CliStartHandlers.Run(root, positional, args, output, error),
      "restart" => RunRestart(root, positional, output, error),
      "open" => CliOpenHandlers.Run(root, positional, error),
      "status" => RunStatus(root, output),
      "publish" => CliPublishHandlers.Run(root, positional, output, error),
      _ => CliUsage.UnknownCommand(positional[0], error),
    };
  }

  private static int RunInstall(string root, List<string> positional)
  {
    var skipPublish = positional.Contains("--skip-publish", StringComparer.OrdinalIgnoreCase);
    return InstallCommands.RunInstall(root, skipPublish);
  }

  private static int RunRestart(string root, List<string> positional, TextWriter output, TextWriter error)
  {
    if (positional.Count < 2 || !positional[1].Equals("tray", StringComparison.OrdinalIgnoreCase))
    {
      error.WriteLine("Usage: SmartGuard.Cli restart tray [--root <dir>]");
      return 1;
    }

    return CliTrayHandlers.Restart(root, output, error);
  }

  private static int RunStatus(string root, TextWriter output)
  {
    output.Write(StatusReporter.FormatStatus(root));
    return 0;
  }
}
