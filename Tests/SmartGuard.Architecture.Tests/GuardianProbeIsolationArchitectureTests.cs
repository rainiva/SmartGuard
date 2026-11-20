using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class GuardianProbeIsolationArchitectureTests
{
  [Fact]
  public void Guardian_facing_services_must_not_execute_WMI_inside_the_guardian_process()
  {
    foreach (var file in new[] { "BrightnessService.cs", "BatteryInfoProvider.cs" })
    {
      var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Infrastructure/" + file);
      source.Should().NotContain("ManagementObjectSearcher",
        "a provider COM hang must be confined to the owned probe process");
    }
  }

  [Fact]
  public void Probe_dispatch_must_precede_root_configuration_and_single_instance_initialization()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Program.cs");
    var dispatch = source.IndexOf("WmiProbeCommand.TryRun", StringComparison.Ordinal);
    dispatch.Should().BeGreaterThanOrEqualTo(0);
    dispatch.Should().BeLessThan(source.IndexOf("InstallRootResolver.Resolve", StringComparison.Ordinal));
  }
}
