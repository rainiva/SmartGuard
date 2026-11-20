namespace SmartGuard.Engine.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
  private DateTimeOffset _utcNow = new(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);
  private long _timestamp;

  public override DateTimeOffset GetUtcNow() => _utcNow;
  public override long GetTimestamp() => _timestamp;
  public override long TimestampFrequency => TimeSpan.TicksPerSecond;

  public void Advance(TimeSpan value)
  {
    _utcNow += value;
    _timestamp += value.Ticks;
  }

  public void AdvanceTimestamp(TimeSpan value) => _timestamp += value.Ticks;
  public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
}
