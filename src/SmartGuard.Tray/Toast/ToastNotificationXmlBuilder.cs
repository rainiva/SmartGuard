using System.Security;

namespace SmartGuard.Tray.Toast;

public static class ToastNotificationXmlBuilder
{
  public static string Build(string title, string body)
  {
    var safeTitle = SecurityElement.Escape(title) ?? string.Empty;
    var safeBody = SecurityElement.Escape(body) ?? string.Empty;
    return $"""
<toast activation="protocol">
  <visual>
    <binding template="ToastGeneric">
      <text hint-maxLines="1">{safeTitle}</text>
      <text hint-style="subtitle">{safeBody}</text>
    </binding>
  </visual>
  <audio src="ms-winsoundevent:Notification.Reminder"/>
</toast>
""";
  }
}
