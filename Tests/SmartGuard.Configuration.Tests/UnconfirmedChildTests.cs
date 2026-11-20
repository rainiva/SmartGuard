using System.Diagnostics;
using FluentAssertions;
using SmartGuard.Configuration;
using Xunit.Abstractions;

namespace SmartGuard.Configuration.Tests;

public class UnconfirmedChildTests(ITestOutputHelper output)
{
  private static Process ExitedChild()
  {
    var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true })!;
    process.WaitForExit(5000).Should().BeTrue();
    return process;
  }

  private static Task<PipeReadOutcome> Eof() => Task.FromResult(new PipeReadOutcome("", true));

  [Fact]
  public void Exited_child_with_both_pipes_at_eof_is_proven()
  {
    // The proof releases both; the test still owns them in case an assertion fails first (disposal is idempotent).
    using var process = ExitedChild();
    using var cts = new CancellationTokenSource();
    var child = UnconfirmedChild.FromProcess(process, Eof(), Eof(), cts, "pipes-open");
    child.TryConfirm().Should().BeTrue();
    child.Evidence.Should().Be("confirmed");
  }

  [Fact]
  public void Reader_that_ended_without_eof_is_not_proof()
  {
    // Never proven, so the child never releases them: the test owns and disposes both.
    using var process = ExitedChild();
    using var cts = new CancellationTokenSource();
    var child = UnconfirmedChild.FromProcess(process, Task.FromResult(new PipeReadOutcome("", false)), Eof(), cts, "pipes-open");
    child.TryConfirm().Should().BeFalse();
    child.Evidence.Should().Be("pipes-open");
  }

  [Theory]
  [InlineData("cancelled")]
  [InlineData("faulted")]
  public void Cancelled_or_faulted_reader_is_never_provable(string how)
  {
    var reader = how == "cancelled"
      ? Task.FromCanceled<PipeReadOutcome>(new CancellationToken(true))
      : Task.FromException<PipeReadOutcome>(new IOException());
    using var process = ExitedChild();
    using var cts = new CancellationTokenSource();
    var child = UnconfirmedChild.FromProcess(process, reader, Eof(), cts, "pipes-open");
    child.TryConfirm().Should().BeFalse();
    child.TryConfirm().Should().BeFalse();
    child.Evidence.Should().Be("reader-failed");
  }

  [Fact]
  public void Execution_proof_follows_its_result_and_a_faulted_execution_is_unprovable()
  {
    var running = new TaskCompletionSource<PowerCfgCommandResult>();
    var held = UnconfirmedChild.FromExecution(running.Task);
    held.TryConfirm().Should().BeFalse();
    held.Evidence.Should().Be("executor-pending");
    var released = false;
    running.SetResult(new PowerCfgCommandResult(true, false, null, "", "")
    {
      FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false,
      PendingCleanup = UnconfirmedChild.ForTests(() => (released, released ? "confirmed" : "pipes-open"), "pipes-open"),
    });
    held.TryConfirm().Should().BeFalse();
    held.Evidence.Should().Be("pipes-open");
    released = true;
    held.TryConfirm().Should().BeTrue();

    UnconfirmedChild.FromExecution(Task.FromException<PowerCfgCommandResult>(new InvalidOperationException()))
      .TryConfirm().Should().BeFalse();
    UnconfirmedChild.Unprovable().TryConfirm().Should().BeFalse();
  }

  [Fact]
  public void Confirmation_cost_is_constant_on_the_exited_and_the_kill_paths()
  {
    const int samples = 200;
    var open = new TaskCompletionSource<PipeReadOutcome>();

    // Exited path: the child exited but a reader never finished, so every call checks state only.
    var exitedCosts = new double[samples];
    var exitedResults = new bool[samples];
    using (var exitedProcess = ExitedChild())
    using (var exitedCts = new CancellationTokenSource())
    {
      var exited = UnconfirmedChild.FromProcess(exitedProcess, open.Task, Eof(), exitedCts, "pipes-open");
      for (var i = 0; i < samples; i++)
      {
        var started = Stopwatch.GetTimestamp();
        exitedResults[i] = exited.TryConfirm();
        exitedCosts[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
      }
    }
    exitedResults.Should().AllBeEquivalentTo(false);

    // Kill path: one sample at a time, each with a fresh live child that has no descendants
    // ("cmd /c pause" blocks on its redirected stdin). Only TryConfirm is inside the timed region.
    var killCosts = new double[samples];
    for (var i = 0; i < samples; i++)
    {
      Process? child = null;
      try
      {
        child = Process.Start(new ProcessStartInfo("cmd.exe", "/c pause")
          { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true })
          ?? throw new InvalidOperationException("fixture child did not start");
        using var cts = new CancellationTokenSource();
        var held = UnconfirmedChild.FromProcess(child, open.Task, open.Task, cts, "not-exited");
        var started = Stopwatch.GetTimestamp();
        var confirmed = held.TryConfirm();
        killCosts[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        confirmed.Should().BeFalse();
        held.Evidence.Should().Be("not-exited");
      }
      finally
      {
        if (child is not null)
        {
          try { if (!child.HasExited) child.Kill(); } catch (InvalidOperationException) { }
          child.WaitForExit(5000).Should().BeTrue("each sample ends its own child before the next starts");
          child.Dispose();
        }
      }
    }

    Report("exited", exitedCosts);
    Report("kill", killCosts);
    exitedCosts.Max().Should().BeLessThan(50);
    killCosts.Max().Should().BeLessThan(50);
  }

  private void Report(string path, double[] costs)
  {
    var sorted = costs.Order().ToArray();
    output.WriteLine($"{path}: P50={sorted[sorted.Length / 2]:F3}ms P95={sorted[(int)(sorted.Length * 0.95)]:F3}ms max={sorted[^1]:F3}ms");
  }
}
