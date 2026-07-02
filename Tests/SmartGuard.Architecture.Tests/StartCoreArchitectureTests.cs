using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class StartCoreArchitectureTests
{
  [Fact]
  public void Start_Core_must_delegate_to_Cli_start_guardian()
  {
    var content = SourceScanHelper.ReadSource("Start-Core.cmd");
    content.Should().Contain("SmartGuard.Cli.exe");
    content.Should().Contain("start guardian");
    content.Should().NotMatchRegex(@"schtasks\s+/Run");
  }

  [Fact]
  public void Debug_Engine_must_delegate_to_Cli_start_engine()
  {
    var content = SourceScanHelper.ReadSource("Debug-Engine.cmd");
    content.Should().Contain("SmartGuard.Cli.exe");
    content.Should().Contain("start engine");
  }
}
