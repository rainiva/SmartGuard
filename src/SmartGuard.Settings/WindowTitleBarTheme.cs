using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmartGuard.Settings;

public static class WindowTitleBarTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeOld = 19;

    public static bool LastRequestedDarkMode { get; private set; }

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void Apply(Window window, bool isDarkMode)
    {
        LastRequestedDarkMode = isDarkMode;

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
}
