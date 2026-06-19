using System.Diagnostics;
using SmartGuard.Configuration;
using SmartGuard.Engine.Cli;

namespace SmartGuard.Engine.Tests;

public class InstallCommandsTests
{
  [Fact]
  public void ScheduledTaskNames_delegate_to_registrar()
  {
    InstallPaths.ScheduledTaskNames.Should().BeEquivalentTo(ScheduledTaskRegistrar.TaskNames);
  }

  [Fact]
  public void GetEngineExe_points_to_bin_engine()
  {
    var root = @"D:\Project\SmartGuard";
    InstallPaths.GetEngineExe(root)
      .Should().Be(Path.Combine(root, "bin", "SmartGuard.Engine.exe"));
  }

  [Fact]
  public void Uninstall_code_kills_processes_before_deleting_tasks()
  {
    // Verify that RunUninstall stops processes BEFORE deleting scheduled tasks.
    // This is verified by inspecting the source code of InstallCommands.cs:
    // TryKillProcess must be called before TryDeleteScheduledTask.
    //
    // Root cause of uninstall residue:
    // If scheduled tasks are deleted first while processes are still running,
    // the task's RestartOnFailure setting (1-minute retry, 999 count) may
    // revive the process before file deletion completes, causing:
    // 1. Process still running after uninstall
    // 2. Files locked and cannot be deleted
    // 3. "Some content could not be removed" message

    var root = Path.GetFullPath(AppContext.BaseDirectory);
    // AppContext.BaseDirectory = Tests\SmartGuard.Engine.Tests\bin\Debug\net8.0\
    // Need to go up 4 levels to reach project root
    var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
    var sourcePath = Path.Combine(projectRoot, "src", "SmartGuard.Engine", "Cli", "InstallCommands.cs");
    if (!File.Exists(sourcePath))
    {
      // Fallback: try 3 levels up (if running from different output path)
      projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", ".."));
      sourcePath = Path.Combine(projectRoot, "src", "SmartGuard.Engine", "Cli", "InstallCommands.cs");
    }
    if (!File.Exists(sourcePath))
    {
      // Final fallback: try 5 levels up
      projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", ".."));
      sourcePath = Path.Combine(projectRoot, "src", "SmartGuard.Engine", "Cli", "InstallCommands.cs");
    }

    File.Exists(sourcePath).Should().BeTrue($"Source file must exist at {sourcePath}");

    var source = File.ReadAllText(sourcePath);

    // Find positions of key method calls in RunUninstall
    var killProcessIndex = source.IndexOf("TryKillProcess", StringComparison.Ordinal);
    var deleteTaskIndex = source.IndexOf("TryDeleteScheduledTask", StringComparison.Ordinal);

    // Both methods must exist in the source
    killProcessIndex.Should().BeGreaterThan(-1,
      "RunUninstall must call TryKillProcess to stop running processes. " +
      "Without killing processes first, RestartOnFailure may revive them.");

    deleteTaskIndex.Should().BeGreaterThan(-1,
      "RunUninstall must call TryDeleteScheduledTask to remove scheduled tasks.");

    // TryKillProcess must appear BEFORE TryDeleteScheduledTask in the source code
    killProcessIndex.Should().BeLessThan(deleteTaskIndex,
      "TryKillProcess must be called BEFORE TryDeleteScheduledTask in RunUninstall. " +
      "If tasks are deleted first while processes are running, RestartOnFailure " +
      "(Interval=PT1M, Count=999) will restart the process before file deletion, " +
      "causing process residue and file lock.");
  }
}

public class ElevationDeclinedMarkerTests
{
  [Fact]
  public void Exists_returns_false_when_marker_missing()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-marker-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      ElevationDeclinedMarker.Exists(root).Should().BeFalse();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void Exists_returns_true_after_Create()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-marker-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      ElevationDeclinedMarker.Exists(root).Should().BeFalse();
      ElevationDeclinedMarker.Create(root);
      ElevationDeclinedMarker.Exists(root).Should().BeTrue();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void Create_is_idempotent()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-marker-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      ElevationDeclinedMarker.Create(root);
      ElevationDeclinedMarker.Create(root);
      ElevationDeclinedMarker.Exists(root).Should().BeTrue();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void Exists_returns_false_for_invalid_path()
  {
    ElevationDeclinedMarker.Exists(@"Z:\NonExistent\Path\12345").Should().BeFalse();
  }
}
