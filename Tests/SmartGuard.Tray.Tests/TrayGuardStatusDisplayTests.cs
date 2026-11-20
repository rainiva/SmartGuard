using SmartGuard.Contracts;

namespace SmartGuard.Tray.Tests;

/// <summary>
/// 任务 4：托盘守护状态展示行为测试。
/// 守护来源/原因为跨进程稳定代码（GuardStatusCodes），文案只在 Tray UI 映射；
/// 旧状态缺字段或代码未知时不得显示确定的正在守护；
/// 只有 guardReason 变化仍必须刷新托盘；Clone 保留新字段。
/// </summary>
public class TrayGuardStatusDisplayTests
{
  [Fact]
  public void Apply_updates_when_only_guard_reason_changes()
  {
    var state = new TrayDisplayState();
    state.Apply(Status(GuardStatusCodes.Reasons.PolicySelected)).Should().BeTrue();

    state.Apply(Status(GuardStatusCodes.Reasons.WaitingForListedProgram)).Should().BeTrue(
      "只有 guardReason 改变也必须更新托盘，否则原因变化永远显示不出来");
    state.StatusLine.Should().Contain("等待指定程序");
  }

  [Fact]
  public void Apply_skips_update_when_guard_fields_unchanged()
  {
    var state = new TrayDisplayState();
    state.Apply(Status(GuardStatusCodes.Reasons.ForegroundMatched)).Should().BeTrue();

    state.Apply(Status(GuardStatusCodes.Reasons.ForegroundMatched)).Should().BeFalse();
  }

  [Fact]
  public void Clone_preserves_guard_fields_so_equal_statuses_stay_skipped()
  {
    var state = new TrayDisplayState();
    state.Apply(Status(GuardStatusCodes.Reasons.WaitingForListedProgram)).Should().BeTrue();

    state.Apply(Status(GuardStatusCodes.Reasons.WaitingForListedProgram)).Should().BeFalse(
      "内部 Clone 若丢失新字段，扩展后的相等判定会把相同状态误判为变化");
  }

  [Fact]
  public void Apply_removes_guard_claim_when_unchanged_status_naturally_expires()
  {
    var now = DateTimeOffset.UtcNow;
    var state = new TrayDisplayState(() => now);
    var status = Status(GuardStatusCodes.Reasons.PolicySelected);
    status.timestamp = now.ToString("O");

    state.Apply(status).Should().BeTrue();
    state.StatusLine.Should().Contain("守护中");

    now += GuardStatusFreshness.MaxAge + TimeSpan.FromTicks(1);
    state.Apply(status).Should().BeTrue(
      "the periodic Tray refresh must recompute freshness even when status.json has not changed");
    state.StatusLine.Should().NotContain("守护中");
    state.Tooltip.Should().NotContain("守护中");
  }

  // ---- 状态行文案：原因代码 → 展示子句 ----

  [Fact]
  public void FormatStatusLine_shows_waiting_for_listed_program()
  {
    TrayStatusFormatter.FormatStatusLine(Status(GuardStatusCodes.Reasons.WaitingForListedProgram))
      .Should().Be("计划：平衡 | 50% 电池 | 等待指定程序");
  }

  [Fact]
  public void FormatStatusLine_shows_empty_list_hint()
  {
    TrayStatusFormatter.FormatStatusLine(Status(GuardStatusCodes.Reasons.ForegroundListEmpty))
      .Should().Be("计划：平衡 | 50% 电池 | 添加程序后生效");
  }

  [Fact]
  public void FormatStatusLine_shows_identity_unknown_hint()
  {
    TrayStatusFormatter.FormatStatusLine(Status(GuardStatusCodes.Reasons.ForegroundIdentityUnknown))
      .Should().Be("计划：平衡 | 50% 电池 | 暂时无法识别前台程序");
  }

  [Fact]
  public void FormatStatusLine_shows_manual_takeover_as_temporary_high_performance()
  {
    var status = Status(GuardStatusCodes.Reasons.ManualActive);
    status.guardSource = GuardStatusCodes.Sources.Manual;

    TrayStatusFormatter.FormatStatusLine(status)
      .Should().Be("计划：平衡 | 50% 电池 | 临时高性能");
  }

  [Fact]
  public void FormatStatusLine_shows_active_guarding_for_policy_selected()
  {
    TrayStatusFormatter.FormatStatusLine(Status(GuardStatusCodes.Reasons.PolicySelected))
      .Should().Be("计划：平衡 | 50% 电池 | 守护中");
  }

  [Fact]
  public void FormatStatusLine_shows_foreground_matched_as_active_guarding()
  {
    TrayStatusFormatter.FormatStatusLine(Status(GuardStatusCodes.Reasons.ForegroundMatched))
      .Should().Be("计划：平衡 | 50% 电池 | 守护中");
  }

  [Fact]
  public void FormatStatusLine_keeps_pause_display_without_extra_guard_clause()
  {
    var status = Status(GuardStatusCodes.Reasons.Paused);
    status.paused = true;

    TrayStatusFormatter.FormatStatusLine(status)
      .Should().Be("计划：平衡 | 50% 电池 | 已暂停");
  }

