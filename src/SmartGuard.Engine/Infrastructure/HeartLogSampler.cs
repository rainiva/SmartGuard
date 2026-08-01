namespace SmartGuard.Engine.Infrastructure;

public sealed class HeartLogSampler
{
  private DateTime? _lastPersisted;

  public bool ShouldPersist(DateTime now, TimeSpan minInterval)
  {
    if (_lastPersisted is { } last && now - last < minInterval)
      return false; // 被节流：不更新 _lastPersisted，窗口起点不重置
    _lastPersisted = now;
    return true;
  }
}
