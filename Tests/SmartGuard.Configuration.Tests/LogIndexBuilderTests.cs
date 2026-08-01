using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogIndexBuilderTests
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
  public void Build_creates_index_from_log_file()
  {
    var path = CreateTempLogFile(
      "[INFO] 2026-08-01 10:00:00 [Engine] AppStarted",
      "[WARN] 2026-08-01 10:01:00 [Engine] RecoveryDegraded op=op-abc",
      "[ERROR] 2026-08-01 10:02:00 [GuardianLoop] IterationError");

    try
    {
      var builder = new LogIndexBuilder();
      var index = builder.Build(path, CancellationToken.None);

      index.Entries.Should().HaveCount(3);
      index.SourcePath.Should().Be(path);
      index.Entries[0].LineNumber.Should().Be(1);
      index.Entries[0].Level.Should().Be("INFO");
      index.Entries[1].Level.Should().Be("WARN");
      index.Entries[2].Level.Should().Be("ERROR");
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
  }

  [Fact]
  public void Build_parses_operation_id_from_line()
  {
    var path = CreateTempLogFile(
      "[WARN] 2026-08-01 10:00:00 [Engine] RecoveryDegraded op=op-abc123");

    try
    {
      var builder = new LogIndexBuilder();
      var index = builder.Build(path, CancellationToken.None);

      index.Entries.Should().HaveCount(1);
      index.Entries[0].OperationId.Should().Be("op-abc123");
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
  }

  [Fact]
  public void Build_parses_trace_id_from_line()
  {
    var path = CreateTempLogFile(
      "[ERROR] 2026-08-01 10:00:00 [GuardianLoop] IterationError op=op-abc trace=trace-xyz");

    try
    {
      var builder = new LogIndexBuilder();
      var index = builder.Build(path, CancellationToken.None);

      index.Entries.Should().HaveCount(1);
      index.Entries[0].TraceId.Should().Be("trace-xyz");
      index.Entries[0].OperationId.Should().Be("op-abc");
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
  }

  [Fact]
  public void Build_returns_empty_for_nonexistent_file()
  {
    var builder = new LogIndexBuilder();
    var index = builder.Build(@"Z:\nonexistent\file.log", CancellationToken.None);

    index.Entries.Should().BeEmpty();
    index.SourcePath.Should().Be(@"Z:\nonexistent\file.log");
  }

  [Fact]
  public void Build_is_cancellable()
  {
    var lines = Enumerable.Range(0, 10000)
      .Select(i => $"[INFO] 2026-08-01 10:00:{i:D4} [Engine] Tick{i}")
      .ToArray();
    var path = CreateTempLogFile(lines);

    try
    {
      using var cts = new CancellationTokenSource();
      cts.Cancel(); // 预先取消

      var builder = new LogIndexBuilder();
      var index = builder.Build(path, cts.Token);

      // 取消后应尽早返回，条目数远小于 10000
      index.Entries.Count.Should().BeLessThan(10000);
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
  }
}
