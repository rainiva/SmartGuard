using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("ProcessHandleMeasurement")]
public class PowerCfgProcessRunnerHandleTests
{
    private const int Runs = 32;

    [Fact]
    public async Task RunProcess_releases_redirected_pipe_handles_without_waiting_for_gc()
    {
        // Warm the runtime/process path before measuring. Suppressing collections makes
        // this exercise deterministic ownership rather than eventual finalizer cleanup.
        for (var i = 0; i < 4; i++)
            (await PowerCfgProcessRunner.RunProcessAsync("cmd.exe", "/c echo warm", TimeSpan.FromSeconds(5))).Succeeded.Should().BeTrue();

        using var current = System.Diagnostics.Process.GetCurrentProcess();
        GC.TryStartNoGCRegion(128 * 1024 * 1024).Should().BeTrue();
        try
        {
            current.Refresh();
            var before = current.HandleCount;
            for (var i = 0; i < Runs; i++)
                (await PowerCfgProcessRunner.RunProcessAsync("cmd.exe", "/c echo measured", TimeSpan.FromSeconds(5))).Succeeded.Should().BeTrue();
            current.Refresh();
            // Cost model: runtime process-wait registrations can retain about one handle per run until
            // GC (~Runs), while leaking both redirected pipes adds two more per run (>= 3 * Runs). The bound
            // of two per run sits between the two with a wide margin either side; this test runs alone in
            // its collection, because the count is process-wide.
            (current.HandleCount - before).Should().BeLessThan(2 * Runs,
                "each completed command must release both redirected pipe handles without a collection");
        }
        finally
        {
            GC.EndNoGCRegion();
        }
    }
}
