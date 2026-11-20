using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class TraceIdContextTests
{
  [Fact]
  public void Current_returns_null_when_not_set()
  {
    TraceIdContext.Clear();
    TraceIdContext.Current.Should().BeNull();
  }

  [Fact]
  public void Set_and_Clear_round_trip()
  {
    TraceIdContext.Set("trace-abc123");
    TraceIdContext.Current.Should().Be("trace-abc123");
    TraceIdContext.Clear();
    TraceIdContext.Current.Should().BeNull();
  }

  [Fact]
  public void GenerateNew_sets_current_and_returns_value()
  {
    var id = TraceIdContext.GenerateNew();
    id.Should().StartWith("trace-");
    id.Should().HaveLength(22); // "trace-"(6) + 16 hex chars = 22
    TraceIdContext.Current.Should().Be(id);
    TraceIdContext.Clear();
  }

  [Fact]
  public async Task AsyncLocal_flows_across_async_boundary()
  {
    TraceIdContext.Set("trace-flow-test");
    var captured = await Task.Run(() => TraceIdContext.Current);
    captured.Should().Be("trace-flow-test");
    TraceIdContext.Clear();
  }
}
