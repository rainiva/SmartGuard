using SmartGuard.Configuration;
using SmartGuard.Configuration.Cli;
using SmartGuard.Contracts;
using SmartGuard.Engine.Cli;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine;

public static class Program
{
  public static int Main(string[] args)
  {
    var parsed = CommandLineParser.Parse(args);
    var root = InstallRootResolver.Resolve(parsed.Root, args);
    GlobalExceptionHandlers.Register("Engine", root);
    var configPath = SmartGuardPaths.ConfigFile(root);
    var statusPath = SmartGuardPaths.StatusFile(root);
    var initMarker = SmartGuardPaths.InitMarkerFile(root);
    var fallbackLog = SmartGuardPaths.StartupLogFile(root);

    return parsed.Mode switch
    {
      EngineCommandMode.Install => InstallCommands.RunInstall(
        root, parsed.SkipPublish, parsed.CreateDesktopShortcut, parsed.SkipScheduledTasks),
      EngineCommandMode.Uninstall => InstallCommands.RunUninstall(root),
      _ => RunGuardian(root, configPath, statusPath, initMarker, fallbackLog),
    };
  }

  private static int RunGuardian(
    string root,
    string configPath,
    string statusPath,
    string initMarker,
    string fallbackLog)
  {
    using var guard = SingleInstanceGuard.TryAcquire("Core");
    if (!guard.IsOwner)
    {
      Console.WriteLine("SmartGuard 核心服务已在后台运行。");
      SmartGuardLogWriter.TryAppendLine(fallbackLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 核心服务已在运行");
      return 0;
    }

    var loop = new GuardianLoop(root, configPath, statusPath, initMarker, fallbackLog);
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
      e.Cancel = true;
      cts.Cancel();
    };
    loop.RunAsync(cts.Token).GetAwaiter().GetResult();
    return 0;
  }
}
