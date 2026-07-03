using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Tray;

public static class ExternalToolLauncher
{
  public static void OpenSettings(string root) => SettingsMainPageLauncher.Open(root);

  public static void OpenLogsPage(string root) => SettingsLogsPageLauncher.Open(root);
}

public static class TrayIconLoader
{
  internal static int LoadCountForTests => BrandIconLoader.WinFormsLoadCountForTests;

  internal static void ResetCacheForTests() => BrandIconLoader.ResetCacheForTests();

  public static Icon Load()
    => BrandIconLoader.LoadWinFormsIcon(SystemIcons.Shield);
}
