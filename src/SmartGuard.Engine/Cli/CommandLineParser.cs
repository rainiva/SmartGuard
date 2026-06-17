namespace SmartGuard.Engine.Cli;

public static class CommandLineParser
{
  public static ParsedCommandLine Parse(string[] args)
  {
    var mode = EngineCommandMode.RunGuardian;
    string? root = null;
    var skipPublish = false;

    for (var i = 0; i < args.Length; i++)
    {
      var arg = args[i];
      switch (arg)
      {
        case "--install" or "/install":
          mode = EngineCommandMode.Install;
          break;
        case "--uninstall" or "/uninstall":
          mode = EngineCommandMode.Uninstall;
          break;
        case "--skip-publish" or "/skip-publish":
          skipPublish = true;
          break;
        case "--root" or "-r" when i + 1 < args.Length:
          root = args[++i];
          break;
      }
    }

    return new ParsedCommandLine(mode, root, skipPublish);
  }
}
