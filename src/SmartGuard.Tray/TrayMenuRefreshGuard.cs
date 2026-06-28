namespace SmartGuard.Tray;

internal static class TrayMenuRefreshGuard
{
  public static bool ShouldDefer(bool contextMenuOpen) => contextMenuOpen;
}
