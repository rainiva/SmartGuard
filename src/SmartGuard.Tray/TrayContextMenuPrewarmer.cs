namespace SmartGuard.Tray;

internal static class TrayContextMenuPrewarmer
{
  public static void WarmUp(ContextMenuStrip menu)
  {
    if (!menu.IsHandleCreated)
      _ = menu.Handle;

    menu.PerformLayout();
  }
}
