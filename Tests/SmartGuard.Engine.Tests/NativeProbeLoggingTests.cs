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

  [Fact]
  public void Unconfirmed_cleanup_names_the_evidence_of_this_execution()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("brightness-read", "cleanup-unconfirmed", 2250, 2000, false, null, 0, false,
      CleanupEvidence: "pipes-open"));
    record.Properties!["cleanupEvidence"].GetString().Should().Be("pipes-open");
    record.Properties.Should().NotContainKey("pendingCleanupEvidence");
    record.Message.Should().Contain("清理未确认（pipes-open）").And.Contain("确认其退出后自动恢复");
  }

  [Fact]
  public void Rejection_reports_not_started_and_the_held_child_separately()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("battery-read", "cleanup-pending", 0, 2000, true, null, 0, false,
      CleanupEvidence: "not-started", PendingCleanupEvidence: "not-exited"));
    record.Properties!["cleanupEvidence"].GetString().Should().Be("not-started");
    record.Properties["pendingCleanupEvidence"].GetString().Should().Be("not-exited");
    record.Message.Should().Contain("上次 WMI 子进程清理尚未确认（not-exited），本次未启动");
  }

  [Fact]
  public void Reopen_is_a_process_cleanup_recovery_with_the_closed_duration()
  {
    var record = GuardianLoop.CreateNativeProbeEvent(new("process-cleanup", "none", 4200, 2000, true, null, 0, true,
      CleanupEvidence: "confirmed"));
    record.EventName.Should().Be(EventNameRegistry.NativeProbeRecovered);
    record.Properties!["recoveryScope"].GetString().Should().Be("process-cleanup");
    record.Properties["elapsedMs"].GetInt64().Should().Be(4200);
    record.Message.Should().Contain("子进程清理确认").And.Contain("恢复启动新的探测子进程");
  }
}
