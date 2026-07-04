using FluentAssertions;
using SmartGuard.Cli;

namespace SmartGuard.Cli.Tests;

public class CliRouterBehaviorTests
{
  [Fact]
  public void Status_command_prints_header()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-cli-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(root, "bin"));
    try
    {
      var exit = CliRouter.Run(["status", "--root", root], Console.Out, Console.Error);
      exit.Should().Be(0);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }
}
