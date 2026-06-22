namespace SmartGuard.Engine.Tests;

public class GuardianStartupCatalogTests
{
    [Fact]
    public void RunAsync_does_not_preload_plan_catalog_before_startup_log()
    {
        var source = File.ReadAllText(GuardianLoopSourcePath());

        source.Should().NotContain("_planCatalog = PowerCfgExecutor.LoadPowerPlanCatalog();",
            "plan catalog should load lazily on the first iteration to shorten cold start");
        source.Should().Contain("_planCatalog ?? PowerCfgExecutor.LoadPowerPlanCatalog()",
            "first iteration must still load the catalog when it is missing");
    }

    private static string GuardianLoopSourcePath()
    {
        var assemblyLocation = typeof(GuardianStartupCatalogTests).Assembly.Location;
        var repoRoot = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(assemblyLocation)!,
            "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "SmartGuard.Engine", "Worker", "GuardianLoop.cs");
    }
}
