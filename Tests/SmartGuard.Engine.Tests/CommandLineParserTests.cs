using SmartGuard.Engine.Cli;

namespace SmartGuard.Engine.Tests;

public class CommandLineParserTests
{
  [Theory]
  [InlineData(new[] { "--install" }, EngineCommandMode.Install)]
  [InlineData(new[] { "/install" }, EngineCommandMode.Install)]
  [InlineData(new[] { "--uninstall" }, EngineCommandMode.Uninstall)]
  [InlineData(new[] { "/uninstall" }, EngineCommandMode.Uninstall)]
  [InlineData(new string[0], EngineCommandMode.RunGuardian)]
  public void Parse_recognizes_command_mode(string[] args, EngineCommandMode expected)
  {
    CommandLineParser.Parse(args).Mode.Should().Be(expected);
  }

  [Fact]
  public void Parse_reads_root_before_install()
  {
    var parsed = CommandLineParser.Parse(new[] { "--root", @"D:\apps\SmartGuard", "--install" });
    parsed.Mode.Should().Be(EngineCommandMode.Install);
    parsed.Root.Should().Be(@"D:\apps\SmartGuard");
  }

  [Fact]
  public void Parse_reads_skip_publish_flag()
  {
    CommandLineParser.Parse(new[] { "--install", "--skip-publish" }).SkipPublish.Should().BeTrue();
  }
}
