using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class StatusReporterTests
{
  [Fact]
  public void DefaultStartupLogTailLineCount_is_eight()
  {
    StatusReporter.DefaultStartupLogTailLineCount.Should().Be(8);
  }

  [Fact]
  public void ReadStartupLogTail_returns_last_lines()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-status-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var logPath = SmartGuardPaths.StartupLogFile(root);
    try
    {
      File.WriteAllText(logPath, string.Join(Environment.NewLine, Enumerable.Range(1, 10).Select(i => $"entry-{i:D2}")));
      var tail = StatusReporter.ReadStartupLogTail(root);
      tail.Should().Contain("entry-10");
      tail.Should().NotContain("entry-01");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }
}
