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
  private sealed record Admission(bool Admitted, GuardDecisionReason Reason, int? Index, string? Name,
    string? ObservationPhase, int? ObservationSeconds);
  private sealed record Execution(string Source, string Reason, Guid? Target, Guid? Actual, string Action, string? FailureKind);
  private Admission? _pendingAdmission, _acceptedAdmission;
  private Execution? _pendingExecution, _acceptedExecution;
  private DateTimeOffset _admissionAt, _executionAt;
  private bool _admissionDirty, _executionDirty;
  private string? _fingerprint, _gate, _minimumLevel;
  private string? _reportedRuleFingerprint, _pendingRuleFingerprint;
  private bool _hasReportedRules, _hasPendingRules, _configurationUnavailable;
  private DateTimeOffset _rulesAt;

  public void ObserveContext(GuardConfigReadResult read, string gate)
  {
    if (!read.IsValid) { _configurationUnavailable = true; _hasPendingRules = false; }
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

  public void ObserveAdmission(ForegroundAdmission admission, string? programFileName, DateTimeOffset observedAtUtc,
    string? observationPhase = null, int? observationSeconds = null, string? lastListedProgram = null)
  {
    if (admission.Reason is not (GuardDecisionReason.ForegroundMatched or GuardDecisionReason.WaitingForListedProgram
      or GuardDecisionReason.ForegroundObservation)) observationPhase = null;
    var name = admission.MatchedRuleIndex is null
      ? observationPhase is null ? null : ShortName(lastListedProgram) : ShortName(programFileName);
    var next = new Admission(admission.Admitted, admission.Reason, admission.MatchedRuleIndex, name,
      observationPhase, observationPhase is null ? null : observationSeconds);
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

  public void ObserveExecution(string source, string reason, Guid? target, Guid? actual, string action, DateTimeOffset observedAtUtc,
    string? failureKind = null)
  {
    var next = new Execution(source, reason, target, actual, action, failureKind);
    if (next != _pendingExecution) { _pendingExecution = next; _executionAt = observedAtUtc; }
    _executionDirty = next != _acceptedExecution;
  }

  public void DrainConfiguration(GuardConfigReadResult read, DateTimeOffset at,
    Func<GuardConfig?, LogLevel, string, string, IReadOnlyDictionary<string, JsonElement>?, DateTimeOffset?, GuardianLogWriteResult> write)
  {
    if (!read.IsValid || read.Config is not { } config)
    { _configurationUnavailable = true; _hasPendingRules = false; return; }
    if (_hasReportedRules && _reportedRuleFingerprint == read.RuleFingerprint && !_configurationUnavailable) return;
    if (!_hasPendingRules || _pendingRuleFingerprint != read.RuleFingerprint)
    { _hasPendingRules = true; _pendingRuleFingerprint = read.RuleFingerprint; _rulesAt = at; }
    var message = (_configurationUnavailable ? "配置读取恢复；" : string.Empty)
      + $"程序规则已生效：{(config.ForegroundRestrictionEnabled ? "仅在名单程序位于前台时自动调整" : "按使用情况自动调整，不限制程序")}。名单 {config.ForegroundProgramPaths?.Count ?? 0} 项，退出观察时间 {config.ForegroundObservationSec} 秒。";
    if (Acknowledged(write(config, LogLevel.Info, message, EventNameRegistry.ConfigReloaded, null, _rulesAt)))
    {
      _hasReportedRules = true;
      _reportedRuleFingerprint = read.RuleFingerprint;
      _hasPendingRules = false;
      _configurationUnavailable = false;
    }
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
      if (admission.ObservationPhase is not null) props["observationState"] = JsonSerializer.SerializeToElement(admission.ObservationPhase);
      if (admission.ObservationSeconds is int seconds) props["observationSeconds"] = JsonSerializer.SerializeToElement(seconds);
      var message = admission.ObservationPhase switch
      {
        "observing" => $"{FriendlyProgram(admission.Name)}已切换至后台，进入观察窗口期（{admission.ObservationSeconds}秒）。期间不自动调整电源计划。",
        "returned" => $"已返回{FriendlyProgram(admission.Name)}，恢复自动检测。",
        "expired" => "观察时间已结束，当前未使用名单中的程序。保留当前电源计划，不再自动调整。",
        _ => admission.Index is int matched
        ? $"正在使用{FriendlyProgram(admission.Name)}，已匹配第{matched + 1}条程序规则。将按使用情况自动调整电源计划。"
        : admission.Reason switch
        {
          GuardDecisionReason.ForegroundIdentityUnknown => "暂时无法识别当前程序，保留当前电源计划，等待下次检测。",
          GuardDecisionReason.ForegroundListEmpty => "程序名单为空，请先添加程序。当前不会自动调整电源计划。",
          GuardDecisionReason.ForegroundProgramPathsInvalid => "程序名单配置有误，请检查程序路径。当前不会自动调整电源计划。",
          _ => "当前未使用名单中的程序，保留当前电源计划，不自动调整。",
        },
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
      if (execution.FailureKind is not null) props["failureKind"] = JsonSerializer.SerializeToElement(execution.FailureKind);
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
      GuardStatusCodes.Reasons.ConfigUnavailable => "无法读取设置：" + FailureLabel(value.FailureKind) + "；暂不自动调整电源计划",
      GuardStatusCodes.Reasons.ProbeFailed => "无法读取当前电源计划；本次不切换",
      GuardStatusCodes.Reasons.Paused => "守护已暂停；保留当前计划",
      GuardStatusCodes.Reasons.ManualActive => "正在使用手动选择的高性能计划",
      GuardStatusCodes.Reasons.ForegroundIdentityUnknown => "暂时无法识别当前程序；保留当前计划",
      GuardStatusCodes.Reasons.ForegroundListEmpty => "尚未添加名单程序；保留当前计划",
      GuardStatusCodes.Reasons.ForegroundProgramPathsInvalid => "程序名单配置有误；保留当前计划",
      GuardStatusCodes.Reasons.WaitingForListedProgram => "当前未使用名单中的程序；保留当前计划",
      GuardStatusCodes.Reasons.ForegroundObservation => "正在观察程序切换；保留当前电源计划",
      GuardStatusCodes.Reasons.BlockedByLifecycle => "电脑正在休眠或恢复；暂不自动调整电源计划",
      _ => "自动守护",
    };
    return value.Action switch
    {
      "already-satisfied" => state + "；当前计划符合设置，无需切换。",
      "verified" => state + "；电源计划已切换，并已确认生效。",
      "failed" => state + "；未能确认电源计划切换成功。",
      "unknown" => state + "；暂时无法确认操作结果。",
      _ => state + "；本次没有切换电源计划。",
    };
  }

  private static string FailureLabel(string? kind) => kind switch
  {
    "missing" => "文件缺失", "parse" => "解析错误", "invalid" => "配置结构无效",
    "access" => "没有读取权限", "io" => "读取文件失败", _ => "原因未知",
  };

  private static string FriendlyProgram(string? name) => name switch
  {
    "wps.exe" => "WPS 文字", "et.exe" => "WPS 表格", "wpp.exe" => "WPS 演示", "wpspdf.exe" => "WPS PDF",
    _ => name ?? "名单程序",
  };
}
