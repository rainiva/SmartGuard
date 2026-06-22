using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayStartupPerformanceTests
{
    [Fact]
    public void TrayApplicationContext_does_not_block_on_toast_registration()
    {
        var root = Path.Combine(Path.GetTempPath(), "SmartGuardTrayStartup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "lib"));
        File.WriteAllText(Path.Combine(root, "SmartGuard.config.json"), "{\"Paused\":false,\"NotifyOnPlanChange\":true}");

        ToastAumidRegistrar.ResetForTests();
        ToastAumidRegistrar.StartMenuShortcutWriterForTests = _ =>
        {
            Thread.Sleep(400);
            return true;
        };

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var context = new TrayApplicationContext(root);
            sw.Stop();

            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
                "tray should show before toast registration completes");
        }
        finally
        {
            ToastAumidRegistrar.ResetForTests();
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
