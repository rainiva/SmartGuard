using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace SmartGuard.Tray.Toast;

public sealed class WinRtToastNotifier(string root) : IToastNotifier
{
  public bool TryShow(string title, string body, string tag)
  {
    if (string.IsNullOrWhiteSpace(title)) return false;
    try
    {
      ToastAumidRegistrar.EnsureRegistered(root);
      var xml = ToastNotificationXmlBuilder.Build(title, body);
      var doc = new XmlDocument();
      doc.LoadXml(xml);
      var toast = new ToastNotification(doc) { Tag = tag };
      var notifier = ToastNotificationManager.CreateToastNotifier(SmartGuardToastAppId.AppId);
      notifier.Show(toast);
      return true;
    }
    catch
    {
      return false;
    }
  }
}
