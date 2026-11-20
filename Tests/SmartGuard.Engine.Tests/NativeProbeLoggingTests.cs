using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class NativeProbeLoggingTests
{
  [Theory]
  [InlineData("NativeProbeFailed")]
  [InlineData("NativeProbeRecovered")]
  public void Native_probe_transitions_have_registered_recovery_events(string eventName)
  {
    EventNameRegistry.IsRegistered(eventName).Should().BeTrue();
    LogEventChannels.ForEventName(eventName).Should().Be(LogChannelRegistry.Recovery);
  }

  [Theory]
  [InlineData("timed-out", false)]
  [InlineData("cancelled", true)]
  [InlineData("cleanup-unconfirmed", false)]
  public void Abnormal_record_contains_budget_cleanup_and_aggregation_without_claiming_success(string kind, bool cleaned)
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("brightness-write", kind, 2250, 2000, cleaned, null, 17, false));
    record.EventName.Should().Be(EventNameRegistry.NativeProbeFailed);
    record.Properties!["operation"].GetString().Should().Be("brightness-write");
    record.Properties["failureKind"].GetString().Should().Be(kind);
    record.Properties["elapsedMs"].GetInt64().Should().Be(2250);
    record.Properties["budgetMs"].GetInt64().Should().Be(2000);
    record.Properties["terminationConfirmed"].GetBoolean().Should().Be(cleaned);
    record.Properties["suppressedCount"].GetInt64().Should().Be(17);
    record.Properties["actionResult"].GetString().Should().Be("unconfirmed");
    record.Message.Should().Contain("无法确认").And.NotContain("已切换");
  }

  [Fact]
  public void Recovery_is_a_separate_record_with_the_suppressed_failure_count()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("brightness-read", "none", 8, 2000, true, 0, 11, true));
    record.EventName.Should().Be(EventNameRegistry.NativeProbeRecovered);
    record.Properties!["recovered"].GetBoolean().Should().BeTrue();
    record.Properties["suppressedCount"].GetInt64().Should().Be(11);
    record.Message.Should().Contain("恢复");
  }

  [Fact]
  public void Setactive_exit_recovery_is_explicitly_command_execution_not_verified_power_plan_success()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("plan-apply", "none", 20, 5000, true, 0, 0, true));
    record.Message.Should().Contain("命令执行").And.NotContain("电源计划已切换");
    record.Properties!["recoveryScope"].GetString().Should().Be("command-execution");
  }

  [Theory]
  [InlineData("plan-read", "timed-out")]
  [InlineData("plan-apply", "call-pending")]
  [InlineData("plan-brightness-dc", "timed-out")]
  [InlineData("plan-catalog", "timed-out")]
  public void Scheme_api_wait_failures_do_not_mention_child_processes(string operation, string kind)
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new(operation, kind, 2000, 2000, true, null, 0, false));
    record.Message.Should().Contain("电源服务调用仍在进行，期间不发起新调用，返回后自动恢复").And.NotContain("子进程");
  }

  [Fact]
  public void Scheme_api_error_reports_its_win32_code()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("plan-brightness-ac", "api-error", 1, 2000, true, null, 0, false, 5));
    record.Properties!["win32Error"].GetInt32().Should().Be(5);
    record.Message.Should().Contain("电源服务返回错误码 5").And.NotContain("子进程");
  }
}
