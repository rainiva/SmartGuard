using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class GuardianIterationRunnerArchitectureTests
{
  [Fact]
  public void GuardianLoop_delegates_iteration_to_GuardianIterationRunner()
  {
    var loop = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs");
    loop.Should().NotContain("private async Task ProcessIterationAsync");
    loop.Should().Contain("GuardianIterationRunner");

    var lineCount = File.ReadAllLines(
      Path.Combine(SourceScanHelper.RepoRoot, "src/SmartGuard.Engine/Worker/GuardianLoop.cs")).Length;
    lineCount.Should().BeLessThan(200);
  }

  [Fact]
  public void GuardianIterationRunner_composes_notifications_via_NotificationEventComposer()
  {
    var runner = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs");
    runner.Should().Contain("NotificationEventComposer.Compose");
    runner.Should().Contain("NotifyOnPlanChange");
    runner.Should().Contain("NotificationEventComposer.ApplyCooldown");
  }
}
