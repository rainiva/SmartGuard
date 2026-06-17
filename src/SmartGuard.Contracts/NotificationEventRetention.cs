namespace SmartGuard.Contracts;

public static class NotificationEventRetention
{
  public static readonly TimeSpan DefaultRetention = TimeSpan.FromSeconds(60);

  public static NotificationEventRetentionState Advance(
    NotificationEvent? newEvent,
    NotificationEventRetentionState state,
    DateTime now,
    TimeSpan? retention = null)
  {
    var duration = retention ?? DefaultRetention;

    if (newEvent is not null)
    {
      return new NotificationEventRetentionState(newEvent, now.Add(duration));
    }

    if (state.Event is not null && now <= state.ExpiresAt)
    {
      return state;
    }

    return NotificationEventRetentionState.Empty;
  }
}

public readonly record struct NotificationEventRetentionState(
  NotificationEvent? Event,
  DateTime ExpiresAt)
{
  public static NotificationEventRetentionState Empty { get; } = new(null, DateTime.MinValue);
}
