using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmartGuard.Settings;

public static class WindowTitleBarTheme
{
  private const int DwmwaUseImmersiveDarkMode = 20;
  private const int DwmwaUseImmersiveDarkModeOld = 19;

  private static readonly ConditionalWeakTable<Window, TitleBarThemeState> States = new();

  public static bool? GetLastRequestedDarkMode(Window window)
  {
    return States.TryGetValue(window, out var state)
      ? state.LastRequestedDarkMode
      : null;
  }

  [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
  private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

  public static void Apply(Window window, bool isDarkMode)
  {
    var state = States.GetOrCreateValue(window);
    state.LastRequestedDarkMode = isDarkMode;

    var helper = new WindowInteropHelper(window);
    if (helper.Handle == IntPtr.Zero)
    {
      void OnSourceInitialized(object? sender, EventArgs e)
      {
        window.SourceInitialized -= OnSourceInitialized;
        ApplyCore(window, isDarkMode);
      }

      window.SourceInitialized += OnSourceInitialized;
      return;
    }

    ApplyCore(window, isDarkMode);
  }

  private static void ApplyCore(Window window, bool isDarkMode)
  {
    var helper = new WindowInteropHelper(window);
    if (helper.Handle == IntPtr.Zero)
      return;

    var value = isDarkMode ? 1 : 0;
    _ = DwmSetWindowAttribute(helper.Handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    _ = DwmSetWindowAttribute(helper.Handle, DwmwaUseImmersiveDarkModeOld, ref value, sizeof(int));
  }

  private sealed class TitleBarThemeState
  {
    public bool LastRequestedDarkMode { get; set; }
  }
}
