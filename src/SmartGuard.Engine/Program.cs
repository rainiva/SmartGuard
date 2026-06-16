using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine;

public static class Program
{
  public static int Main(string[] args)
  {
    var root = ResolveRoot(args);
    var configPath = Path.Combine(root, "SmartGuard.config.json");
    var statusPath = Path.Combine(root, "SmartGuard.status.json");
    var initMarker = Path.Combine(root, ".SmartGuard.initialized");
    var fallbackLog = Path.Combine(root, "SmartGuard.startup.log");

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
    loop.Run(cts.Token);
    return 0;
  }

  private static string ResolveRoot(string[] args)
  {
    for (var i = 0; i < args.Length - 1; i++)
    {
      if (args[i] is "--root" or "-r")
        return Path.GetFullPath(args[i + 1]);
    }
    var env = Environment.GetEnvironmentVariable("SMARTGUARD_ROOT");
    if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);

    var dir = AppContext.BaseDirectory;
    for (var depth = 0; depth < 6; depth++)
    {
      if (File.Exists(Path.Combine(dir, "SmartGuard.config.json")))
        return Path.GetFullPath(dir);
      var parent = Directory.GetParent(dir);
      if (parent is null) break;
      dir = parent.FullName;
    }
    return @"D:\Project\SmartGuard";
  }
}
