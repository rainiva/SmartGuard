using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class UserActionIdContextTests
{
  [Fact]
  public void Current_returns_null_when_not_set()
  {
    UserActionIdContext.Clear();
    UserActionIdContext.Current.Should().BeNull();
  }

  [Fact]
  public void Set_and_Clear_round_trip()
  {
    UserActionIdContext.Set("ua-click-001");
    UserActionIdContext.Current.Should().Be("ua-click-001");
    UserActionIdContext.Clear();
    UserActionIdContext.Current.Should().BeNull();
  }

  [Fact]
  public void GenerateNew_sets_current_and_returns_value()
  {
    var id = UserActionIdContext.GenerateNew();
    id.Should().StartWith("ua-");
    UserActionIdContext.Current.Should().Be(id);
    UserActionIdContext.Clear();
  }

  [Fact]
  public async Task AsyncLocal_flows_across_async_boundary()
  {
    UserActionIdContext.Set("ua-flow-test");
    var captured = await Task.Run(() => UserActionIdContext.Current);
    captured.Should().Be("ua-flow-test");
    UserActionIdContext.Clear();
  }
}
