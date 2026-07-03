using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayStartupPerformanceTests
{
    [Fact]
    public void TrayApplicationContext_startup_does_not_wait_on_shell_registration()
    {
        var root = Path.Combine(Path.GetTempPath(), "SmartGuardTrayStartup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "lib"));
        File.WriteAllText(Path.Combine(root, "SmartGuard.config.json"), "{\"Paused\":false,\"NotifyOnPlanChange\":true}");

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
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
