using System.Text.Json;
using System.Windows.Forms;
using FluentAssertions;
using SmartGuard.Contracts;
using SmartGuard.Tray;
using SmartGuard.Tray.Toast;

namespace SmartGuard.Tray.Tests;

public class TrayRefreshSchedulerTests
{
    private sealed class FakeToastNotifier : IToastNotifier
    {
        public bool TryShow(string title, string body, string tag) => true;
    }

    [Fact]
    public void ScheduleRefresh_skips_disk_read_while_menu_open()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTrayRefresh_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.status.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new StatusPayload { currentPlan = "平衡" }));

        try
        {
            StaTestHost.Run(() =>
            {
                var store = new StatusStore(path);
                store.Read();
                store.ResetMetricsForTests();

                var sink = new Control();
                sink.CreateControl();
                var cache = new TrayDisplaySettingsCache(
                    new TrayNotificationPreferences(true, true),
                    () => new TrayNotificationPreferences(true, true));
                var presenter = new TrayNotificationPresenter(new FakeToastNotifier());
                var scheduler = new TrayRefreshScheduler(
                    dir,
                    store,
                    cache,
                    presenter,
                    sink,
                    _ => { });

                scheduler.ContextMenuOpen = true;
                scheduler.ScheduleRefresh();

                store.DiskReadCountForTests.Should().Be(0);
                scheduler.RefreshDeferredForTests.Should().BeTrue();
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
