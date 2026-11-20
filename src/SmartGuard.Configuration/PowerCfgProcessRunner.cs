using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SmartGuard.Configuration;

internal static class PowerCfgProcessRunner
{
  private static readonly Encoding ConsoleEncoding = CreateConsoleEncoding();
  private static readonly AsyncLocal<Action?> AfterTerminationRequest = new();
  private static readonly AsyncLocal<TaskScheduler?> ReaderScheduler = new();

  // Test-only: runs after the termination request and before the 250ms settle wait, to fix race timing.
  internal static void UseAfterTerminationRequestForTests(Action? hook) => AfterTerminationRequest.Value = hook;

  // Test-only: lets a test run the reader tasks late, after the runner has returned.
  internal static void UseReaderSchedulerForTests(TaskScheduler? scheduler) => ReaderScheduler.Value = scheduler;

  internal static PowerCfgCommandResult RunProcess(string fileName, string arguments, TimeSpan timeout)
  {
    return RunProcessAsync(fileName, arguments, timeout, CancellationToken.None)
      .GetAwaiter()
      .GetResult();
  }

  internal static Task<PowerCfgCommandResult> RunProcessAsync(
    string fileName,
    string arguments,
    TimeSpan timeout,
    CancellationToken cancellationToken = default,
    bool retainUnconfirmed = false)
    => RunProcessCoreAsync(fileName, arguments, timeout, cancellationToken, retainUnconfirmed);

  private static async Task<PowerCfgCommandResult> RunProcessCoreAsync(
    string fileName,
    string arguments,
    TimeSpan timeout,
    CancellationToken cancellationToken,
    bool retainUnconfirmed)
  {
    var elapsed = Stopwatch.StartNew();
    PowerCfgCommandResult Result(bool started, string failure, bool confirmed = true, int? exitCode = null,
      string output = "", string error = "") => new(started, failure == "timed-out", exitCode, output, error)
      { FailureKind = failure, TerminationConfirmed = confirmed, ElapsedMilliseconds = elapsed.ElapsedMilliseconds };

    if (cancellationToken.IsCancellationRequested)
      return Result(false, "cancelled");

    using var timeoutCts = new CancellationTokenSource(timeout);
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
    var psi = new ProcessStartInfo(fileName, arguments)
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = ConsoleEncoding,
      StandardErrorEncoding = ConsoleEncoding,
      UseShellExecute = false,
      CreateNoWindow = true,
    };

    Process? proc;
    try
    {
      proc = Process.Start(psi);
    }
    catch (Exception)
    {
      return Result(false, "start-failed");
    }

    if (proc is null)
      return Result(false, "start-failed");

    var readerCts = new CancellationTokenSource();
    // Taken before scheduling: a reader that starts after the source was disposed must still enter
    // ReadBoundedAsync, which owns the reader's disposal.
    var readerToken = readerCts.Token;
    var failureSignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    // Anonymous pipe implementations may perform synchronous work before their first await.
    // Capture before scheduling: a late worker must not access an already-disposed Process.
    // Reading the properties transfers stream ownership away from Process.Dispose.
    var standardOutput = proc.StandardOutput;
    var standardError = proc.StandardError;
    var readOutTask = StartReader(standardOutput, failureSignal, readerToken);
    var readErrTask = StartReader(standardError, failureSignal, readerToken);
    var exitTask = proc.WaitForExitAsync();
    var completion = Task.WhenAll(exitTask, readOutTask, readErrTask);
    string failure;

