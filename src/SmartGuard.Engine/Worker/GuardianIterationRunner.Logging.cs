using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal sealed partial class GuardianIterationRunner
{
  private void ObserveUnavailable(GuardConfigReadResult read, Guid? actual, string reason, DateTimeOffset at)
  {
    _observations.ObserveContext(read, GuardObservationGate.For(read, at));
    _observations.ClearAdmission();
    _observations.ObserveExecution(GuardStatusCodes.Sources.None, reason, null, actual, "not-attempted", at,
      reason == GuardStatusCodes.Reasons.ConfigUnavailable ? read.FailureKind ?? "unknown" : null);
  }

  private void ObserveFinal(GuardConfigReadResult read, ForegroundObservation foreground,
    GuardDecision decision, string source, string reason, Guid? actual, string action, DateTimeOffset at)
  {
    _observations.ObserveContext(read, GuardObservationGate.For(read, at));
    if (read.Config?.ForegroundRestrictionEnabled == true
        && decision.Reason is not GuardDecisionReason.ManualActive and not GuardDecisionReason.Paused)
      _observations.ObserveAdmission(new(decision.Source == GuardDecisionSource.Automatic, decision.Reason, decision.MatchedRuleIndex),
        foreground.ExecutablePath, at);
    _observations.ObserveExecution(source, reason, decision.TargetPlanGuid, actual, action, at);
  }

  private static IReadOnlyDictionary<string, JsonElement> ActionProperties(Guid? target, Guid? actual, string action)
  {
    var props = new Dictionary<string, JsonElement> { ["actionResult"] = JsonSerializer.SerializeToElement(action) };
    if (target is Guid targetGuid) props["targetPlanGuid"] = JsonSerializer.SerializeToElement(targetGuid);
    if (actual is Guid actualGuid) props["actualPlanGuid"] = JsonSerializer.SerializeToElement(actualGuid);
    return props;
  }

  private void LogBrightnessChangeIfNeeded(GuardConfig config, int value, GuardianLoopIterationState state)
  {
    if (value < 0) return;
    if (state.LastBrightness is int previous && previous != value)
      writeLog(config, LogLevel.Info, GuardianLogMessages.FormatBrightnessChange(previous, value), EventNameRegistry.BrightnessAdjusted, null, null);
    state.LastBrightness = value;
  }
}

internal static class GuardObservationGate
{
  // Invalidation only; PolicyEngine remains the decision/authorization owner.
  public static string For(GuardConfigReadResult read, DateTimeOffset at)
    => !read.IsValid || read.Config is not { } config ? GuardStatusCodes.Reasons.ConfigUnavailable
      : config.IsManualHighPerformanceActive(at.LocalDateTime) ? GuardStatusCodes.Reasons.ManualActive
      : config.Paused ? GuardStatusCodes.Reasons.Paused
      : config.ForegroundRestrictionEnabled ? "restricted" : "unrestricted";
}
