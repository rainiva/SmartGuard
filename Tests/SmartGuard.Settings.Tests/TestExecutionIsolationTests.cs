namespace SmartGuard.Settings.Tests;

public class TestExecutionIsolationTests
{
  [Fact]
  public void Runner_disables_parallel_test_collections_for_shared_wpf_state()
  {
    var runnerConfigPath = Path.Combine(AppContext.BaseDirectory, "xunit.runner.json");

    File.Exists(runnerConfigPath).Should().BeTrue();
    File.ReadAllText(runnerConfigPath).Should().Contain("\"parallelizeTestCollections\": false");
  }
}
