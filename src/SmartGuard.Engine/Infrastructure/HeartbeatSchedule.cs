namespace SmartGuard.Engine.Infrastructure;

public sealed class HeartbeatSchedule
{
  private readonly TimeProvider _timeProvider;
  private long? _lastAcceptedTimestamp;
  private bool _resumeDue = true;

  public HeartbeatSchedule(TimeProvider? timeProvider = null)
  {
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  public bool IsDue(TimeSpan heartbeatInterval, TimeSpan minimumPersistInterval)
  {
    if (_resumeDue || _lastAcceptedTimestamp is null)
      return true;

    var effectiveInterval = heartbeatInterval >= minimumPersistInterval
      ? heartbeatInterval
      : minimumPersistInterval;
    return _timeProvider.GetElapsedTime(_lastAcceptedTimestamp.Value) >= effectiveInterval;
  }

  public void MarkAccepted()
  {
    _lastAcceptedTimestamp = _timeProvider.GetTimestamp();
    _resumeDue = false;
  }

  public void ResetAfterResume() => _resumeDue = true;
}
