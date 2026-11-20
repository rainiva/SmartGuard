namespace SmartGuard.Engine.Infrastructure;

internal sealed class PowerSourceTransitionTracker
{
  private int _lastObservedState = -1;

  internal bool Observe(bool isOnAc)
  {
    var currentState = isOnAc ? 1 : 0;
    var previousState = Interlocked.Exchange(ref _lastObservedState, currentState);
    return previousState >= 0 && previousState != currentState;
  }
}