    try
    {
      // The same deadline covers EOF: a descendant can keep redirected pipes open after its parent exits.
      var winner = await Task.WhenAny(completion, failureSignal.Task).WaitAsync(linkedCts.Token).ConfigureAwait(false);
      if (winner == failureSignal.Task || failureSignal.Task.IsCompletedSuccessfully)
        failure = await failureSignal.Task.ConfigureAwait(false);
      else
      {
        await completion.ConfigureAwait(false);
        linkedCts.Token.ThrowIfCancellationRequested();
        var output = (await readOutTask.ConfigureAwait(false)).Text;
        var error = (await readErrTask.ConfigureAwait(false)).Text;
        var exitCode = proc.ExitCode;
        Release(proc, readerCts);
        return Result(true, "none", exitCode: exitCode, output: output, error: error) with { CleanupEvidence = "confirmed" };
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      failure = "cancelled";
    }
    catch (OperationCanceledException)
    {
      failure = "timed-out";
    }
    catch (Exception)
    {
      failure = "io-failed";
    }

    var killSucceeded = TryKill(proc);
    AfterTerminationRequest.Value?.Invoke();
    // Readers keep running through the settle wait: the proof is EOF on both pipes after the child exited.
    await Task.WhenAny(completion, Task.Delay(250)).ConfigureAwait(false);
    // Observe every reader/exit failure even if an OS pipe cannot settle inside the cleanup budget.
    _ = completion.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
      TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    var evidence = JudgeCleanup(killSucceeded, proc, readOutTask, readErrTask);
    if (evidence == "confirmed")
    {
      Release(proc, readerCts);
      return Result(true, failure) with { CleanupEvidence = evidence };
    }
    if (retainUnconfirmed)
      // The caller owns the child: the process stays undisposed and the readers keep running to EOF.
      return Result(true, "cleanup-unconfirmed", confirmed: false) with
      {
        CleanupEvidence = evidence,
        PendingCleanup = UnconfirmedChild.FromProcess(proc, readOutTask, readErrTask, readerCts, evidence),
      };
    readerCts.Cancel();
    Release(proc, readerCts);
    return Result(true, "cleanup-unconfirmed", confirmed: false) with { CleanupEvidence = evidence };
  }

  // Proof = the child exited and both redirected pipes reached EOF (PWR-INV-CLEANUP-REOPEN). A cancelled
  // or faulted reader can never prove it.
  internal static string JudgeCleanup(bool killSucceeded, Process proc, Task<PipeReadOutcome> stdout, Task<PipeReadOutcome> stderr)
  {
    if (!killSucceeded) return "kill-failed";
    if (!proc.HasExited) return "not-exited";
    if (stdout.IsFaulted || stdout.IsCanceled || stderr.IsFaulted || stderr.IsCanceled) return "reader-failed";
    return stdout.IsCompletedSuccessfully && stderr.IsCompletedSuccessfully && stdout.Result.ReachedEof && stderr.Result.ReachedEof
      ? "confirmed" : "pipes-open";
  }

  private static void Release(Process proc, CancellationTokenSource readerCts)
  {
    proc.Dispose();
    readerCts.Dispose();
  }

  private static bool TryKill(Process proc)
  {
    try
    {
      if (!proc.HasExited) proc.Kill(entireProcessTree: true);
      return true;
    }
    catch { return false; }
  }

  private static Task<PipeReadOutcome> StartReader(StreamReader reader, TaskCompletionSource<string> failure, CancellationToken token)
    => Task.Factory.StartNew(() => ReadBoundedAsync(reader, failure, token), CancellationToken.None,
      TaskCreationOptions.DenyChildAttach, ReaderScheduler.Value ?? TaskScheduler.Default).Unwrap();

  private static async Task<PipeReadOutcome> ReadBoundedAsync(StreamReader reader,
    TaskCompletionSource<string> failure, CancellationToken cancellationToken)
  {
    // The reader task exclusively owns disposal, including after the caller's cleanup
    // deadline. Disposing concurrently with a blocked synchronous pipe read can block
    // the guardian itself; late tasks retain ownership until their read unwinds.
    using var ownedReader = reader;
    const int limit = 65536;
    var output = new StringBuilder();
    var buffer = new char[2048];
    var overLimit = false;
    try
    {
      while (true)
      {
        var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        if (count == 0) return new(output.ToString(), true);
        // Past the limit, keep reading and discarding with the fixed buffer so EOF stays observable.
        if (overLimit) continue;
        if (count > limit - output.Length)
        {
          overLimit = true;
          failure.TrySetResult("output-limit");
          continue;
        }
        output.Append(buffer, 0, count);
      }
    }
    catch (OperationCanceledException) { throw; }
    catch
    {
      failure.TrySetResult("io-failed");
      throw;
    }
  }

  private static Encoding CreateConsoleEncoding()
  {
    try
    {
      Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
      var codePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
      if (codePage > 0) return Encoding.GetEncoding(codePage);
    }
    catch
    {
      // fall back below
    }

    return Encoding.UTF8;
  }
}

internal sealed record PipeReadOutcome(string Text, bool ReachedEof);
