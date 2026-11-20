using System.Windows.Input;

namespace SmartGuard.Settings;

internal static class LogMatchNavigationCommands
{
  internal static bool TryHandleKeyDown(KeyEventArgs e, Action<LogMatchNavigationDirection> navigate)
    => TryHandleKey(e.Key, e.KeyboardDevice.Modifiers, navigate);

  internal static bool TryHandleKey(
    Key key,
    ModifierKeys modifiers,
    Action<LogMatchNavigationDirection> navigate)
  {
    if (key == Key.F3)
    {
      navigate(modifiers.HasFlag(ModifierKeys.Shift)
        ? LogMatchNavigationDirection.Backward
        : LogMatchNavigationDirection.Forward);
      return true;
    }

    if (key is Key.Enter or Key.Return)
    {
      navigate(modifiers.HasFlag(ModifierKeys.Shift)
        ? LogMatchNavigationDirection.Backward
        : LogMatchNavigationDirection.Forward);
      return true;
    }

    return false;
  }
}
