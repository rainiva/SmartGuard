using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

[Collection("LoggingFallbackTests")]
public sealed class GuardianFirstRunInitializerTests
{
  private static IEnumerable<string> Writes(FakePowrProfNative native)
    => native.Calls.Where(call => call.StartsWith("write-value", StringComparison.Ordinal));

  [Fact]
  public void Capability_probe_is_read_only_and_alignment_writes_only_brightness_settings()
  {
    var native = new FakePowrProfNative();
    var supported = false;
    var run = WithInitializer(native, (initializer, cfg) => initializer.InitializeIfNeeded(cfg, ref supported).Should().BeTrue());
    supported.Should().BeTrue();
    run.MarkerWritten.Should().BeTrue();
    run.Logs.Should().Contain("INIT: 三个电源计划的亮度设置项已对齐为 40%");
    native.Calls.Should().Contain(
      $"read-value {run.Config.BalancedPlanGuid:D} {PowerSchemeService.VideoSubgroupGuid:D} {PowerSchemeService.VideoNormalLevelGuid:D} Ac");
    Writes(native).Should().HaveCount(12);
    Writes(native).Should().OnlyContain(call =>
      (call.Contains(PowerSchemeService.AdaptiveBrightnessGuid.ToString("D")) && call.EndsWith(" 0"))
      || (call.Contains(PowerSchemeService.VideoNormalLevelGuid.ToString("D")) && call.EndsWith(" 40")),
      "the capability probe no longer writes a trial value");
  }

  [Fact]
  public void Confirmed_absent_brightness_setting_selects_wmi_only_mode_without_writes()
  {
    var native = new FakePowrProfNative { ReadValue = (_, _, _) => (2, 0) };
    var supported = true;
    var run = WithInitializer(native, (initializer, cfg) => initializer.InitializeIfNeeded(cfg, ref supported).Should().BeTrue());
    supported.Should().BeFalse();
    run.MarkerWritten.Should().BeTrue();
    Writes(native).Should().BeEmpty();
  }

  [Fact]
  public void Brightness_probe_failure_keeps_the_mode_and_retries_on_the_next_start()
  {
    var native = new FakePowrProfNative { ReadValue = (_, _, _) => (5, 0) };
    var supported = true;
    var run = WithInitializer(native, (initializer, cfg) => initializer.InitializeIfNeeded(cfg, ref supported).Should().BeFalse());
    supported.Should().BeTrue("an unknown capability is not evidence that the setting is missing");
    run.MarkerWritten.Should().BeFalse("the next start must retry the probe");
    run.Logs.Should().Contain(log => log.Contains("探测失败") && log.Contains("电源服务调用失败（错误码 5）"));
    Writes(native).Should().BeEmpty();
  }

  [Fact]
  public void Failed_alignment_write_is_not_reported_as_aligned_and_writes_no_marker()
  {
    var native = new FakePowrProfNative
    {
      WriteValue = (_, setting, _, _) => setting == PowerSchemeService.VideoNormalLevelGuid ? 5u : 0u,
    };
    var supported = false;
    var run = WithInitializer(native, (initializer, cfg) => initializer.InitializeIfNeeded(cfg, ref supported).Should().BeFalse());
    run.MarkerWritten.Should().BeFalse();
    run.Logs.Should().NotContain(log => log.Contains("已对齐"));
    run.Logs.Should().Contain(log => log.Contains("写入失败") && log.Contains("错误码 5"));
    Writes(native).Count(call => call.Contains(PowerSchemeService.VideoNormalLevelGuid.ToString("D")))
      .Should().Be(1, "alignment stops at the first failed write");
  }

  private sealed record InitializerRun(GuardConfig Config, bool MarkerWritten, IReadOnlyList<string> Logs);

  private delegate void InitializerAction(GuardianFirstRunInitializer initializer, GuardConfig config);

  private static InitializerRun WithInitializer(FakePowrProfNative native, InitializerAction run)
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardFirstRun_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var config = GuardConfig.CreateDefault(root);
      var marker = Path.Combine(root, "init");
      var logs = new List<string>();
      var brightness = new BrightnessService(new WmiProbeProcess((_, _, _, _) =>
        Task.FromResult(new PowerCfgCommandResult(true, false, 0, "SGWMI1|brightness|40", ""))));
      var initializer = new GuardianFirstRunInitializer(marker, brightness, (_, _, message) => logs.Add(message));
      // The scope also blocks the legacy powercfg runner, so the RED never writes this machine's settings.
      PowerSchemeTestScope.WithNativeSync(native, () => run(initializer, config));
      return new(config, File.Exists(marker), logs);
    }
    finally { Directory.Delete(root, true); }
  }
}
