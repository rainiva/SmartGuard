using SmartGuard.Contracts;

namespace SmartGuard.Engine.Tests;

public class NotificationEventRetentionTests
{
  [Fact]
  public void Advance_stores_new_event_with_expiry()
  {
    var now = new DateTime(2026, 6, 16, 12, 0, 0);
    var evt = new NotificationEvent { id = "a" };

    var state = NotificationEventRetention.Advance(evt, NotificationEventRetentionState.Empty, now);

    state.Event.Should().BeSameAs(evt);
    state.ExpiresAt.Should().Be(now.Add(NotificationEventRetention.DefaultRetention));
  }

  [Fact]
  public void Advance_keeps_previous_event_until_expiry_when_no_new_event()
  {
    var now = new DateTime(2026, 6, 16, 12, 0, 0);
    var evt = new NotificationEvent { id = "a" };
    var stored = new NotificationEventRetentionState(evt, now.AddSeconds(60));

    var state = NotificationEventRetention.Advance(null, stored, now.AddSeconds(30));

    state.Event.Should().BeSameAs(evt);
  }

  [Fact]
  public void Advance_clears_event_after_expiry()
  {
    var now = new DateTime(2026, 6, 16, 12, 0, 0);
    var evt = new NotificationEvent { id = "a" };
    var stored = new NotificationEventRetentionState(evt, now);

    var state = NotificationEventRetention.Advance(null, stored, now.AddSeconds(1));

    state.Event.Should().BeNull();
  }
}
