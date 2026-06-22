using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SmartGuard.Configuration;

internal static class PowerCfgProcessRunner
{
  private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
  private static readonly Encoding ConsoleEncoding = CreateConsoleEncoding();

  internal static Func<string, TimeSpan, string>? RunOverrideForTests;

  internal static string RunPowerCfg(string arguments, TimeSpan? timeout = null)
  {
    if (RunOverrideForTests is not null)
      return RunOverrideForTests(arguments, timeout ?? DefaultTimeout);

    return RunProcess("powercfg.exe", arguments, timeout ?? DefaultTimeout);
  }

  internal static string RunProcess(string fileName, string arguments, TimeSpan timeout)
  {
    return RunProcessAsync(fileName, arguments, timeout).GetAwaiter().GetResult();
  }

  private static async Task<string> RunProcessAsync(string fileName, string arguments, TimeSpan timeout)
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

    using var proc = Process.Start(psi);
    if (proc is null)
      return string.Empty;

    using var cts = new CancellationTokenSource(timeout);
    var readOutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
    var readErrTask = proc.StandardError.ReadToEndAsync(cts.Token);

    try
    {
      await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      try { proc.Kill(entireProcessTree: true); } catch { }
      throw new TimeoutException($"{fileName} {arguments} timed out after {timeout.TotalSeconds}s");
    }

    var output = await readOutTask.ConfigureAwait(false);
    var error = await readErrTask.ConfigureAwait(false);
    return output + error;
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