  [Fact]
  public void FormatStatusLine_shows_degraded_reasons_as_distinct_fault_text()
  {
    TrayStatusFormatter.FormatStatusLine(Status(GuardStatusCodes.Reasons.ConfigUnavailable))
      .Should().Be("计划：平衡 | 50% 电池 | 配置不可用");
    TrayStatusFormatter.FormatStatusLine(Status(GuardStatusCodes.Reasons.ProbeFailed))
      .Should().Be("计划：平衡 | 50% 电池 | 状态探测失败");
    TrayStatusFormatter.FormatStatusLine(Status(GuardStatusCodes.Reasons.BlockedByLifecycle))
      .Should().Be("计划：平衡 | 50% 电池 | 电源恢复暂缓守护");
    TrayStatusFormatter.FormatStatusLine(Status(GuardStatusCodes.Reasons.ForegroundProgramPathsInvalid))
      .Should().Be("计划：平衡 | 50% 电池 | 名单配置异常");
  }

  // ---- 状态过期/时间戳不可信：不得显示任何确定的守护子句（与 Settings 同一新鲜度上界）----

  [Fact]
  public void FormatStatusLine_omits_guard_claim_when_timestamp_is_stale()
  {
    var status = Status(GuardStatusCodes.Reasons.PolicySelected);
    status.timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O");

    TrayStatusFormatter.FormatStatusLine(status)
      .Should().Be("计划：平衡 | 50% 电池", "状态过期时不得显示确定的正在守护");
  }

  [Fact]
  public void FormatTooltip_omits_guard_claim_when_timestamp_is_stale()
  {
    var status = Status(GuardStatusCodes.Reasons.WaitingForListedProgram);
    status.timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O");

    TrayStatusFormatter.FormatTooltip(status)
      .Should().Be("计划: 平衡 | 50% 电池 | 亮度60%", "状态过期时不得显示确定的正在守护");
  }

  [Fact]
  public void FormatStatusLine_omits_guard_claim_when_timestamp_missing_or_unparsable()
  {
    var missing = Status(GuardStatusCodes.Reasons.PolicySelected);
    missing.timestamp = string.Empty;
    TrayStatusFormatter.FormatStatusLine(missing).Should().Be("计划：平衡 | 50% 电池");

    var unparsable = Status(GuardStatusCodes.Reasons.WaitingForListedProgram);
    unparsable.timestamp = "not-a-timestamp";
    TrayStatusFormatter.FormatStatusLine(unparsable).Should().Be("计划：平衡 | 50% 电池");
  }

  [Fact]
  public void FormatStatusLine_keeps_paused_display_for_stale_status()
  {
    var status = Status(GuardStatusCodes.Reasons.PolicySelected);
    status.timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O");
    status.paused = true;

    TrayStatusFormatter.FormatStatusLine(status)
      .Should().Be("计划：平衡 | 50% 电池 | 已暂停");
  }

  // ---- 旧状态兼容：字段缺失或代码未知 → 不显示确定的正在守护 ----

  [Fact]
  public void FormatStatusLine_omits_guard_claim_for_old_status_without_guard_fields()
  {
    var status = Status(reason: null);
    status.guardSource = null;

    TrayStatusFormatter.FormatStatusLine(status)
      .Should().Be("计划：平衡 | 50% 电池",
        "旧状态缺守护字段时不得显示确定的正在守护");
  }

  [Fact]
  public void FormatStatusLine_omits_claim_for_unregistered_codes()
  {
    TrayStatusFormatter.FormatStatusLine(Status("not-a-registered-code"))
      .Should().Be("计划：平衡 | 50% 电池");
  }

  [Fact]
  public void FormatStatusLine_omits_claim_when_guard_reason_is_null_but_source_present()
  {
    var status = Status(reason: null);

    TrayStatusFormatter.FormatStatusLine(status)
      .Should().Be("计划：平衡 | 50% 电池");
  }

  [Fact]
  public void FormatTooltip_appends_guard_clause()
  {
    TrayStatusFormatter.FormatTooltip(Status(GuardStatusCodes.Reasons.WaitingForListedProgram))
      .Should().Be("计划: 平衡 | 50% 电池 | 亮度60% | 等待指定程序");
  }

  private static StatusPayload Status(string? reason, string source = GuardStatusCodes.Sources.Automatic)
    => new()
    {
      timestamp = DateTimeOffset.UtcNow.ToString("O"),
      currentPlan = "平衡",
      batteryPercent = 50,
      isOnAC = false,
      brightness = 60,
      paused = false,
      guardSource = source,
      guardReason = reason,
      // These tests isolate guard freshness; logging has its own fresh sample.
      logHealth = new LogHealthStatus(LogHealthCodes.Healthy, DateTimeOffset.UtcNow, 1, 0, 0, 0, 0, 0),
    };
}
