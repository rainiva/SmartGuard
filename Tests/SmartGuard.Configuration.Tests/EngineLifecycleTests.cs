using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class EngineLifecycleTests
{
  [Fact]
  public void StopForUninstall_source_orders_end_disable_before_process_stop_before_task_delete()
  {
    var source = ReadEngineLifecycleSource();
    var endDisableIndex = source.IndexOf("EndAndDisableScheduledTasks", StringComparison.Ordinal);
    var killProcessIndex = source.IndexOf("StopProcesses", StringComparison.Ordinal);
    var deleteTaskIndex = source.IndexOf("DeleteScheduledTasks", StringComparison.Ordinal);

    endDisableIndex.Should().BeGreaterThan(-1);
    killProcessIndex.Should().BeGreaterThan(-1);
    deleteTaskIndex.Should().BeGreaterThan(-1);
    endDisableIndex.Should().BeLessThan(killProcessIndex);
    killProcessIndex.Should().BeLessThan(deleteTaskIndex);
  }

  [Fact]
  public void StopForUninstall_source_orders_process_stop_before_task_delete()
  {
    var source = ReadEngineLifecycleSource();
    var killProcessIndex = source.IndexOf("StopProcesses", StringComparison.Ordinal);
    var deleteTaskIndex = source.IndexOf("DeleteScheduledTasks", StringComparison.Ordinal);

    killProcessIndex.Should().BeGreaterThan(-1);
    deleteTaskIndex.Should().BeGreaterThan(-1);
    killProcessIndex.Should().BeLessThan(deleteTaskIndex);
  }

  private static string ReadEngineLifecycleSource()
  {
    var root = Path.GetFullPath(AppContext.BaseDirectory);
    var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
    var path = Path.Combine(projectRoot, "src", "SmartGuard.Configuration", "EngineLifecycle.cs");
    if (!File.Exists(path))
      path = Path.Combine(Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "..")),
        "src", "SmartGuard.Configuration", "EngineLifecycle.cs");
    File.Exists(path).Should().BeTrue();
    return File.ReadAllText(path);
  }
}
