namespace SmartGuard.Cli;

internal static class Program
{
  public static int Main(string[] args)
    => CliRouter.Run(args, Console.Out, Console.Error);
}
