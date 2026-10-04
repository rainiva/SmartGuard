using SmartGuard.Configuration;

namespace SmartGuard.Engine.Infrastructure;

/// <summary>
/// 消费者自恢复包装器：包装管线消费回调，异常/失败时推进状态机，
/// 冷却只控制主 sink 重试；回退确认不表示主写恢复，结果按记录分区。
/// </summary>
public sealed class LogConsumerRecovery
{
  private readonly LogRecoveryStateMachine _stateMachine;
  public RecoveryState State => _stateMachine.State;

  public LogConsumerRecovery(LogRecoveryStateMachine stateMachine)
  {
    _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
  }

  public LogBatchWriteResult ExecuteWithRecovery(
    Func<IReadOnlyList<LogEvent>, bool> writeBatch,
    IReadOnlyList<LogEvent> batch,
    string fallbackLogPath,
    bool primaryAvailable = true)
  {
    if (!primaryAvailable || (_stateMachine.State == RecoveryState.Failed && !_stateMachine.TryRecover()))
    {
      return LogFallbackChain.WriteBatch(fallbackLogPath, batch, primaryAttemptFailed: false);
    }

    try
    {
      if (writeBatch(batch))
      {
        _stateMachine.RecordSuccess();
        return LogBatchWriteResult.Primary(batch.Count);
      }
    }
    catch
    {
      // A throwing primary writer confirms no prefix; replay once per sink.
    }
    _stateMachine.RecordFailure();
    return LogFallbackChain.WriteBatch(fallbackLogPath, batch, primaryAttemptFailed: true);
  }
}
