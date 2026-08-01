using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class HeartLogSamplerTests
{
  private static readonly DateTime T0 = new(2026, 8, 1, 10, 0, 0);

  [Fact]
  public void First_heart_is_always_persisted()
  {
    var sampler = new HeartLogSampler();
    sampler.ShouldPersist(T0, TimeSpan.FromSeconds(60)).Should().BeTrue();
  }

  [Fact]
  public void Heart_within_min_interval_is_throttled()
  {
    var sampler = new HeartLogSampler();
    sampler.ShouldPersist(T0, TimeSpan.FromSeconds(60));
    sampler.ShouldPersist(T0.AddSeconds(59), TimeSpan.FromSeconds(60)).Should().BeFalse();
  }

  [Fact]
  public void Heart_after_min_interval_is_persisted_again()
  {
    var sampler = new HeartLogSampler();
    sampler.ShouldPersist(T0, TimeSpan.FromSeconds(60));
    sampler.ShouldPersist(T0.AddSeconds(60), TimeSpan.FromSeconds(60)).Should().BeTrue();
  }

  [Fact]
  public void Throttled_heart_does_not_reset_interval_window()
  {
    var sampler = new HeartLogSampler();
    sampler.ShouldPersist(T0, TimeSpan.FromSeconds(60));
    sampler.ShouldPersist(T0.AddSeconds(30), TimeSpan.FromSeconds(60)).Should().BeFalse();
    sampler.ShouldPersist(T0.AddSeconds(60), TimeSpan.FromSeconds(60)).Should().BeTrue(
      "被节流的心跳不得重置窗口起点，否则高频心跳会永久饥饿");
  }

  [Fact]
  public void Zero_interval_disables_throttling()
  {
    var sampler = new HeartLogSampler();
    sampler.ShouldPersist(T0, TimeSpan.Zero).Should().BeTrue();
    sampler.ShouldPersist(T0, TimeSpan.Zero).Should().BeTrue();
  }
}
