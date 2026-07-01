using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class GuardianRecoveryEnginePidTests
{
  [Fact]
  public void ShouldSkipRecovery_when_status_reports_live_engine_pid()
  {
    GuardianRecovery.EngineProcessCheckerForTests = _ => true;
    try
    {
      GuardianRecovery.ShouldSkipScheduledTaskRecovery(enginePid: 42_424).Should().BeTrue();
    }
    finally
    {
      GuardianRecovery.ResetEngineProcessCheckerForTests();
    }
  }

  [Fact]
  public void ShouldNotSkipRecovery_when_engine_pid_is_zero()
  {
    GuardianRecovery.ShouldSkipScheduledTaskRecovery(enginePid: 0).Should().BeFalse();
  }

  [Fact]
  public void ShouldNotSkipRecovery_when_engine_pid_process_exited()
  {
    GuardianRecovery.ShouldSkipScheduledTaskRecovery(enginePid: 999_999_999).Should().BeFalse();
  }
}
