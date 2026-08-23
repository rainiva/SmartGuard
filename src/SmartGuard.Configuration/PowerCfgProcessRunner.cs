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
    catch (Exception ex)
    {
      return new PowerCfgCommandResult(false, false, null, string.Empty, ex.Message);
    }

    if (proc is null)
      return new PowerCfgCommandResult(false, false, null, string.Empty, "Process.Start returned null.");

    using var process = proc;
    using var timeoutCts = new CancellationTokenSource(timeout);
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
      timeoutCts.Token,
      cancellationToken);

    var readOutTask = proc.StandardOutput.ReadToEndAsync(linkedCts.Token);
    var readErrTask = proc.StandardError.ReadToEndAsync(linkedCts.Token);

    try
    {
      await proc.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
      var output = await readOutTask.ConfigureAwait(false);
      var error = await readErrTask.ConfigureAwait(false);
      return new PowerCfgCommandResult(true, false, proc.ExitCode, output, error);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      TryKill(proc);
      return new PowerCfgCommandResult(true, false, null, string.Empty, "Process execution was cancelled.");
    }
    catch (OperationCanceledException)
    {
      TryKill(proc);
      return new PowerCfgCommandResult(
        true,
        true,
        null,
        string.Empty,
        $"{fileName} {arguments} timed out after {timeout.TotalSeconds}s");
    }
    catch (Exception ex)
    {
      TryKill(proc);
      return new PowerCfgCommandResult(true, false, null, string.Empty, ex.Message);
    }
  }

  private static void TryKill(Process proc)
  {
    try { proc.Kill(entireProcessTree: true); } catch { }
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
