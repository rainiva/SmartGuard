using SmartGuard.Contracts;

namespace SmartGuard.Settings.Tests;

/// <summary>
/// 任务 4：Settings 守护状态文案映射（稳定代码 → 展示文案）。
/// 旧状态缺字段、代码未登记或状态过期一律"状态未知"，不得显示确定的正在守护；
/// 运行时配置/识别异常与普通未命中分开显示。
/// </summary>
public class ForegroundGuardStatusTextTests
{
  private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public void Waiting_reason_maps_to_exact_brief_text()
    => Describe(reason: GuardStatusCodes.Reasons.WaitingForListedProgram)
      .Should().Be("等待指定程序");

  [Fact]
  public void Empty_list_reason_maps_to_exact_brief_text()
    => Describe(reason: GuardStatusCodes.Reasons.ForegroundListEmpty)
      .Should().Be("添加程序后生效");

  [Fact]
  public void Identity_unknown_reason_maps_to_exact_brief_text()
    => Describe(reason: GuardStatusCodes.Reasons.ForegroundIdentityUnknown)
      .Should().Be("暂时无法识别前台程序");

  [Fact]
  public void Manual_active_maps_to_temporary_high_performance()
    => Describe(source: GuardStatusCodes.Sources.Manual, reason: GuardStatusCodes.Reasons.ManualActive)
      .Should().Be("临时高性能");

  [Fact]
  public void Automatic_guarding_maps_to_active_text()
  {
    Describe(reason: GuardStatusCodes.Reasons.PolicySelected).Should().Be("守护中");
    Describe(reason: GuardStatusCodes.Reasons.ForegroundMatched).Should().Be("守护中");
  }

  [Fact]
  public void Paused_status_displays_paused()
  {
    var status = Fresh(reason: GuardStatusCodes.Reasons.Paused);
    status.paused = true;
    ForegroundGuardStatusText.Describe(status, Now).Should().Be("已暂停");
  }

  [Fact]
  public void Degraded_reasons_show_distinct_fault_text_not_normal_waiting()
  {
    Describe(reason: GuardStatusCodes.Reasons.ConfigUnavailable).Should().Be("配置不可用");
    Describe(reason: GuardStatusCodes.Reasons.ProbeFailed).Should().Be("状态探测失败");
    Describe(reason: GuardStatusCodes.Reasons.BlockedByLifecycle).Should().Be("电源恢复暂缓守护");
    Describe(reason: GuardStatusCodes.Reasons.ForegroundProgramPathsInvalid).Should().Be("名单配置异常");
  }

  [Fact]
  public void Missing_guard_fields_show_unknown_never_a_guarding_claim()
  {
    var status = Fresh(reason: null);
    status.guardSource = null;
    ForegroundGuardStatusText.Describe(status, Now).Should().Be(ForegroundGuardStatusText.UnknownText);
  }

  [Fact]
  public void Unregistered_codes_show_unknown()
  {
    Describe(source: "mystery-source", reason: "mystery-reason")
      .Should().Be(ForegroundGuardStatusText.UnknownText);
  }

  [Fact]
  public void Null_status_shows_unknown()
    => ForegroundGuardStatusText.Describe(null, Now).Should().Be(ForegroundGuardStatusText.UnknownText);

  [Fact]
  public void Stale_timestamp_shows_unknown()
    => ForegroundGuardStatusText.Describe(
      Fresh(reason: GuardStatusCodes.Reasons.WaitingForListedProgram, writtenAt: Now.AddMinutes(-10)),
      Now).Should().Be(ForegroundGuardStatusText.UnknownText);

  [Fact]
  public void Unparsable_timestamp_shows_unknown()
  {
    var status = Fresh(reason: GuardStatusCodes.Reasons.WaitingForListedProgram);
    status.timestamp = "not-a-timestamp";
    ForegroundGuardStatusText.Describe(status, Now).Should().Be(ForegroundGuardStatusText.UnknownText);
  }

  [Fact]
  public void Timestamp_from_far_future_shows_unknown_as_clock_skew_protection()
    => ForegroundGuardStatusText.Describe(
      Fresh(reason: GuardStatusCodes.Reasons.WaitingForListedProgram, writtenAt: Now.AddHours(5)),
      Now).Should().Be(ForegroundGuardStatusText.UnknownText);

  [Fact]
  public void Freshness_bound_allows_three_minutes_of_age()
    => ForegroundGuardStatusText.Describe(
      Fresh(reason: GuardStatusCodes.Reasons.WaitingForListedProgram, writtenAt: Now.Add(-ForegroundGuardStatusText.MaxStatusAge)),
      Now).Should().Be("等待指定程序");

  [Fact]
  public void Stall_shows_its_start_and_reason_instead_of_probe_failure()
  {
    var since = new DateTimeOffset(2026, 9, 30, 11, 40, 0, TimeSpan.Zero);
    var status = Fresh(GuardStatusCodes.Sources.None, GuardStatusCodes.Reasons.ProbeFailed);
    status.guardHealth = new GuardHealthStatus(GuardHealthCodes.Stalled, since, GuardHealthCodes.FailureKinds.CallPending);
    ForegroundGuardStatusText.Describe(status, Now)
      .Should().Be($"守护已停工（自 {since.ToLocalTime():HH:mm}，电源服务调用一直未返回）");
  }

  [Fact]
  public void Healthy_guard_health_keeps_the_existing_reason_text()
  {
    var status = Fresh(GuardStatusCodes.Sources.None, GuardStatusCodes.Reasons.ProbeFailed);
    status.guardHealth = new GuardHealthStatus(GuardHealthCodes.Healthy, null, null);
    ForegroundGuardStatusText.Describe(status, Now).Should().Be("状态探测失败");
  }

  private static string Describe(
    string source = GuardStatusCodes.Sources.Automatic,
    string? reason = GuardStatusCodes.Reasons.PolicySelected)
    => ForegroundGuardStatusText.Describe(Fresh(source, reason), Now);

  private static StatusPayload Fresh(
    string source = GuardStatusCodes.Sources.Automatic,
    string? reason = null,
    DateTimeOffset? writtenAt = null)
    => new()
    {
      timestamp = (writtenAt ?? Now).ToString("O"),
      currentPlan = "高性能",
      paused = false,
      guardSource = reason is null ? null : source,
      guardReason = reason,
    };
}
