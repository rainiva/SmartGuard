using SmartGuard.Engine.Cli;

namespace SmartGuard.Engine.Tests;

public class InstallCommandsTests
{
  [Fact]
  public void GetRegisterScriptPaths_resolve_under_root()
  {
    var root = @"D:\Project\SmartGuard";
    InstallPaths.GetGuardianRegisterScript(root)
      .Should().Be(Path.Combine(root, "Register-SmartGuardTask.ps1"));
    InstallPaths.GetTrayRegisterScript(root)
      .Should().Be(Path.Combine(root, "Register-TrayTask.ps1"));
  }

  [Fact]
  public void BuildPowerShellInvocation_includes_bypass_and_script_path()
  {
    var script = @"D:\Project\SmartGuard\Register-TrayTask.ps1";
    var args = PowerShellInvocation.BuildArguments(script);
    args.Should().Contain("-ExecutionPolicy Bypass");
    args.Should().Contain(script);
  }

  [Fact]
  public void ScheduledTaskNames_include_guardian_and_tray()
  {
    InstallPaths.ScheduledTaskNames.Should().Contain("SmartGuard Guardian");
    InstallPaths.ScheduledTaskNames.Should().Contain("SmartGuard Tray");
  }
}
