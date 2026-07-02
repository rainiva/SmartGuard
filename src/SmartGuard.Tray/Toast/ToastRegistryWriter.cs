using Microsoft.Win32;
using SmartGuard.Configuration;

namespace SmartGuard.Tray.Toast;

internal static class ToastRegistryWriter
{
  internal static void RegisterAppUserModelId(string root, Action? onWriteForTests = null)
  {
    if (File.Exists(GetAumidMarkerPath(root)))
      return;

    var regPath = $@"Software\Classes\AppUserModelId\{SmartGuardToastAppId.AppId}";
    using var key = Registry.CurrentUser.CreateSubKey(regPath, true);
    if (key is null) return;
    key.SetValue("DisplayName", SmartGuardToastAppId.DisplayName, RegistryValueKind.String);
    var iconPath = SmartGuardPaths.BrandIcon(root);
    if (File.Exists(iconPath))
    {
      var iconUri = new Uri(iconPath).AbsoluteUri;
      key.SetValue("IconUri", iconUri, RegistryValueKind.String);
    }

    onWriteForTests?.Invoke();
    MarkAumidRegistered(root);
  }

  private static string GetAumidMarkerPath(string root)
    => Path.Combine(root, "lib", ".smartguard-toast-aumid");

  private static void MarkAumidRegistered(string root)
  {
    var markerPath = GetAumidMarkerPath(root);
    var markerDir = Path.GetDirectoryName(markerPath);
    if (!string.IsNullOrEmpty(markerDir))
      Directory.CreateDirectory(markerDir);
    File.WriteAllText(markerPath, DateTime.UtcNow.ToString("o"));
  }
}
