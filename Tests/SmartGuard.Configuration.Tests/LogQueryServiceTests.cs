using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogQueryServiceTests
{
  private static LogIndex CreateTestIndex()
  {
    var entries = new List<LogIndexEntry>
    {
      new(1, new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero), "INFO", "Engine", "AppStarted", null, null, 1234, 5678),
      new(2, new DateTimeOffset(2026, 8, 1, 10, 1, 0, TimeSpan.Zero), "WARN", "Engine", "RecoveryDegraded", "op-abc", null, 1234, 5678),
      new(3, new DateTimeOffset(2026, 8, 1, 10, 2, 0, TimeSpan.Zero), "ERROR", "GuardianLoop", "IterationError", "op-abc", null, 1234, 5678),
      new(4, new DateTimeOffset(2026, 8, 1, 10, 3, 0, TimeSpan.Zero), "INFO", "Engine", "HeartbeatTick", null, null, 1234, 5678),
      new(5, new DateTimeOffset(2026, 8, 1, 10, 4, 0, TimeSpan.Zero), "ERROR", "Engine", "IterationError", "op-def", null, 4321, 8765),
    };
    return new LogIndex(entries, "test.log");
  }

  [Fact]
  public void Query_by_level_returns_matching_entries()
  {
    var service = new LogQueryService();
    var index = CreateTestIndex();
    var request = new LogQueryRequest(null, "ERROR", null, null, null, null, null, null, 1, 50);

    var result = service.Query(index, request, CancellationToken.None);

    result.TotalCount.Should().Be(2);
    result.Matches.Should().HaveCount(2);
    result.Matches.All(e => e.Level == "ERROR").Should().BeTrue();
  }

  [Fact]
  public void Query_by_operation_id_returns_matching_entries()
  {
    var service = new LogQueryService();
    var index = CreateTestIndex();
    var request = new LogQueryRequest(null, null, null, "op-abc", null, null, null, null, 1, 50);

    var result = service.Query(index, request, CancellationToken.None);

    result.TotalCount.Should().Be(2);
    result.Matches.All(e => e.OperationId == "op-abc").Should().BeTrue();
  }

  [Fact]
  public void Query_with_pagination_returns_correct_page()
  {
    var service = new LogQueryService();
    var index = CreateTestIndex();
    var request = new LogQueryRequest(null, null, null, null, null, null, null, null, 1, 2);

    var result = service.Query(index, request, CancellationToken.None);

    result.TotalCount.Should().Be(5);
    result.Matches.Should().HaveCount(2);
    result.Page.Should().Be(1);
    result.PageSize.Should().Be(2);
  }

  [Fact]
  public void Query_by_keyword_matches_message_content()
  {
    var service = new LogQueryService();
    var index = CreateTestIndex();
    var request = new LogQueryRequest("Heartbeat", null, null, null, null, null, null, null, 1, 50);

    var result = service.Query(index, request, CancellationToken.None);

    result.TotalCount.Should().Be(1);
    result.Matches[0].EventName.Should().Be("HeartbeatTick");
  }

  [Fact]
  public void Query_by_process_id_returns_matching_entries()
  {
    var service = new LogQueryService();
    var index = CreateTestIndex();
    var request = new LogQueryRequest(null, null, null, null, null, 4321, null, null, 1, 50);

    var result = service.Query(index, request, CancellationToken.None);

    result.TotalCount.Should().Be(1);
    result.Matches[0].ProcessId.Should().Be(4321);
  }

  [Fact]
  public void Query_with_no_filters_returns_all()
  {
    var service = new LogQueryService();
    var index = CreateTestIndex();
    var request = new LogQueryRequest(null, null, null, null, null, null, null, null, 1, 100);

    var result = service.Query(index, request, CancellationToken.None);

    result.TotalCount.Should().Be(5);
    result.Matches.Should().HaveCount(5);
  }
}
