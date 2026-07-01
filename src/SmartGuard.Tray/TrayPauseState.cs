namespace SmartGuard.Tray;

internal static class TrayPauseState
{
  internal static string MenuText(bool? statusPaused) =>
    statusPaused == true ? "恢复守护" : "暂停守护";

  internal static bool ToggleTarget(bool? statusPaused) =>
    !(statusPaused ?? false);
}
