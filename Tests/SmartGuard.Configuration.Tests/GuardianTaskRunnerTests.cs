using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class GuardianTaskRunnerTests
{
  [Fact]
  public void BuildSchTasksRunArguments_targets_guardian_task()
  {
    GuardianTaskRunner.BuildSchTasksRunArguments()
      .Should().Be("/Run /TN \"SmartGuard Guardian\"");
  }
}
