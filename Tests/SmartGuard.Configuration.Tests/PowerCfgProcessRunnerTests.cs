using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class PowerCfgProcessRunnerTests
{
    [Fact]
    public async Task RunProcess_pre_cancel_does_not_start()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await PowerCfgProcessRunner.RunProcessAsync("cmd.exe", "/c echo forbidden", TimeSpan.FromSeconds(2), cancellation.Token);
        result.Started.Should().BeFalse();
        result.FailureKind.Should().Be("cancelled");
    }

    [Theory]
    [InlineData("Out")]
    [InlineData("Error")]
    public async Task RunProcess_limits_each_output_stream(string stream)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await PowerCfgProcessRunner.RunProcessAsync("powershell.exe",
            $"-NoProfile -Command \"[Console]::{stream}.Write(('x' * 100000)); Start-Sleep -Seconds 10\"", TimeSpan.FromSeconds(5));
        result.Succeeded.Should().BeFalse();
        result.StandardOutput.Length.Should().BeLessThanOrEqualTo(65536);
        result.StandardError.Length.Should().BeLessThanOrEqualTo(65536);
        result.FailureKind.Should().Be("output-limit");
        result.TerminationConfirmed.Should().BeTrue();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task RunProcess_cancellation_is_bounded_and_confirmed()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await PowerCfgProcessRunner.RunProcessAsync("cmd.exe", "/c ping localhost -n 20 > nul", TimeSpan.FromSeconds(10), cancellation.Token);
        result.FailureKind.Should().Be("cancelled");
        result.TerminationConfirmed.Should().BeTrue();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RunProcess_deadline_covers_descendant_held_pipes_and_reports_uncertain_cleanup()
    {
        var pidPath = Path.Combine(Path.GetTempPath(), $"smartguard-process-test-{Guid.NewGuid():N}.pid");
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await PowerCfgProcessRunner.RunProcessAsync("powershell.exe",
                $"-NoProfile -Command \"$p = Start-Process powershell.exe -ArgumentList '-NoProfile -Command Start-Sleep -Seconds 20' -NoNewWindow -PassThru; [IO.File]::WriteAllText('{pidPath}', [string]$p.Id);\"",
                TimeSpan.FromSeconds(2));
            result.Succeeded.Should().BeFalse();
            result.FailureKind.Should().Be("cleanup-unconfirmed");
            result.TerminationConfirmed.Should().BeFalse();
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        }
        finally
        {
            // This child deliberately outlives its parent. The fixture, not the runner, owns
            // its explicit PID and must remove it even when an assertion fails.
            if (File.Exists(pidPath))
            {
                var pid = int.Parse(File.ReadAllText(pidPath, System.Text.Encoding.UTF8));
                try
                {
                    using var child = System.Diagnostics.Process.GetProcessById(pid);
                    child.Kill(entireProcessTree: true);
                    child.WaitForExit(1000).Should().BeTrue();
                }
                catch (ArgumentException) { }
                File.Delete(pidPath);
            }
        }
    }

    [Fact]
    public async Task RunProcess_rejects_finite_oversize_output()
    {
        var result = await PowerCfgProcessRunner.RunProcessAsync("powershell.exe",
            "-NoProfile -Command \"[Console]::Out.Write(('x' * 100000))\"", TimeSpan.FromSeconds(5));
        result.Succeeded.Should().BeFalse();
        result.FailureKind.Should().Be("output-limit");
    }

    [Fact]
    public void RunProcess_reports_timeout_and_kills_child_when_wait_exceeds_timeout()
    {
        var result = PowerCfgProcessRunner.RunProcess(
            "cmd.exe",
            "/c ping localhost -n 8 > nul",
            TimeSpan.FromSeconds(1));

        result.Started.Should().BeTrue();
        result.TimedOut.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void RunProcess_returns_output_for_fast_command()
    {
        var result = PowerCfgProcessRunner.RunProcess(
            "cmd.exe",
            "/c echo hello-powercfg-runner",
            TimeSpan.FromSeconds(5));

        result.Succeeded.Should().BeTrue();
        result.StandardOutput.Should().Contain("hello-powercfg-runner");
    }

    [Fact]
    public void RunProcess_reports_start_failure_without_faking_success()
    {
        var result = PowerCfgProcessRunner.RunProcess(
            "missing-powercfg-test-executable.exe",
            "",
            TimeSpan.FromSeconds(1));

        result.Started.Should().BeFalse();
        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().BeNull();
    }

    [Fact]
    public async Task Pipes_closing_inside_the_settle_wait_prove_cleanup_despite_the_parent_exiting_first()
    {
        // No `using`: TestTaskSettle owns these and releases them only after every task has ended.
        var fixture = new DescendantFixture();
        var stop = new CancellationTokenSource();
        Task<PowerCfgCommandResult>? run = null;
        PowerCfgProcessRunner.UseAfterTerminationRequestForTests(fixture.SignalDescendantExit);
        try
        {
            run = PowerCfgProcessRunner.RunProcessAsync("powershell.exe", fixture.ParentArguments, TimeSpan.FromSeconds(15), stop.Token);
            fixture.WaitParentExitedWithDescendantAlive(TimeSpan.FromSeconds(10))
                .Should().BeTrue("the scenario needs an exited parent whose descendant still holds the pipes");
            stop.Cancel();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(10));
            result.FailureKind.Should().Be("cancelled");
            result.TerminationConfirmed.Should().BeTrue("both pipes reached EOF inside the 250ms settle wait");
            result.CleanupEvidence.Should().Be("confirmed");
        }
        finally
        {
            // Unblock everything first, then wait for every task; resources go last.
            PowerCfgProcessRunner.UseAfterTerminationRequestForTests(null);
            stop.Cancel();
            fixture.KillAndWait();
            await TestTaskSettle.SettleAsync([run], stop, fixture);
        }
    }

    [Fact]
    public async Task Pipes_closing_after_the_settle_wait_leave_cleanup_unconfirmed()
    {
        // No `using`: TestTaskSettle owns these and releases them only after every task has ended.
        var fixture = new DescendantFixture();
        var stop = new CancellationTokenSource();
        Task<PowerCfgCommandResult>? run = null;
        Task? lateSignal = null;
        // The descendant dies one second after the termination request, well outside the 250ms budget.
        PowerCfgProcessRunner.UseAfterTerminationRequestForTests(() =>
            lateSignal = Task.Delay(1000).ContinueWith(_ => fixture.SignalDescendantExit(), TaskScheduler.Default));
        try
        {
            run = PowerCfgProcessRunner.RunProcessAsync("powershell.exe", fixture.ParentArguments, TimeSpan.FromSeconds(15), stop.Token);
            fixture.WaitParentExitedWithDescendantAlive(TimeSpan.FromSeconds(10)).Should().BeTrue();
            stop.Cancel();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(10));
            result.FailureKind.Should().Be("cleanup-unconfirmed");
            result.CleanupEvidence.Should().Be("pipes-open");
            result.PendingCleanup.Should().BeNull("a caller that does not retain gets nothing to own");
        }
        finally
        {
            // Unblock everything first, then wait for every task (one timing out never skips the other).
            PowerCfgProcessRunner.UseAfterTerminationRequestForTests(null);
            stop.Cancel();
            fixture.KillAndWait();
            await TestTaskSettle.SettleAsync([run, lateSignal], stop, fixture);
        }
    }

    [Fact]
    public async Task Held_pipes_after_parent_exit_are_reported_as_pipes_open()
    {
        using var fixture = new DescendantFixture();
        var result = await PowerCfgProcessRunner.RunProcessAsync("powershell.exe", fixture.ParentArguments, TimeSpan.FromSeconds(2));
        result.FailureKind.Should().Be("cleanup-unconfirmed");
        result.TerminationConfirmed.Should().BeFalse();
        result.CleanupEvidence.Should().Be("pipes-open");
        result.PendingCleanup.Should().BeNull("a caller that does not retain gets nothing to own");
    }
}
