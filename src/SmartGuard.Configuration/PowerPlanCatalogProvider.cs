using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SmartGuard.Configuration;

public static class PowerPlanCatalogProvider
{
  public static readonly Guid HighPerformancePlanGuid = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
  public static readonly Guid BalancedPlanGuid = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
  public static readonly Guid PowerSaverPlanGuid = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");

  private static readonly Guid[] KnownSchemeGuids =
  [
    HighPerformancePlanGuid,
    BalancedPlanGuid,
    PowerSaverPlanGuid,
  ];

  public static IReadOnlyDictionary<Guid, string> TryLoad()
  {
    try
    {
      var catalog = PowerPlanCatalogParser.ParseList(RunPowerCfg("/list"));
      EnrichWithHiddenSchemes(catalog, TryQueryPlanName);
      return catalog;
    }
    catch
    {
      return new Dictionary<Guid, string>();
    }
  }

  public static void EnrichWithHiddenSchemes(
    IDictionary<Guid, string> catalog,
    Func<Guid, string?> queryPlanName)
  {
    foreach (var guid in KnownSchemeGuids)
    {
      if (catalog.ContainsKey(guid))
        continue;

      var name = queryPlanName(guid);
      if (!string.IsNullOrWhiteSpace(name))
        catalog[guid] = name!;
    }
  }

  private static string? TryQueryPlanName(Guid guid)
  {
    var output = RunPowerCfg($"/query {guid:D}");
    if (!PowerPlanCatalogParser.TryParseQueryHeader(output, out var parsedGuid, out var name))
      return null;

    return parsedGuid == guid ? name : null;
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
