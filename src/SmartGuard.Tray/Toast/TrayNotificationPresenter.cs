namespace SmartGuard.Tray.Toast;

public interface IToastNotifier
{
  bool TryShow(string title, string body, string tag);
}

public enum TrayNotificationChannel
{
  Toast,
  Balloon,
}

public sealed class TrayNotificationPresenter(IToastNotifier toastNotifier)
{
  public TrayNotificationChannel Show(
    string title,
    string body,
    string tag,
    Action<string, string> showBalloon)
  {
    if (toastNotifier.TryShow(title, body, tag))
      return TrayNotificationChannel.Toast;

    showBalloon(title, body);
    return TrayNotificationChannel.Balloon;
  }
}
