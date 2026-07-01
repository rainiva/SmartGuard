using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class StartCoreArchitectureTests
{
  [Fact]
  public void Start_Core_must_trigger_guardian_task_not_direct_engine_foreground()
  {
    var content = SourceScanHelper.ReadSource("Start-Core.cmd");
    content.Should().NotMatchRegex(@"SmartGuard\.Engine\.exe[\s\S]*--root", "direct foreground engine launch belongs in Debug-Engine.cmd");
    content.Should().Contain("schtasks");
    content.Should().Contain(ScheduledTaskRegistrar.GuardianTaskName);
  }

  [Fact]
  public void Debug_Engine_must_support_foreground_engine_for_dev()
  {
    var content = SourceScanHelper.ReadSource("Debug-Engine.cmd");
    content.Should().MatchRegex(@"SmartGuard\.Engine\.exe[\s\S]*--root");
  }
}
