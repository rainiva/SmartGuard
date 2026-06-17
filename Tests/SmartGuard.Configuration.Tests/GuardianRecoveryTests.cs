namespace SmartGuard.Configuration.Tests;

public class GuardianRecoveryTests
{
  [Theory]
  [InlineData(0, false)]
  [InlineData(1, false)]
  [InlineData(2, false)]
  [InlineData(3, true)]
  [InlineData(5, true)]
  public void ShouldAttemptStart_after_threshold(int missedReads, bool expected)
  {
    GuardianRecovery.ShouldAttemptStart(missedReads).Should().Be(expected);
  }

  [Fact]
  public void GetEngineExecutablePath_points_to_bin_engine()
  {
    GuardianRecovery.GetEngineExecutablePath(@"D:\SmartGuard")
      .Should().Be(@"D:\SmartGuard\bin\SmartGuard.Engine.exe");
  }

  [Fact]
  public void BuildSchTasksRunArguments_targets_guardian_task()
  {
    GuardianRecovery.BuildSchTasksRunArguments()
      .Should().Be("/Run /TN \"SmartGuard Guardian\"");
  }
}
