namespace SmartGuard.Tray.Toast;

public static class ToastAumidRegistrar
{
  internal static Func<string, bool>? StartMenuShortcutWriterForTests;
  internal static int ShortcutWriteCountForTests { get; private set; }
  internal static int AppUserModelIdWriteCountForTests { get; private set; }
  private static readonly object TestHookSync = new();

  public static bool EnsureRegistered(string root)
  {
    if (StartMenuShortcutWriterForTests is not null)
    {
      lock (TestHookSync)
        return EnsureRegisteredCore(root);
    }

    return EnsureRegisteredCore(root);
  }

  private static bool EnsureRegisteredCore(string root)
  {
    try
    {
      ToastRegistryWriter.RegisterAppUserModelId(
        root,
        StartMenuShortcutWriterForTests is not null ? () => AppUserModelIdWriteCountForTests++ : null);
      StartMenuShortcutWriter.EnsureShortcut(
        root,
        StartMenuShortcutWriterForTests,
        StartMenuShortcutWriterForTests is not null ? () => ShortcutWriteCountForTests++ : null);
      return true;
    }
    catch
    {
      return false;
    }
  }

  internal static void ResetForTests()
  {
    lock (TestHookSync)
    {
      StartMenuShortcutWriterForTests = null;
      ShortcutWriteCountForTests = 0;
      AppUserModelIdWriteCountForTests = 0;
    }
  }
}
