using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogHorizontalThumbRealMouseTests
{
    [Fact]
    public void Log_refresh_preserves_horizontal_offset_so_thumb_does_not_snap_back()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out var controller);
            using (ctx)
            {
                scrollViewer.ScrollableWidth.Should().BeGreaterThan(0);

                var target = Math.Min(scrollViewer.ScrollableWidth, 80);
                scrollViewer.ScrollToHorizontalOffset(target);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                scrollViewer.HorizontalOffset.Should().BeGreaterThan(10);

                controller.ForceRefreshLogView();
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(window);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                scrollViewer.HorizontalOffset.Should().BeGreaterThan(
                    10,
                    "horizontal offset must be preserved across log refreshes so the thumb does not snap back");
            }
        });
    }

    private static Scope CreateShownLogPageContext(out Window window, out ScrollViewer scrollViewer, out SettingsWindowController controller)
    {
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-realmouse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
        File.WriteAllText(
            Path.Combine(installRoot, "SmartGuard.log"),
            "[INFO] 2026-07-04 08:49:56 SmartGuard engine 启动。日志: " + new string('x', 600));

        var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
        File.WriteAllText(
            configPath,
            "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000,\"ThemeFollowSystem\":false,\"ThemeIsDark\":false}");
        var repository = new SmartGuard.Configuration.GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(installRoot);
        controller = SettingsWindowController.TryCreate(installRoot, repository, config)!;
        controller.Should().NotBeNull();

        var field = typeof(SettingsWindowController).GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance);
        window = (Window)field!.GetValue(controller)!;
        window.Width = 420;
        window.Height = 360;
        WpfStaTestHost.ShowAndWait(window);
        controller!.NavigateTo("logs");
        window.UpdateLayout();
        controller.EnsureLogScrollViewerHooked();
        SettingsLogNavResponsivenessTests.WaitUntilLogContent(window);

        scrollViewer = GetLogScrollViewer(window, controller);
        return new Scope(installRoot);
    }

    private static ScrollViewer GetLogScrollViewer(Window window, SettingsWindowController controller)
    {
        var hostField = typeof(SettingsWindowController).GetField(
            "_logPageHost",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var host = hostField!.GetValue(controller)!;
        var scrollViewerField = host.GetType().GetField(
            "_logScrollViewer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var scrollViewer = scrollViewerField!.GetValue(host) as ScrollViewer;
        scrollViewer.Should().NotBeNull();
        return scrollViewer!;
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _root;
        public Scope(string root) => _root = root;
        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }
    }
}
