namespace SmartGuard.Configuration;

/// <summary>
/// 背压控制器：监控 Normal 队列深度超过阈值时主动丢弃低价值行。
/// High 优先级不受背压影响。
/// </summary>
public sealed class LogBackpressureController
{
  private readonly int _thresholdPercent;

  public LogBackpressureController(int thresholdPercent = 80)
  {
    _thresholdPercent = Math.Clamp(thresholdPercent, 1, 100);
  }

  public bool ShouldThrottle(int normalQueueDepth, int normalQueueCapacity)
  {
    if (normalQueueCapacity <= 0) return false;
    return normalQueueDepth * 100 >= normalQueueCapacity * _thresholdPercent;
  }
}
