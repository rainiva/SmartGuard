namespace SmartGuard.Configuration.Tests;

public class PowerPlanMappingValidatorTests
{
  private static readonly Guid HighPerf = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
  private static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
  private static readonly Guid Saver = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");

  [Fact]
  public void Validate_rejects_duplicate_plan_guids()
  {
    var config = new GuardConfig
    {
      ActivePlanGuid = HighPerf,
      BalancedPlanGuid = HighPerf,
      PowerSaverPlanGuid = Saver,
    };

    PowerPlanMappingValidator.Validate(config)
      .Should().Contain("高性能与平衡不能选择相同电源计划");
  }

  [Fact]
  public void Validate_reports_missing_catalog_entries()
  {
    var config = new GuardConfig
    {
      ActivePlanGuid = HighPerf,
      BalancedPlanGuid = Balanced,
      PowerSaverPlanGuid = Saver,
    };
    var catalog = new Dictionary<Guid, string>
    {
      [HighPerf] = "高性能",
      [Balanced] = "平衡",
    };

    PowerPlanMappingValidator.Validate(config, catalog)
      .Should().Contain("节能计划未在本机找到");
  }

  [Fact]
  public void Validate_accepts_hidden_power_saver_when_catalog_was_enriched()
  {
    var config = new GuardConfig
    {
      ActivePlanGuid = HighPerf,
      BalancedPlanGuid = Balanced,
      PowerSaverPlanGuid = Saver,
    };
    var catalog = new Dictionary<Guid, string>
    {
      [HighPerf] = "高性能",
      [Balanced] = "平衡",
      [Saver] = "节能",
    };

    PowerPlanMappingValidator.Validate(config, catalog).Should().BeEmpty();
  }
}
