using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

/// <summary>
/// Main-loop-owned, memory-only latest observations. Admission and execution have
/// independent acknowledgement; rejected records retain their observation time.
/// This never authorizes an action or retains configuration/window history.
/// </summary>
internal sealed class GuardObservationTracker
{
  private sealed record Admission(bool Admitted, GuardDecisionReason Reason, int? Index, string? Name);
  private sealed record Execution(string Source, string Reason, Guid? Target, Guid? Actual, string Action);
  private Admission? _pendingAdmission, _acceptedAdmission;
  private Execution? _pendingExecution, _acceptedExecution;
  private DateTimeOffset _admissionAt, _executionAt;
  private bool _admissionDirty, _executionDirty;
  private string? _fingerprint, _gate, _minimumLevel;

  public void ObserveContext(GuardConfigReadResult read, string gate)
  {
    var level = read.Config?.LogMinimumLevel;
    if (_minimumLevel != level)
    {
      // User filtering is acknowledgement, not a retry failure. A threshold
      // change permits exactly one summary of the current observation.
      _acceptedAdmission = null;
      _acceptedExecution = null;
      _admissionDirty = _pendingAdmission is not null;
      _executionDirty = _pendingExecution is not null;
      _minimumLevel = level;
    }
    if (_fingerprint != read.RuleFingerprint || _gate != gate)
    {
      // Rules or authority changed: an old rejected automatic outcome must not
      // be replayed into a new generation, even if admission was accepted.
      _pendingExecution = _acceptedExecution = null;
      _executionDirty = false;
      _pendingAdmission = _acceptedAdmission = null;
      _admissionDirty = false;
      _fingerprint = read.RuleFingerprint;
      _gate = gate;
    }
  }

  public void ObserveAdmission(ForegroundAdmission admission, string? programFileName, DateTimeOffset observedAtUtc)
  {
    var name = admission.MatchedRuleIndex is null ? null : ShortName(programFileName);
    var next = new Admission(admission.Admitted, admission.Reason, admission.MatchedRuleIndex, name);
    if (next != _pendingAdmission)
    {
      _pendingAdmission = next;
      _admissionAt = observedAtUtc;
      // Even A→B can invalidate an unaccepted full result observed under A.
      // Repeated identical held observations must retain the original time.
      _pendingExecution = null;
      _executionDirty = false;
    }
    _admissionDirty = next != _acceptedAdmission;
  }

  public void ClearAdmission() { _pendingAdmission = null; _admissionDirty = false; }

  public void ObserveExecution(string source, string reason, Guid? target, Guid? actual, string action, DateTimeOffset observedAtUtc)
  {
    var next = new Execution(source, reason, target, actual, action);
    if (next != _pendingExecution) { _pendingExecution = next; _executionAt = observedAtUtc; }
    _executionDirty = next != _acceptedExecution;
  }

  public void Drain(GuardConfig? config,
    Func<GuardConfig?, LogLevel, string, string, IReadOnlyDictionary<string, JsonElement>?, DateTimeOffset?, GuardianLogWriteResult> write)
  {
    // At most two attempts per existing cycle. A rejection creates no task,
    // wakeup, history queue or synchronous fallback.
    if (_admissionDirty && _pendingAdmission is { } admission)
    {
      var props = Properties(_admissionAt);
      props["guardReason"] = JsonSerializer.SerializeToElement(GuardDecisionCodes.ToReasonCode(admission.Reason));
      if (admission.Index is int index) props["matchedRuleIndex"] = JsonSerializer.SerializeToElement(index);
      if (admission.Name is not null) props["programFileName"] = JsonSerializer.SerializeToElement(admission.Name);
      var message = admission.Index is int matched
        ? $"前台命中名单项 #{matched + 1}：{admission.Name}；已满足名单条件"
        : admission.Reason switch
        {
          GuardDecisionReason.ForegroundIdentityUnknown => "前台身份无法识别；名单条件未确认",
          GuardDecisionReason.ForegroundListEmpty => "前台名单为空；名单条件未满足",
          GuardDecisionReason.ForegroundProgramPathsInvalid => "前台名单无效；名单条件未确认",
          _ => "前台已退出命中；名单条件未满足",
        };
      var result = write(config, admission.Reason == GuardDecisionReason.ForegroundIdentityUnknown ? LogLevel.Warn : LogLevel.Info,
        message, EventNameRegistry.ForegroundAdmissionChanged, props, _admissionAt);
      if (Acknowledged(result)) { _acceptedAdmission = admission; _admissionDirty = false; }
    }
    if (_executionDirty && _pendingExecution is { } execution)
    {
      var props = Properties(_executionAt);
      props["guardSource"] = JsonSerializer.SerializeToElement(execution.Source);
      props["guardReason"] = JsonSerializer.SerializeToElement(execution.Reason);
      props["actionResult"] = JsonSerializer.SerializeToElement(execution.Action);
      if (execution.Target is Guid target) props["targetPlanGuid"] = JsonSerializer.SerializeToElement(target);
      if (execution.Actual is Guid actual) props["actualPlanGuid"] = JsonSerializer.SerializeToElement(actual);
      var result = write(config,
        execution.Reason is GuardStatusCodes.Reasons.ConfigUnavailable or GuardStatusCodes.Reasons.ProbeFailed ? LogLevel.Warn : LogLevel.Info,
        Describe(execution), EventNameRegistry.GuardStateChanged, props, _executionAt);
      if (Acknowledged(result)) { _acceptedExecution = execution; _executionDirty = false; }
    }
  }

  private static bool Acknowledged(GuardianLogWriteResult result)
    => result.IsAccepted() || result == GuardianLogWriteResult.SuppressedByLevel;
  private static Dictionary<string, JsonElement> Properties(DateTimeOffset at)
    => new() { ["observedAtUtc"] = JsonSerializer.SerializeToElement(at) };

  private static string? ShortName(string? value)
  {
    if (string.IsNullOrEmpty(value)) return null;
    var name = Path.GetFileName(value).ToLowerInvariant();
    return name.Length <= 128 ? name : name[..125] + "...";
  }

  private static string Describe(Execution value)
  {
    var state = value.Reason switch
    {
      GuardStatusCodes.Reasons.ConfigUnavailable => "配置不可用；自动守护暂停干预",
      GuardStatusCodes.Reasons.ProbeFailed => "计划探测失败；本轮不执行切换",
      GuardStatusCodes.Reasons.Paused => "守护已暂停；保留当前计划",
      GuardStatusCodes.Reasons.ManualActive => "人工接管有效",
      GuardStatusCodes.Reasons.ForegroundIdentityUnknown => "前台身份未知；自动守护停止干预",
      GuardStatusCodes.Reasons.ForegroundListEmpty => "前台名单为空；自动守护停止干预",
      GuardStatusCodes.Reasons.ForegroundProgramPathsInvalid => "前台名单无效；自动守护停止干预",
      GuardStatusCodes.Reasons.WaitingForListedProgram => "前台未命中；自动守护停止干预，保留当前计划",
      GuardStatusCodes.Reasons.BlockedByLifecycle => "电源生命周期阻止本轮策略变更",
      _ => "自动守护",
    };
    return value.Action switch
    {
      "already-satisfied" => state + "；当前已符合目标，无需切换",
      "verified" => state + "；已切换，最终观测验证成功",
      "failed" => state + "；切换未验证成功",
      "unknown" => state + "；执行结果未知",
      _ => state + "；本轮未执行切换",
    };
  }
}
