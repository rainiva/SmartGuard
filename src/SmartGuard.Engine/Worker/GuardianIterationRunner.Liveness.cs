using System.Diagnostics;
using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal sealed partial class GuardianIterationRunner
{
  // Owned by the main round; shares the runner's TimeProvider so the window and the round use one clock.
  private readonly GuardLivenessTracker _liveness = new(timeProvider ?? TimeProvider.System);

  private void ObserveLiveness(GuardConfig? config, GuardianLoopIterationState state, bool applicable,
    PowerPlanProbeResult planProbe, bool resumed)
  {
    var observed = _liveness.Observe(applicable, planProbe.Succeeded,
      planProbe.Succeeded ? null : GuardLivenessTracker.FailureKindCode(planProbe.FailureKind), resumed);
    var minutes = (long)observed.Duration.TotalMinutes;
    var stalledMs = JsonSerializer.SerializeToElement((long)observed.Duration.TotalMilliseconds);
    if (observed.Transition == GuardLivenessTransition.Entered)
    {
      var reason = DescribeStallReason(observed.LastFailureKind);
      var properties = new Dictionary<string, JsonElement>
      {
        ["stalledMs"] = stalledMs,
        ["lastFailureKind"] = JsonSerializer.SerializeToElement(observed.LastFailureKind ?? GuardHealthCodes.FailureKinds.Other),
      };
      if (PowerSchemeService.InFlightSince is long since)
        properties["schemeCallInFlightMs"] = JsonSerializer.SerializeToElement((long)Stopwatch.GetElapsedTime(since).TotalMilliseconds);
      writeLog(config, LogLevel.Warn,
        $"守护已停工：已连续 {minutes} 分钟无法读取当前电源计划（{reason}），自动守护暂时无法切换计划。",
        EventNameRegistry.GuardStalled, properties, null);
      // One notification per stall: the tray dedupes by event id; this path bypasses the plan-change preference.
      var stall = new NotificationEvent
      {
        kind = NotificationKinds.GuardStalled,
        title = "智能电源守护已停工",
        body = $"已连续 {minutes} 分钟无法读取电源计划（{reason}），自动守护暂时无法工作。",
      };
      var now = DateTime.Now;
      state.LastEmittedNotification = stall;
      state.LastEmittedNotificationAt = now;
      state.NotificationRetention = NotificationEventRetention.Advance(stall, state.NotificationRetention, now);
    }
    else if (observed.Transition == GuardLivenessTransition.Recovered)
    {
      writeLog(config, LogLevel.Info, $"守护已恢复：电源计划读取恢复正常，此前停工 {minutes} 分钟。",
        EventNameRegistry.GuardStallRecovered,
        new Dictionary<string, JsonElement> { ["stalledMs"] = stalledMs }, null);
    }
  }

  private static string DescribeStallReason(string? failureKind) => failureKind switch
  {
    GuardHealthCodes.FailureKinds.CallPending => "电源服务调用一直未返回",
    GuardHealthCodes.FailureKinds.ApiError => "电源服务调用失败",
    GuardHealthCodes.FailureKinds.TimedOut => "电源服务调用超时",
    _ => "原因未知",
  };
}
