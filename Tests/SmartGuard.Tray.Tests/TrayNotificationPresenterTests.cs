namespace SmartGuard.Tray.Tests;

public class TrayNotificationPresenterTests
{
  [Fact]
  public void Show_uses_toast_when_available()
  {
    var toast = new FakeToastNotifier { Result = true };
    var presenter = new TrayNotificationPresenter(toast);
    var balloonCalled = false;

    var shown = presenter.Show("标题", "正文", "tag-1", (_, _) => balloonCalled = true);

    shown.Should().Be(TrayNotificationChannel.Toast);
    toast.LastTag.Should().Be("tag-1");
    balloonCalled.Should().BeFalse();
  }

  [Fact]
  public void Show_falls_back_to_balloon_when_toast_fails()
  {
    var toast = new FakeToastNotifier { Result = false };
    var presenter = new TrayNotificationPresenter(toast);
    string? balloonTitle = null;
    string? balloonBody = null;

    var shown = presenter.Show("标题", "正文", "tag-2", (t, b) =>
    {
      balloonTitle = t;
      balloonBody = b;
    });

    shown.Should().Be(TrayNotificationChannel.Balloon);
    balloonTitle.Should().Be("标题");
    balloonBody.Should().Be("正文");
  }

  private sealed class FakeToastNotifier : IToastNotifier
  {
    public bool Result { get; init; }
    public string? LastTag { get; private set; }

    public bool TryShow(string title, string body, string tag)
    {
      LastTag = tag;
      return Result;
    }
  }
}
