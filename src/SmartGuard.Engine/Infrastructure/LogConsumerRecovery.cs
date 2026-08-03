using SmartGuard.Configuration;

namespace SmartGuard.Engine.Infrastructure;

/// <summary>
/// 消费者自恢复包装器：包装管线消费回调，异常/失败时推进状态机，
/// Failed 态下降级到 LogFallbackChain 同步写入，恢复后复位。
/// </summary>
public sealed class LogConsumerRecovery
{
  private readonly LogRecoveryStateMachine _stateMachine;

  public LogConsumerRecovery(LogRecoveryStateMachine stateMachine)
  {
    _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
  }

  public bool ExecuteWithRecovery(
    Func<IReadOnlyList<LogEvent>, bool> writeBatch,
    IReadOnlyList<LogEvent> batch,
    string fallbackLogPath)
  {
    if (_stateMachine.State == RecoveryState.Failed)
    {
      if (!_stateMachine.TryRecover())
      {
        foreach (var logEvent in batch)
          LogFallbackChain.WriteEvent(() => false, fallbackLogPath, logEvent);
        return true;
      }
    }

    try
    {
      if (writeBatch(batch))
      {
        _stateMachine.RecordSuccess();
        return true;
      }
      _stateMachine.RecordFailure();
      foreach (var logEvent in batch)
        LogFallbackChain.WriteEvent(() => false, fallbackLogPath, logEvent);
      return true;
    }
    catch
    {
      _stateMachine.RecordFailure();
      foreach (var logEvent in batch)
        LogFallbackChain.WriteEvent(() => false, fallbackLogPath, logEvent);
      return true;
    }
  }
}
