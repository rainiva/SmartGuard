using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class SpanIdContextTests
{
  [Fact]
  public void Current_returns_null_when_not_set()
  {
    SpanIdContext.Clear();
    SpanIdContext.Current.Should().BeNull();
  }

  [Fact]
  public void Set_and_Clear_round_trip()
  {
    SpanIdContext.Set("span-xyz789");
    SpanIdContext.Current.Should().Be("span-xyz789");
    SpanIdContext.Clear();
    SpanIdContext.Current.Should().BeNull();
  }

  [Fact]
  public void GenerateNew_sets_current_and_returns_value()
  {
    var id = SpanIdContext.GenerateNew();
    id.Should().StartWith("span-");
    SpanIdContext.Current.Should().Be(id);
    SpanIdContext.Clear();
  }

  [Fact]
  public async Task AsyncLocal_flows_across_async_boundary()
  {
    SpanIdContext.Set("span-flow-test");
    var captured = await Task.Run(() => SpanIdContext.Current);
    captured.Should().Be("span-flow-test");
    SpanIdContext.Clear();
  }
}
