namespace SmartGuard.Engine.Infrastructure;

public sealed class IdleTracker
{
  private readonly TimeProvider _timeProvider;
  private long _lastSampleTimestamp;
  private uint _effectiveIdleSeconds;
  private uint _previousApiIdle;
  private bool _seeded;
  private bool _resumeResetPending;

  public IdleTracker(TimeProvider? timeProvider = null)
  {
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  public uint Sample(Func<uint> readApiIdle)
  {
    ArgumentNullException.ThrowIfNull(readApiIdle);
    var apiIdle = readApiIdle();
    var now = _timeProvider.GetTimestamp();

    if (!_seeded)
    {
      _effectiveIdleSeconds = _resumeResetPending ? 0u : apiIdle;
      _resumeResetPending = false;
      _seeded = true;
    }
    else
    {
      var elapsed = ToWholeSeconds(_timeProvider.GetElapsedTime(_lastSampleTimestamp, now));
      _effectiveIdleSeconds = IsUserActivity(apiIdle, _previousApiIdle)
        ? apiIdle
        : Math.Max(apiIdle, SaturatingAdd(_effectiveIdleSeconds, elapsed));
    }

    _previousApiIdle = apiIdle;
    _lastSampleTimestamp = now;
    return _effectiveIdleSeconds;
  }

  public void ResetAfterResume()
  {
    _seeded = false;
    _resumeResetPending = true;
  }

  public static bool IsUserActivity(uint apiIdle, uint previousApiIdle)
  {
    if (apiIdle <= 2) return true;
    return previousApiIdle > apiIdle && previousApiIdle - apiIdle > 5;
  }

  private static uint ToWholeSeconds(TimeSpan elapsed)
    => elapsed <= TimeSpan.Zero ? 0u : (uint)Math.Min(uint.MaxValue, elapsed.TotalSeconds);

  private static uint SaturatingAdd(uint left, uint right)
    => uint.MaxValue - left < right ? uint.MaxValue : left + right;
}
