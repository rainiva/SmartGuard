namespace SmartGuard.Configuration.Tests;

public class ScheduledTaskRegistrarTests
{
  [Fact]
  public void TaskNames_include_guardian_and_tray()
  {
    ScheduledTaskRegistrar.TaskNames.Should().Contain("SmartGuard Guardian");
    ScheduledTaskRegistrar.TaskNames.Should().Contain("SmartGuard Tray");
  }

  [Fact]
  public void BuildGuardianLaunchSpec_uses_engine_exe_when_present()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-registrar-" + Guid.NewGuid().ToString("N"));
    var bin = Path.Combine(root, "bin");
    Directory.CreateDirectory(bin);
    var engine = Path.Combine(bin, "SmartGuard.Engine.exe");
    File.WriteAllText(engine, string.Empty);

    try
    {
      var spec = ScheduledTaskRegistrar.BuildGuardianLaunchSpec(root);
      spec.ExecutePath.Should().Be(engine);
      spec.Arguments.Should().Be($"--root \"{root}\"");
      spec.WorkingDirectory.Should().Be(root);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void BuildGuardianLaunchSpec_falls_back_to_ps_core_when_engine_missing()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-registrar-" + Guid.NewGuid().ToString("N"));
    var lib = Path.Combine(root, "lib");
    Directory.CreateDirectory(lib);
    var core = Path.Combine(lib, "SmartGuard.Core.ps1");
    File.WriteAllText(core, string.Empty);

    try
    {
      var spec = ScheduledTaskRegistrar.BuildGuardianLaunchSpec(root);
      spec.ExecutePath.Should().Be("powershell.exe");
      spec.Arguments.Should().Contain(core);
      spec.Arguments.Should().Contain("-WindowStyle Hidden");
      spec.WorkingDirectory.Should().Be(root);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void BuildTrayLaunchSpec_uses_tray_exe_when_present()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-registrar-" + Guid.NewGuid().ToString("N"));
    var bin = Path.Combine(root, "bin");
    Directory.CreateDirectory(bin);
    var tray = Path.Combine(bin, "SmartGuard.Tray.exe");
    File.WriteAllText(tray, string.Empty);

    try
    {
      var spec = ScheduledTaskRegistrar.BuildTrayLaunchSpec(root);
      spec.ExecutePath.Should().Be(tray);
      spec.Arguments.Should().Be($"--root \"{root}\"");
      spec.WorkingDirectory.Should().Be(root);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void BuildTrayLaunchSpec_falls_back_to_ps_tray_when_exe_missing()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-registrar-" + Guid.NewGuid().ToString("N"));
    var lib = Path.Combine(root, "lib");
    Directory.CreateDirectory(lib);
    var tray = Path.Combine(lib, "SmartGuard.Tray.ps1");
    File.WriteAllText(tray, string.Empty);

    try
    {
      var spec = ScheduledTaskRegistrar.BuildTrayLaunchSpec(root);
      spec.ExecutePath.Should().Be("powershell.exe");
      spec.Arguments.Should().Contain(tray);
      spec.Arguments.Should().Contain("-Sta");
      spec.WorkingDirectory.Should().Be(root);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void BuildTaskXml_guardian_uses_highest_run_level_and_restart_on_failure()
  {
    var root = @"D:\SmartGuard";
    var spec = new ScheduledTaskLaunchSpec(
      Path.Combine(root, "bin", "SmartGuard.Engine.exe"),
      $"--root \"{root}\"",
      root);

    var xml = ScheduledTaskRegistrar.BuildTaskXml(ScheduledTaskRegistrar.GuardianTaskName, spec, ScheduledTaskRunLevel.Highest);

    xml.Should().Contain("<LogonTrigger>");
    xml.Should().Contain("<RunLevel>HighestAvailable</RunLevel>");
    xml.Should().Contain("<RestartOnFailure>");
    xml.Should().Contain("<Interval>PT1M</Interval>");
    xml.Should().Contain("<Count>999</Count>");
    xml.Should().Contain("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
    xml.Should().Contain("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
    xml.Should().Contain("<StartWhenAvailable>true</StartWhenAvailable>");
    xml.Should().Contain("--root");
    xml.Should().Contain("D:\\SmartGuard");
  }

  [Fact]
  public void BuildTaskXml_tray_uses_limited_run_level()
  {
    var root = @"D:\SmartGuard";
    var spec = new ScheduledTaskLaunchSpec(
      Path.Combine(root, "bin", "SmartGuard.Tray.exe"),
      $"--root \"{root}\"",
      root);

    var xml = ScheduledTaskRegistrar.BuildTaskXml(ScheduledTaskRegistrar.TrayTaskName, spec, ScheduledTaskRunLevel.Limited);

    xml.Should().Contain("<RunLevel>LeastPrivilege</RunLevel>");
    xml.Should().NotContain("<RunLevel>HighestAvailable</RunLevel>");
  }
}
