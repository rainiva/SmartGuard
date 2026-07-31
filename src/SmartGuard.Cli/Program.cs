using SmartGuard.Configuration;

namespace SmartGuard.Cli;

internal static class Program
{
  public static int Main(string[] args)
  {
    var root = InstallRootResolver.Resolve(null, args);
    GlobalExceptionHandlers.Register("Cli", root);
    return CliRouter.Run(args, Console.Out, Console.Error);
  }
}
