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

    [Fact]
    public async Task Late_scheduled_readers_still_release_their_pipes_on_the_unconfirmed_path()
    {
        // Readers that only start after the runner has returned must still dispose their pipe handles:
        // the token is captured before scheduling, so a disposed token source cannot skip the reader's
        // own disposal. Between the baseline and the measurement only the two queued readers run, inside
        // a no-GC region entered before the baseline, so no collection or finalizer can close handles.
        using var fixture = new DescendantFixture();
        var scheduler = new ManualTaskScheduler();
        PowerCfgProcessRunner.UseReaderSchedulerForTests(scheduler);
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        try
        {
            var result = await PowerCfgProcessRunner.RunProcessAsync("powershell.exe", fixture.ParentArguments, TimeSpan.FromSeconds(2));
            result.FailureKind.Should().Be("cleanup-unconfirmed", "the readers never ran, so EOF was never observed");
            fixture.KillAndWait();
            GC.TryStartNoGCRegion(16 * 1024 * 1024).Should().BeTrue();
            try
            {
                current.Refresh();
                var before = current.HandleCount;
                scheduler.RunPending().Should().Be(2);
                // A reader started with an already-cancelled token unwinds synchronously through its using.
                current.Refresh();
                (before - current.HandleCount).Should().BeGreaterThanOrEqualTo(2,
                    "both late readers disposed their pipe handles without a collection");
            }
            finally { GC.EndNoGCRegion(); }
        }
        finally
        {
            PowerCfgProcessRunner.UseReaderSchedulerForTests(null);
            scheduler.RunPending();
        }
    }
}
