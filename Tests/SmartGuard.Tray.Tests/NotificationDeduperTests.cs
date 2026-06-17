using SmartGuard.Contracts;

namespace SmartGuard.Tray.Tests;

public class NotificationDeduperTests
{
  [Fact]
  public void ShouldShow_returns_false_for_same_event_id()
  {
    var evt = new NotificationEvent { id = "abc" };
    NotificationDeduper.ShouldShow("abc", evt).Should().BeFalse();
  }

  [Fact]
  public void ShouldShow_returns_true_for_new_event_id()
  {
    var evt = new NotificationEvent { id = "xyz" };
    NotificationDeduper.ShouldShow("abc", evt).Should().BeTrue();
  }
}
