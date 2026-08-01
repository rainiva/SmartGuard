namespace SmartGuard.Configuration;

/// <summary>
/// SpanId 进程内 AsyncLocal 异步流传播上下文（D3 裁决）。
/// 用于细化一次操作内的子步骤关联。
/// </summary>
public static class SpanIdContext
{
  private static readonly AsyncLocal<string?> _current = new();

  public static string? Current => _current.Value;

  public static void Set(string spanId) => _current.Value = spanId;

  public static void Clear() => _current.Value = null;

  public static string GenerateNew()
  {
    var id = "span-" + Guid.NewGuid().ToString("N")[..8];
    Set(id);
    return id;
  }
}
