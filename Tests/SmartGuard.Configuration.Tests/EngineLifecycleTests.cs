using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class EngineLifecycleTests
{
  [Fact]
  public void StopForUninstall_source_orders_process_stop_before_task_delete()
  {
    var root = Path.GetFullPath(AppContext.BaseDirectory);
    var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
    var path = Path.Combine(projectRoot, "src", "SmartGuard.Configuration", "EngineLifecycle.cs");
    if (!File.Exists(path))
      path = Path.Combine(Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "..")),
        "src", "SmartGuard.Configuration", "EngineLifecycle.cs");

    var source = File.ReadAllText(path);
    source.IndexOf("StopProcesses", StringComparison.Ordinal)
      .Should().BeLessThan(source.IndexOf("DeleteScheduledTasks", StringComparison.Ordinal));
  }
}
