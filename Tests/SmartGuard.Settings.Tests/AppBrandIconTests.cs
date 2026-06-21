using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class AppBrandIconTests
{
    private static void EnsureApplication()
    {
        if (Application.Current is not null)
            return;

        try
        {
            _ = new Application();
        }
        catch (InvalidOperationException)
        {
            // Another STA thread in the same AppDomain already created the Application instance.
        }
    }

    [Fact]
    public void LoadImageSource_uses_taskbar_safe_icon_size_matching_tray_default()
    {
        RunOnSta(() =>
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
            var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));
            var iconPath = Path.Combine(repoRoot, "lib", "SmartGuard.ico");

            File.Exists(iconPath).Should().BeTrue();

            using var trayIcon = new Icon(iconPath, 32, 32);
            var settingsIcon = AppBrandIcon.LoadImageSource(repoRoot);

            settingsIcon.Should().NotBeNull();
            settingsIcon!.Width.Should().BeLessThanOrEqualTo(32,
                "taskbar icon must not use a large .ico frame such as 256x256");
            settingsIcon.Height.Should().BeLessThanOrEqualTo(32);
            settingsIcon.Width.Should().Be(trayIcon.Width);
            settingsIcon.Height.Should().Be(trayIcon.Height);
        });
    }

    [Fact]
    public void LoadImageSource_reads_install_root_icon_file()
    {
        RunOnSta(() =>
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
            var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));
            var iconPath = Path.Combine(repoRoot, "lib", "SmartGuard.ico");

            File.Exists(iconPath).Should().BeTrue();

            var image = AppBrandIcon.LoadImageSource(repoRoot);
            image.Should().NotBeNull();
            image!.Width.Should().BeGreaterThan(0);
            image.Height.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void ApplyTo_sets_window_icon_from_install_root()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
            var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));

            var window = new Window();
            AppBrandIcon.ApplyTo(window, repoRoot);

            window.Icon.Should().NotBeNull();
            window.Icon!.Width.Should().BeGreaterThan(0);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw error;
    }
}
