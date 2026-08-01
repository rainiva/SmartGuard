namespace SmartGuard.Configuration;

/// <summary>
/// UserActionId 进程内 AsyncLocal 异步流传播上下文（D3 裁决）。
/// 用于关联用户交互操作（如点击托盘菜单、切换设置项）触发的日志事件链。
/// </summary>
public static class UserActionIdContext
{
  private static readonly AsyncLocal<string?> _current = new();

  public static string? Current => _current.Value;

  public static void Set(string userActionId) => _current.Value = userActionId;

  public static void Clear() => _current.Value = null;

  public static string GenerateNew()
  {
    var id = "ua-" + Guid.NewGuid().ToString("N")[..8];
    Set(id);
    return id;
  }
}
