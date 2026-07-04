using SmartGuard.Configuration.Build;

namespace SmartGuard.Cli;

internal static class CliPublishHandlers
{
  public static int Run(string root, List<string> positional, TextWriter output, TextWriter error)
  {
    var configuration = "Release";
    if (positional.Count >= 2 && !positional[1].StartsWith('-'))
      configuration = positional[1];

    output.WriteLine($"Publishing SmartGuard desktop suite ({configuration}, framework-dependent win-x64)...");
    var exit = DesktopSuitePublisher.Publish(root, configuration);
    if (exit != 0)
    {
      error.WriteLine($"Publish failed with exit code {exit}");
      return exit;
    }

    output.WriteLine($"Published to {Path.Combine(root, "bin")}");
    return 0;
  }
}
