using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class PublishAllReferenceArchitectureTests
{
  private static readonly string[] RuntimeErrorSources =
  [
    "Tests/Integration/SmartGuardStop.ps1",
    "Tests/Integration/TrayCoreUserFlow.Helpers.ps1",
    "Tests/Integration/InstallerUserFlow.Helpers.ps1",
    "src/SmartGuard.Configuration/ScheduledTaskRegistrar.cs",
    "src/SmartGuard.Engine/Cli/InstallCommands.cs",
    "Start-Core.cmd",
    "Debug-Engine.cmd",
    "Register-AllTasks.cmd",
    "Tests/Integration/TrayCoreUserFlow.Tests.ps1",
  ];

  [Theory]
  [MemberData(nameof(RuntimeErrorSourcesMemberData))]
  public void Runtime_error_messages_must_not_reference_removed_Publish_All_script(string relativePath)
  {
    var content = SourceScanHelper.ReadSource(relativePath);
    content.Should().NotContain("Publish-All.ps1", $"{relativePath} should direct users to build.cmd");
  }

  public static IEnumerable<object[]> RuntimeErrorSourcesMemberData()
    => RuntimeErrorSources.Select(path => new object[] { path });
}
