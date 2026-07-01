using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class GuardConfigDefaultsSingleSourceTests
{
  [Fact]
  public void CreateDefault_plan_guids_match_PowerPlanCatalogProvider()
  {
    var config = GuardConfig.CreateDefault(@"C:\SmartGuard");
    config.ActivePlanGuid.Should().Be(PowerPlanCatalogProvider.HighPerformancePlanGuid);
    config.BalancedPlanGuid.Should().Be(PowerPlanCatalogProvider.BalancedPlanGuid);
    config.PowerSaverPlanGuid.Should().Be(PowerPlanCatalogProvider.PowerSaverPlanGuid);
  }

  [Fact]
  public void CreateDefault_log_file_uses_SmartGuardPaths_DefaultLogFile()
  {
    const string root = @"C:\SmartGuard";
    GuardConfig.CreateDefault(root).LogFile.Should().Be(SmartGuardPaths.DefaultLogFile(root));
  }
}
