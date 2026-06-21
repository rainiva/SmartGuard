using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SmartGuard.Configuration;

public static class PowerPlanCatalogProvider
{
  public static IReadOnlyDictionary<Guid, string> TryLoad()
  {
    try
    {
      var output = RunPowerCfg("/list");
      return PowerPlanCatalogParser.ParseList(output);
    }
    catch
    {
      return new Dictionary<Guid, string>();
    }
  }

  private static string RunPowerCfg(string arguments)
  {
    var encoding = CreateConsoleEncoding();
    var psi = new ProcessStartInfo("powercfg.exe", arguments)
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = encoding,
      StandardErrorEncoding = encoding,
      UseShellExecute = false,
      CreateNoWindow = true,
    };

    using var proc = Process.Start(psi);
    if (proc is null) return string.Empty;

    var output = proc.StandardOutput.ReadToEnd();
    var error = proc.StandardError.ReadToEnd();
    proc.WaitForExit(TimeSpan.FromSeconds(5));
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
