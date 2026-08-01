using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

public class LogViewBackgroundQueryTests
{
  private static string CreateTempLogFile(params string[] lines)
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "test.log");
    File.WriteAllLines(path, lines);
    return path;
  }

  [Fact]
  public async Task QueryAsync_returns_filtered_lines_off_ui_thread()
  {
    var path = CreateTempLogFile(
      "[INFO] 2026-08-01 10:00:00 [Engine] AppStarted",
      "[ERROR] 2026-08-01 10:01:00 [Engine] IterationError");

    try
    {
      var controller = new LogViewController(path);
      controller.ForceRefresh();
      controller.SearchKeyword = "Error";

      var query = new LogViewBackgroundQuery();
      var result = await query.QueryAsync(controller, CancellationToken.None);

      result.Should().HaveCount(1);
      result[0].Should().Contain("IterationError");
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
  }

  [Fact]
  public async Task QueryAsync_can_be_cancelled()
  {
    var lines = Enumerable.Range(0, 5000)
      .Select(i => $"[INFO] 2026-08-01 10:00:{i:D4} [Engine] Tick{i}")
      .ToArray();
    var path = CreateTempLogFile(lines);

    try
    {
      var controller = new LogViewController(path);
      controller.ForceRefresh();

      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var query = new LogViewBackgroundQuery();
      var result = await query.QueryAsync(controller, cts.Token);

      result.Should().NotBeNull();
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
  }

  [Fact]
  public async Task QueryAsync_returns_empty_for_no_matches()
  {
    var path = CreateTempLogFile(
      "[INFO] 2026-08-01 10:00:00 [Engine] AppStarted");

    try
    {
      var controller = new LogViewController(path);
      controller.ForceRefresh();
      controller.SearchKeyword = "NonExistentKeyword";

      var query = new LogViewBackgroundQuery();
      var result = await query.QueryAsync(controller, CancellationToken.None);

      result.Should().BeEmpty();
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
  }

  [Fact]
  public async Task QueryAsync_returns_all_when_no_filter_set()
  {
    var path = CreateTempLogFile(
      "[INFO] 2026-08-01 10:00:00 [Engine] AppStarted",
      "[ERROR] 2026-08-01 10:01:00 [Engine] IterationError");

    try
    {
      var controller = new LogViewController(path);
      controller.ForceRefresh();

      var query = new LogViewBackgroundQuery();
      var result = await query.QueryAsync(controller, CancellationToken.None);

      result.Should().HaveCount(2);
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
  }
}
