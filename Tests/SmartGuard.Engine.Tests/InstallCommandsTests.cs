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
    var root = Path.GetFullPath(AppContext.BaseDirectory);
    var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
    var lifecyclePath = Path.Combine(projectRoot, "src", "SmartGuard.Configuration", "EngineLifecycle.cs");
    if (!File.Exists(lifecyclePath))
    {
      projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", ".."));
      lifecyclePath = Path.Combine(projectRoot, "src", "SmartGuard.Configuration", "EngineLifecycle.cs");
    }

    File.Exists(lifecyclePath).Should().BeTrue($"Source file must exist at {lifecyclePath}");

    var source = File.ReadAllText(lifecyclePath);
    var killProcessIndex = source.IndexOf("StopProcesses", StringComparison.Ordinal);
    var deleteTaskIndex = source.IndexOf("DeleteScheduledTasks", StringComparison.Ordinal);

    killProcessIndex.Should().BeGreaterThan(-1);
    deleteTaskIndex.Should().BeGreaterThan(-1);
    killProcessIndex.Should().BeLessThan(deleteTaskIndex);
  }

  [Fact]
  public void Installer_script_requires_admin_privileges_for_uninstall()
  {
    // Verify that SmartGuard.iss has PrivilegesRequired=admin.
    // Root cause: uninstaller runs taskkill to stop SmartGuard.Engine, which runs
    // with highestAvailable (admin) privileges. If uninstaller runs as lowest,
    // taskkill cannot terminate the elevated process, causing:
    // 1. Process still running after uninstall
    // 2. Files locked by the running process cannot be deleted
    // 3. Uninstall hangs waiting for [UninstallRun] --uninstall to complete (UAC prompt)

    var root = Path.GetFullPath(AppContext.BaseDirectory);
    var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
    var scriptPath = Path.Combine(projectRoot, "installer", "SmartGuard.iss");
    if (!File.Exists(scriptPath))
    {
      projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", ".."));
      scriptPath = Path.Combine(projectRoot, "installer", "SmartGuard.iss");
    }
    if (!File.Exists(scriptPath))
    {
      projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", ".."));
      scriptPath = Path.Combine(projectRoot, "installer", "SmartGuard.iss");
    }

    File.Exists(scriptPath).Should().BeTrue($"Installer script must exist at {scriptPath}");

    var script = File.ReadAllText(scriptPath);

    // Must NOT have PrivilegesRequired=lowest (this was the bug)
    script.Should().NotContain("PrivilegesRequired=lowest",
      "PrivilegesRequired=lowest causes uninstaller to run without admin rights. " +
      "This prevents taskkill from terminating elevated SmartGuard processes, " +
      "causing process residue, file locks, and uninstall hang.");

    // Must have PrivilegesRequired=admin (or not set, which defaults to admin in some cases)
    // Inno Setup: if PrivilegesRequired is not set, it defaults to admin on non-admin install
    // But explicit admin is safer.
    (script.Contains("PrivilegesRequired=admin") || !script.Contains("PrivilegesRequired="))
      .Should().BeTrue(
        "Installer must require admin privileges. SmartGuard.Engine runs with highestAvailable, " +
        "so the uninstaller needs admin rights to terminate it via taskkill.");
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
