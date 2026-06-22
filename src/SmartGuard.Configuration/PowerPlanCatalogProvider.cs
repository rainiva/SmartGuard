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

  private static IReadOnlyDictionary<Guid, string>? _sessionCache;

  internal static Func<IReadOnlyDictionary<Guid, string>>? LoadImplementationForTests;

  public static IReadOnlyDictionary<Guid, string> TryLoad()
  {
    if (_sessionCache is not null)
      return _sessionCache;

    try
    {
      var catalog = LoadImplementationForTests?.Invoke() ?? LoadCore();
      if (catalog.Count > 0)
        _sessionCache = catalog;
      return catalog;
    }
    catch
    {
      return new Dictionary<Guid, string>();
    }
  }

  public static Task<IReadOnlyDictionary<Guid, string>> LoadAsync(CancellationToken cancellationToken = default)
    => Task.Run(TryLoad, cancellationToken);

  public static void InvalidateSessionCache() => _sessionCache = null;

  internal static void ClearSessionCacheForTests()
  {
    InvalidateSessionCache();
    LoadImplementationForTests = null;
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

  private static IReadOnlyDictionary<Guid, string> LoadCore()
  {
    var catalog = PowerPlanCatalogParser.ParseList(RunPowerCfg("/list"));
    EnrichWithHiddenSchemes(catalog, TryQueryPlanName);
    return catalog;
  }

  private static string? TryQueryPlanName(Guid guid)
  {
    try
    {
      var output = RunPowerCfg($"/query {guid:D}");
      if (!PowerPlanCatalogParser.TryParseQueryHeader(output, out var parsedGuid, out var name))
        return null;

      return parsedGuid == guid ? name : null;
    }
    catch (TimeoutException)
    {
      return null;
    }
  }

  private static string RunPowerCfg(string arguments)
    => PowerCfgProcessRunner.RunPowerCfg(arguments);
}
