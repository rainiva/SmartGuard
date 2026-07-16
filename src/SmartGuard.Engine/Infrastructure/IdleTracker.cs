namespace SmartGuard.Engine.Infrastructure;

public sealed class IdleTracker
{
  /// <summary>
  /// When wall-clock idle exceeds previous API idle by more than this slack,
  /// an API idle drop is treated as sleep/wake recovery rather than user activity.
  /// </summary>
  public const uint SleepRecoverySlackSeconds = 30;

  private DateTime _lastActivityUtc = DateTime.UtcNow;
  private uint _previousApiIdle;
  private bool _seeded;

  public uint Sample(Func<uint> readApiIdle, DateTime utcNow)
  {
    var apiIdle = readApiIdle();
    if (!_seeded)
    {
      _lastActivityUtc = utcNow.AddSeconds(-apiIdle);
      _seeded = true;
    }
    else
    {
      var wallIdle = (uint)Math.Max(0, (utcNow - _lastActivityUtc).TotalSeconds);
      if (ShouldResetForActivity(apiIdle, _previousApiIdle, wallIdle))
        _lastActivityUtc = utcNow.AddSeconds(-apiIdle);
    }

    _previousApiIdle = apiIdle;
    var finalWallIdle = (uint)Math.Max(0, (utcNow - _lastActivityUtc).TotalSeconds);
    return Math.Max(apiIdle, finalWallIdle);
  }

  public static bool IsUserActivity(uint apiIdle, uint previousApiIdle)
  {
    if (apiIdle <= 2) return true;
    if (apiIdle + 5 < previousApiIdle) return true;
    return false;
  }

  public static bool ShouldResetForActivity(uint apiIdle, uint previousApiIdle, uint wallIdle)
  {
    if (apiIdle <= 2) return true;

    if (apiIdle + 5 < previousApiIdle)
    {
      // Sleep/wake: wall advanced while GetLastInputInfo stalled; API snap-back is not activity.
      if (wallIdle > previousApiIdle + SleepRecoverySlackSeconds)
        return false;
      return true;
    }

    return false;
  }
}
