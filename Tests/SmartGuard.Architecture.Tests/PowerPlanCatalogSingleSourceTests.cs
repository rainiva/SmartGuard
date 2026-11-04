using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class PowerPlanCatalogSingleSourceTests
{
  [Fact]
  public void Engine_must_not_call_PowerCfgExecutor_LoadPowerPlanCatalog()
  {
    var engineDir = Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Engine");
    foreach (var file in Directory.EnumerateFiles(engineDir, "*.cs", SearchOption.AllDirectories))
    {
      var relative = Path.GetRelativePath(SourceScanHelper.RepoRoot, file).Replace('\\', '/');
      var source = File.ReadAllText(file);
      source.Should().NotContain(
        "PowerCfgExecutor.LoadPowerPlanCatalog",
        $"catalog loads must use PowerPlanCatalogProvider in {relative}");
    }
  }

  [Fact]
  public void Guardian_iteration_must_load_catalog_via_PowerPlanCatalogProvider()
  {
    var sources = SourceScanHelper.EnumerateCsFilesUnder("src/SmartGuard.Engine/Worker")
      .Where(path => Path.GetFileName(path).StartsWith("GuardianIterationRunner", StringComparison.Ordinal))
      .Select(path => File.ReadAllText(path, System.Text.Encoding.UTF8));
    var source = string.Concat(sources);
    source.Should().Contain("PowerPlanCatalogProvider.TryLoadOnceAsync(");
    source.Should().NotContain("PowerPlanCatalogProvider.TryLoad()");
  }
}
