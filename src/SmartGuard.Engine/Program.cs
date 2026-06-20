using SmartGuard.Contracts;
using SmartGuard.Engine.Cli;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine;

public static class Program
{
  public static int Main(string[] args)
  {
    var parsed = CommandLineParser.Parse(args);
    var root = RootResolver.Resolve(parsed.Root, args);
    var configPath = Path.Combine(root, "SmartGuard.config.json");
    var statusPath = Path.Combine(root, "SmartGuard.status.json");
    var initMarker = Path.Combine(root, ".SmartGuard.initialized");
    var fallbackLog = Path.Combine(root, "SmartGuard.startup.log");

    return parsed.Mode switch
    {
      EngineCommandMode.Install => InstallCommands.RunInstall(root, parsed.SkipPublish),
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
      try
      {
        File.AppendAllText(fallbackLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 核心服务已在运行{Environment.NewLine}");
      }
      catch { /* ignore */ }
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
