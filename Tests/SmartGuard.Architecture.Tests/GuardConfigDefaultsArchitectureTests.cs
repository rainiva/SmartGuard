using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class GuardConfigDefaultsArchitectureTests
{
  [Fact]
  public void CreateDefault_must_delegate_plan_guids_and_log_path_to_single_sources()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardConfig.cs");
    source.Should().Contain("PowerPlanCatalogProvider.HighPerformancePlanGuid");
    source.Should().Contain("PowerPlanCatalogProvider.BalancedPlanGuid");
    source.Should().Contain("PowerPlanCatalogProvider.PowerSaverPlanGuid");
    source.Should().Contain("SmartGuardPaths.DefaultLogFile(root)");
    source.Should().NotContain("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    source.Should().NotContain(@"Path.Combine(root, ""SmartGuard.log"")");
  }
}
