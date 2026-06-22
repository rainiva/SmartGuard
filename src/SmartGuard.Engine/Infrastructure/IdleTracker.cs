namespace SmartGuard.Engine.Infrastructure;

public sealed class IdleTracker
{
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
    else if (IsUserActivity(apiIdle, _previousApiIdle))
    {
      _lastActivityUtc = utcNow;
    }

    _previousApiIdle = apiIdle;
    var wallIdle = (uint)Math.Max(0, (utcNow - _lastActivityUtc).TotalSeconds);
    return Math.Max(apiIdle, wallIdle);
  }

  public static bool IsUserActivity(uint apiIdle, uint previousApiIdle)
  {
    if (apiIdle <= 2) return true;
    if (apiIdle + 5 < previousApiIdle) return true;
    return false;
  }
}
