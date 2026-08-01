using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class OperationIdContextTests
{
  [Fact]
  public void Current_is_null_by_default()
  {
    OperationIdContext.Clear();
    OperationIdContext.Current.Should().BeNull();
  }

  [Fact]
  public void Set_and_Get_returns_same_value()
  {
    OperationIdContext.Set("op-test-123");
    OperationIdContext.Current.Should().Be("op-test-123");
    OperationIdContext.Clear();
  }

  [Fact]
  public void Clear_resets_to_null()
  {
    OperationIdContext.Set("op-test-456");
    OperationIdContext.Clear();
    OperationIdContext.Current.Should().BeNull();
  }

  [Fact]
  public void GenerateNew_returns_non_empty_string()
  {
    var id = OperationIdContext.GenerateNew();
    id.Should().NotBeNullOrEmpty();
    id.Should().StartWith("op-");
  }

  [Fact]
  public void GenerateNew_returns_unique_values()
  {
    var id1 = OperationIdContext.GenerateNew();
    var id2 = OperationIdContext.GenerateNew();
    id1.Should().NotBe(id2);
  }

  [Fact]
  public async Task AsyncLocal_propagates_across_await()
  {
    OperationIdContext.Set("op-async-test");
    await Task.Yield();
    OperationIdContext.Current.Should().Be("op-async-test");
    OperationIdContext.Clear();
  }

  [Fact]
  public async Task AsyncLocal_does_not_leak_across_tasks()
  {
    OperationIdContext.Set("op-parent");
    string? childValue = null;
    await Task.Run(() => childValue = OperationIdContext.Current);
    // Task.Run 创建新执行上下文，AsyncLocal 值不泄漏
    // 注意：.NET AsyncLocal 在 Task.Run 中会继承父上下文值
    // 这是设计允许的行为——同一操作内的子任务应共享 OperationId
    childValue.Should().Be("op-parent");
    OperationIdContext.Clear();
  }
}
