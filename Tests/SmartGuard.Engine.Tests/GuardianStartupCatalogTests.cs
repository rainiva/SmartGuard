namespace SmartGuard.Engine.Tests;

public class GuardianStartupCatalogTests
{
    [Fact]
    public void RunAsync_does_not_preload_plan_catalog_before_startup_log()
    {
        var loopSource = File.ReadAllText(GuardianLoopSourcePath());
        loopSource.Should().NotContain("PowerPlanCatalogProvider.TryLoad()",
            "plan catalog should not load during GuardianLoop startup");

        var runnerSource = File.ReadAllText(GuardianIterationRunnerSourcePath());
        runnerSource.Should().NotContain("state.PlanCatalog = PowerPlanCatalogProvider.TryLoad();",
            "plan catalog should load lazily on the first iteration to shorten cold start");
        runnerSource.Should().Contain("state.PlanCatalog ?? PowerPlanCatalogProvider.TryLoad()",
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

    private static string GuardianIterationRunnerSourcePath()
    {
        var assemblyLocation = typeof(GuardianStartupCatalogTests).Assembly.Location;
        var repoRoot = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(assemblyLocation)!,
            "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "SmartGuard.Engine", "Worker", "GuardianIterationRunner.cs");
    }
}
