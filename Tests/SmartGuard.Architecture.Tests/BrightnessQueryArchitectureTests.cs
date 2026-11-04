using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class BrightnessQueryArchitectureTests
{
  [Fact]
  public void Optional_brightness_queries_must_have_finite_enumeration_budgets()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Infrastructure/BrightnessService.cs");
    source.Should().Contain("Timeout = WmiTimeout",
      "optional brightness must not block policy evaluation with the infinite WMI default");
    source.Should().Contain("ReturnImmediately = true");
  }
}
