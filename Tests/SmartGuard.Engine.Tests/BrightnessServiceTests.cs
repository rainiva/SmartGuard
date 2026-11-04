using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class BrightnessServiceTests
{
  [Fact]
  public async Task Unresponsive_brightness_provider_returns_unavailable_without_stalling_guardian()
  {
    // Exercise the real WMI boundary: runner tests injecting -1 cannot expose
    // a provider that holds the entire policy loop for minutes on real hardware.
    var result = await Task.Run(() => new BrightnessService().GetBrightness())
      .WaitAsync(TimeSpan.FromSeconds(3));

    result.Should().BeInRange(-1, 100);
  }
}
