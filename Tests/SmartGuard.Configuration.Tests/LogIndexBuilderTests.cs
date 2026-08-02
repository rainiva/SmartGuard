using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogIndexBuilderTests
{
  private sealed class FakePhysicalLineSource : ILogEventSource, ILogPhysicalLineSource
  {
    private readonly IReadOnlyList<LogPhysicalLine> _lines;

    public FakePhysicalLineSource(IReadOnlyList<LogPhysicalLine> lines) => _lines = lines;

    public Task<LogEventPage> ReadLatestAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadOlderAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadNewerAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public IEnumerable<LogPhysicalLine> EnumeratePhysicalLines(LogReadScope scope, CancellationToken cancellationToken)
    {
    foreach (var line in _lines)
    {
      if (cancellationToken.IsCancellationRequested)
        yield break;

      yield return line;
    }
    }

    public async IAsyncEnumerable<LogEvent> StreamAsync(
      LogReadScope scope,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
      foreach (var line in EnumeratePhysicalLines(scope, cancellationToken))
        yield return LogEventDecoder.Decode(line);
    }

    public async IAsyncEnumerable<LogPhysicalLine> StreamPhysicalLinesAsync(
      LogReadScope scope,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
      foreach (var line in EnumeratePhysicalLines(scope, cancellationToken))
        yield return line;
    }
  }

  private static LogSourceIdentity TestSource()
    => LogSourceIdentity.FromContentPrefix(
      new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
      "index-test-prefix");

  private static LogPhysicalLine LegacyLine(long offset, string text, int ordinal = 0)
    => new(TestSource(), offset, text, true, ordinal);

  [Fact]
  public void Build_creates_index_from_event_source()
  {
    var source = new FakePhysicalLineSource([
      LegacyLine(0, "[INFO] 2026-08-01 10:00:00 [Engine] AppStarted"),
      LegacyLine(64, "[WARN] 2026-08-01 10:01:00 [Engine] RecoveryDegraded op=op-abc"),
      LegacyLine(128, "[ERROR] 2026-08-01 10:02:00 [GuardianLoop] IterationError"),
    ]);

    var builder = new LogIndexBuilder();
    var index = builder.Build(source, "test.log", CancellationToken.None);

    index.Entries.Should().HaveCount(3);
    index.SourcePath.Should().Be("test.log");
    index.Entries[0].ByteOffset.Should().Be(0);
    index.Entries[0].Level.Should().Be("INFO");
    index.Entries[1].Level.Should().Be("WARN");
    index.Entries[2].Level.Should().Be("ERROR");
    index.Entries[0].EventId.Should().StartWith("l:");
  }

  [Fact]
  public void Build_parses_operation_id_from_line()
  {
    var source = new FakePhysicalLineSource([
      LegacyLine(0, "[WARN] 2026-08-01 10:00:00 [Engine] RecoveryDegraded op=op-abc123"),
    ]);

    var builder = new LogIndexBuilder();
    var index = builder.Build(source, "test.log", CancellationToken.None);

    index.Entries.Should().HaveCount(1);
    index.Entries[0].OperationId.Should().Be("op-abc123");
  }

  [Fact]
  public void Build_parses_trace_id_from_line()
  {
    var source = new FakePhysicalLineSource([
      LegacyLine(0, "[ERROR] 2026-08-01 10:00:00 [GuardianLoop] IterationError op=op-abc trace=trace-xyz"),
    ]);

    var builder = new LogIndexBuilder();
    var index = builder.Build(source, "test.log", CancellationToken.None);

    index.Entries.Should().HaveCount(1);
    index.Entries[0].TraceId.Should().Be("trace-xyz");
    index.Entries[0].OperationId.Should().Be("op-abc");
  }

  [Fact]
  public void Build_returns_empty_for_empty_source()
  {
    var source = new FakePhysicalLineSource(Array.Empty<LogPhysicalLine>());
    var builder = new LogIndexBuilder();
    var index = builder.Build(source, "missing.log", CancellationToken.None);

    index.Entries.Should().BeEmpty();
    index.SourcePath.Should().Be("missing.log");
  }

  [Fact]
  public void Build_is_cancellable()
  {
    var lines = Enumerable.Range(0, 10000)
      .Select(i => LegacyLine(i * 64L, $"[INFO] 2026-08-01 10:00:{i % 60:D2} [Engine] Tick{i}"))
      .ToArray();
    var source = new FakePhysicalLineSource(lines);

    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var builder = new LogIndexBuilder();
    var index = builder.Build(source, "test.log", cts.Token);

    index.Entries.Should().BeEmpty();
  }
}
