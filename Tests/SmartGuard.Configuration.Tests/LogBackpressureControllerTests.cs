using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogBackpressureControllerTests
{
  [Fact]
  public void ShouldThrottle_returns_false_when_below_threshold()
  {
    var controller = new LogBackpressureController(thresholdPercent: 80);
    controller.ShouldThrottle(normalQueueDepth: 100, normalQueueCapacity: 200).Should().BeFalse();
  }

  [Fact]
  public void ShouldThrottle_returns_true_when_at_threshold()
  {
    var controller = new LogBackpressureController(thresholdPercent: 80);
    controller.ShouldThrottle(normalQueueDepth: 160, normalQueueCapacity: 200).Should().BeTrue();
  }

  [Fact]
  public void ShouldThrottle_returns_true_when_above_threshold()
  {
    var controller = new LogBackpressureController(thresholdPercent: 80);
    controller.ShouldThrottle(normalQueueDepth: 200, normalQueueCapacity: 200).Should().BeTrue();
  }

  [Fact]
  public void ShouldThrottle_handles_zero_capacity()
  {
    var controller = new LogBackpressureController(thresholdPercent: 80);
    controller.ShouldThrottle(normalQueueDepth: 0, normalQueueCapacity: 0).Should().BeFalse();
  }
}
