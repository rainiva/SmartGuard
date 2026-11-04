using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SmartGuard.Configuration;

internal static class PowerCfgProcessRunner
{
  private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
  private static readonly Encoding ConsoleEncoding = CreateConsoleEncoding();

  internal static Func<string, TimeSpan, PowerCfgCommandResult>? RunOverrideForTests;
  internal static Func<string, TimeSpan, CancellationToken, Task<PowerCfgCommandResult>>? RunAsyncOverrideForTests;

  internal static PowerCfgCommandResult RunPowerCfg(string arguments, TimeSpan? timeout = null)
  {
    if (RunOverrideForTests is not null)
      return RunOverrideForTests(arguments, timeout ?? DefaultTimeout);

    return RunProcess("powercfg.exe", arguments, timeout ?? DefaultTimeout);
  }

  internal static Task<PowerCfgCommandResult> RunPowerCfgAsync(
    string arguments,
    CancellationToken cancellationToken = default,
    TimeSpan? timeout = null)
  {
    if (RunAsyncOverrideForTests is not null)
      return RunAsyncOverrideForTests(arguments, timeout ?? DefaultTimeout, cancellationToken);

    if (RunOverrideForTests is not null)
      return Task.FromResult(RunOverrideForTests(arguments, timeout ?? DefaultTimeout));

    return RunProcessAsync(
      "powercfg.exe",
      arguments,
      timeout ?? DefaultTimeout,
      cancellationToken);
  }

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
    CancellationToken cancellationToken = default)
    => RunProcessCoreAsync(fileName, arguments, timeout, cancellationToken);

  private static async Task<PowerCfgCommandResult> RunProcessCoreAsync(
    string fileName,
    string arguments,
    TimeSpan timeout,
    CancellationToken cancellationToken)
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

    using var process = proc;

    var failureSignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    // Anonymous pipe implementations may perform synchronous work before their first await.
    // Capture before scheduling: a late worker must not access an already-disposed Process.
    // Reading the properties transfers stream ownership away from Process.Dispose.
    var standardOutput = proc.StandardOutput;
    var standardError = proc.StandardError;
    var readOutTask = Task.Run(() => ReadBoundedAsync(standardOutput, failureSignal, linkedCts.Token));
    var readErrTask = Task.Run(() => ReadBoundedAsync(standardError, failureSignal, linkedCts.Token));
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
        var output = await readOutTask.ConfigureAwait(false);
        var error = await readErrTask.ConfigureAwait(false);
        return Result(true, "none", exitCode: proc.ExitCode, output: output, error: error);
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

    // Do not equate parent exit with tree cleanup. An exited parent with open pipes cannot
    // safely identify its surviving descendants; the caller must disable further probes.
    var orphanedPipes = proc.HasExited && (!readOutTask.IsCompleted || !readErrTask.IsCompleted);
    var killSucceeded = TryKill(proc);
    linkedCts.Cancel();
    var settled = await Task.WhenAny(completion, Task.Delay(250)).ConfigureAwait(false) == completion;
    // Observe every reader/exit failure even if an OS pipe cannot settle inside the cleanup budget.
    _ = completion.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
      TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    var confirmed = killSucceeded && proc.HasExited && settled && !orphanedPipes;
    return Result(true, failure, confirmed) with { FailureKind = confirmed ? failure : "cleanup-unconfirmed" };
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

  private static async Task<string> ReadBoundedAsync(StreamReader reader,
    TaskCompletionSource<string> failure, CancellationToken cancellationToken)
  {
    // The reader task exclusively owns disposal, including after the caller's cleanup
    // deadline. Disposing concurrently with a blocked synchronous pipe read can block
    // the guardian itself; late tasks retain ownership until their read unwinds.
    using var ownedReader = reader;
    const int limit = 65536;
    var output = new StringBuilder();
    var buffer = new char[2048];
    try
    {
      while (true)
      {
        var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        if (count == 0) return output.ToString();
        if (count > limit - output.Length)
        {
          failure.TrySetResult("output-limit");
          return output.ToString();
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
