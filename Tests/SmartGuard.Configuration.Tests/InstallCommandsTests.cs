using SmartGuard.Configuration;
using SmartGuard.Configuration.Cli;

namespace SmartGuard.Configuration.Tests;

public class InstallCommandsTests
{
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
    script.Should().NotContain("PrivilegesRequired=lowest");
    (script.Contains("PrivilegesRequired=admin") || !script.Contains("PrivilegesRequired="))
      .Should().BeTrue();
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
