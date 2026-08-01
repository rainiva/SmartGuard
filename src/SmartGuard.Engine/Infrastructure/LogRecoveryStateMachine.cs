namespace SmartGuard.Engine.Infrastructure;

public enum RecoveryState { Healthy, Degraded, Failed }

/// <summary>
/// 故障恢复状态机：跟踪管线消费侧 Healthy/Degraded/Failed 三态。
/// 连续失败 N 次进入 Failed；Failed 后经定时探测恢复至 Degraded，
/// 再次成功回到 Healthy。
/// </summary>
public sealed class LogRecoveryStateMachine
{
  private readonly int _failureThreshold;
  private readonly int _recoveryIntervalSeconds;
  private int _consecutiveFailures;
  private DateTime? _failedAt;
  private RecoveryState _state = RecoveryState.Healthy;

  public LogRecoveryStateMachine(int failureThreshold = 3, int recoveryIntervalSeconds = 30)
  {
    _failureThreshold = Math.Max(1, failureThreshold);
    _recoveryIntervalSeconds = Math.Max(0, recoveryIntervalSeconds);
  }

  public RecoveryState State => _state;

  public void RecordSuccess()
  {
    _consecutiveFailures = 0;
    _failedAt = null;
    _state = RecoveryState.Healthy;
  }

  public void RecordFailure()
  {
    _consecutiveFailures++;
    _state = RecoveryState.Degraded;
    if (_consecutiveFailures >= _failureThreshold)
    {
      _state = RecoveryState.Failed;
      _failedAt ??= DateTime.UtcNow;
    }
  }

  public bool TryRecover()
  {
    if (_state != RecoveryState.Failed || _failedAt is null) return false;
    if ((DateTime.UtcNow - _failedAt.Value).TotalSeconds < _recoveryIntervalSeconds) return false;
    _state = RecoveryState.Degraded;
    _failedAt = null;
    return true;
  }
}
