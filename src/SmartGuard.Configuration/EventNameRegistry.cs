using System.Collections.Concurrent;

namespace SmartGuard.Configuration;

/// <summary>
/// 事件名称注册表：所有日志事件名称的唯一权威来源。
/// WriteLog 调用点必须使用本类常量，禁止硬编码事件名。
/// 门禁 ArchitectureContractLoggingPhase3Tests 扫描强制执行。
/// </summary>
public static class EventNameRegistry
{
  private static readonly ConcurrentDictionary<string, bool> _registry = new(StringComparer.Ordinal);

  // 内置事件名称预注册
  public const string AppStarted = nameof(AppStarted);
  public const string AppStopping = nameof(AppStopping);
  public const string PowerPlanSwitched = nameof(PowerPlanSwitched);
  public const string PowerSourceChanged = nameof(PowerSourceChanged);
  public const string HeartbeatTick = nameof(HeartbeatTick);
  public const string ExternalPlanChange = nameof(ExternalPlanChange);
  public const string BatteryStatusPolled = nameof(BatteryStatusPolled);
  public const string ConfigReloaded = nameof(ConfigReloaded);
  public const string IterationError = nameof(IterationError);
  public const string BrightnessAdjusted = nameof(BrightnessAdjusted);
  public const string IdleDetected = nameof(IdleDetected);
  public const string RecoveryDegraded = nameof(RecoveryDegraded);
  public const string RecoveryRestored = nameof(RecoveryRestored);
  public const string LogBackpressureApplied = nameof(LogBackpressureApplied);
  public const string LogDeduplicationEmitted = nameof(LogDeduplicationEmitted);
  public const string ConfigMutationLogged = nameof(ConfigMutationLogged);
  public const string ConfigMutationFailed = nameof(ConfigMutationFailed);
  public const string PipelineHealthSampled = nameof(PipelineHealthSampled);
  public const string InstallProgressLogged = nameof(InstallProgressLogged);
  public const string PowerSuspendObserved = nameof(PowerSuspendObserved);
  public const string PowerResumeObserved = nameof(PowerResumeObserved);
  public const string PowerLifecycleRecoveryForced = nameof(PowerLifecycleRecoveryForced);
  public const string PowerLifecycleSignalOverflow = nameof(PowerLifecycleSignalOverflow);
  public const string PowerPlanProbeFailed = nameof(PowerPlanProbeFailed);
  public const string PowerPlanApplyFailed = nameof(PowerPlanApplyFailed);
  public const string ForegroundListenerDegraded = nameof(ForegroundListenerDegraded);
  public const string ForegroundAdmissionChanged = nameof(ForegroundAdmissionChanged);
  public const string GuardStateChanged = nameof(GuardStateChanged);

  static EventNameRegistry()
  {
    Register(AppStarted);
    Register(AppStopping);
    Register(PowerPlanSwitched);
    Register(PowerSourceChanged);
    Register(HeartbeatTick);
    Register(ExternalPlanChange);
    Register(BatteryStatusPolled);
    Register(ConfigReloaded);
    Register(IterationError);
    Register(BrightnessAdjusted);
    Register(IdleDetected);
    Register(RecoveryDegraded);
    Register(RecoveryRestored);
    Register(LogBackpressureApplied);
    Register(LogDeduplicationEmitted);
    Register(ConfigMutationLogged);
    Register(ConfigMutationFailed);
    Register(PipelineHealthSampled);
    Register(InstallProgressLogged);
    Register(PowerSuspendObserved);
    Register(PowerResumeObserved);
    Register(PowerLifecycleRecoveryForced);
    Register(PowerLifecycleSignalOverflow);
    Register(PowerPlanProbeFailed);
    Register(PowerPlanApplyFailed);
    Register(ForegroundListenerDegraded);
    Register(ForegroundAdmissionChanged);
    Register(GuardStateChanged);
  }

  public static void Register(string name)
  {
    if (string.IsNullOrEmpty(name))
      throw new ArgumentException("Event name must not be null or empty.", nameof(name));
    _registry.TryAdd(name, true);
  }

  public static bool IsRegistered(string name) => _registry.ContainsKey(name);

  public static IReadOnlyCollection<string> AllRegistered => _registry.Keys.ToList().AsReadOnly();
}
