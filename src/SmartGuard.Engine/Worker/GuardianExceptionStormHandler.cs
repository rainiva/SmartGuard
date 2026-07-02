using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal sealed class GuardianExceptionStormHandler
{
  private readonly Dictionary<string, int> _exceptionCounts = new();
  private readonly GuardianLoopIterationState _iterationState;
  private readonly Action<GuardConfig, LogLevel, string> _writeLog;
  private DateTime _exceptionWindowStart = DateTime.MinValue;

  internal GuardianExceptionStormHandler(
    GuardianLoopIterationState iterationState,
    Action<GuardConfig, LogLevel, string> writeLog)
  {
    _iterationState = iterationState;
    _writeLog = writeLog;
  }

  internal void TrackAndLogException(Exception ex, GuardConfig config)
  {
    const int windowSeconds = 120;
    const int threshold = 5;
    var key = ex.GetType().Name;
    var now = DateTime.UtcNow;
    if (now - _exceptionWindowStart > TimeSpan.FromSeconds(windowSeconds))
    {
      _exceptionWindowStart = now;
      _exceptionCounts.Clear();
    }

    _exceptionCounts[key] = _exceptionCounts.GetValueOrDefault(key) + 1;
    var count = _exceptionCounts[key];
    var suffix = count >= threshold
      ? $" (同类异常 {count} 次，将重新初始化状态以恢复)"
      : string.Empty;
    _writeLog(config, LogLevel.Error, ex.Message + suffix);
    if (count >= threshold)
    {
      _iterationState.LastKnownGuid = null;
      _iterationState.LastBrightness = null;
      _iterationState.ScriptJustSwitched = false;
      _exceptionWindowStart = DateTime.MinValue;
      _exceptionCounts.Clear();
    }
  }
}
