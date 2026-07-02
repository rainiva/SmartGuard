using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SmartGuardPathsSingleSourceArchitectureTests
{
  [Fact]
  public void InstallCommands_must_not_hardcode_startup_log_file_name()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Cli/InstallCommands.cs");
    source.Should().NotContain(
      "\"SmartGuard.startup.log\"",
      "use SmartGuardPaths.StartupLogFile or StartupLogFileName");
    source.Should().Contain("SmartGuardPaths.StartupLogFile");
  }

  [Fact]
  public void ScheduledTaskRegistrar_must_use_SmartGuardPaths_for_exe_paths()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/ScheduledTaskRegistrar.cs");
    source.Should().NotContain(
      "Path.Combine(root, \"bin\", \"SmartGuard.Engine.exe\")",
      "use SmartGuardPaths.EngineExe");
    source.Should().NotContain(
      "Path.Combine(root, \"bin\", \"SmartGuard.Tray.exe\")",
      "use SmartGuardPaths.TrayExe");
    source.Should().Contain("SmartGuardPaths.EngineExe");
    source.Should().Contain("SmartGuardPaths.TrayExe");
  }

  [Fact]
  public void ToastShortcutResolver_must_use_SmartGuardPaths_for_tray_exe()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Tray/Toast/ToastShortcutResolver.cs");
    source.Should().NotContain(
      "Path.Combine(root, \"bin\", \"SmartGuard.Tray.exe\")",
      "use SmartGuardPaths.TrayExe");
    source.Should().Contain("SmartGuardPaths.TrayExe");
  }

  [Fact]
  public void EngineLifecycle_process_image_names_must_derive_from_SmartGuardPaths()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/EngineLifecycle.cs");
    source.Should().NotContain("\"SmartGuard.Tray.exe\"", "process names must come from SmartGuardPaths");
    source.Should().NotContain("\"SmartGuard.Engine.exe\"", "process names must come from SmartGuardPaths");
    source.Should().NotContain("\"SmartGuard.LogViewer.exe\"", "process names must come from SmartGuardPaths");
    source.Should().NotContain("\"SmartGuard.Settings.exe\"", "process names must come from SmartGuardPaths");
    source.Should().Contain("SmartGuardPaths.ProcessImageNames");
  }
}
