namespace SmartGuard.Engine.Cli;

public static class CommandLineParser
{
  public static ParsedCommandLine Parse(string[] args)
  {
    var mode = EngineCommandMode.RunGuardian;
    string? root = null;
    var skipPublish = false;
    var createDesktopShortcut = false;
    var skipScheduledTasks = false;

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
        case "--create-desktop-shortcut" or "/create-desktop-shortcut":
          createDesktopShortcut = true;
          break;
        case "--skip-scheduled-tasks" or "/skip-scheduled-tasks":
          skipScheduledTasks = true;
          break;
        case "--root" or "-r" when i + 1 < args.Length:
          root = args[++i];
          break;
      }
    }

    return new ParsedCommandLine(mode, root, skipPublish, createDesktopShortcut, skipScheduledTasks);
  }
}
