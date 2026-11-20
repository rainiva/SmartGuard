using System.Reflection;
using SmartGuard.Configuration;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

[Collection("LoggingFallbackTests")]
public sealed class GuardianLoopStartupInitializationTests
{
  [Fact]
  public void Startup_initialization_updates_the_capability_the_runner_reads()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardLoopStartup_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var marker = SmartGuardPaths.InitMarkerFile(root);
      // An existing install: only the read-only capability probe may run, never an alignment write.
      File.WriteAllText(marker, "2026-11-19T00:00:00");
      var loop = new GuardianLoop(root, SmartGuardPaths.ConfigFile(root), Path.Combine(root, "status.json"),
        marker, Path.Combine(root, "startup.log"), powerCfgBrightnessSupported: true);
      var startup = typeof(GuardianLoop).GetMethod("RunStartupInitialization", BindingFlags.NonPublic | BindingFlags.Instance);
      startup.Should().NotBeNull("RunAsync needs one startup entry for the capability probe");

      var native = new FakePowrProfNative { ReadValue = (_, _, _) => (2, 0) };
      PowerSchemeTestScope.WithNativeSync(native, () => startup!.Invoke(loop, [GuardConfig.CreateDefault(root)]));

      typeof(GuardianLoop).GetField("_powerCfgBrightnessSupported", BindingFlags.NonPublic | BindingFlags.Instance)!
        .GetValue(loop).Should().Be(false, "a confirmed absence switches the guardian to WMI-only mode");
      native.Calls.Should().NotContain(call => call.StartsWith("write-value", StringComparison.Ordinal));
    }
    finally { Directory.Delete(root, true); }
  }
}
