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
    Func<IReadOnlyList<string>, bool> writeBatch,
    IReadOnlyList<string> batch,
    string fallbackLogPath)
  {
    // Failed 态下尝试恢复探测
    if (_stateMachine.State == RecoveryState.Failed)
    {
      if (!_stateMachine.TryRecover())
      {
        // 仍在 Failed 态：降级到同步写入
        foreach (var line in batch)
          LogFallbackChain.Write(() => false, fallbackLogPath, line);
        return true; // 已降级处置，不让管线重复计失败批次
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
      // 降级处置每行
      foreach (var line in batch)
        LogFallbackChain.Write(() => false, fallbackLogPath, line);
      return true; // 已降级处置完毕
    }
    catch
    {
      _stateMachine.RecordFailure();
      foreach (var line in batch)
        LogFallbackChain.Write(() => false, fallbackLogPath, line);
      return true; // 异常隔离，不让管线上抛
    }
  }
}
