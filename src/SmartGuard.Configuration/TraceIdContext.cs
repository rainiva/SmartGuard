namespace SmartGuard.Configuration;

/// <summary>
/// TraceId 进程内 AsyncLocal 异步流传播上下文。
/// 跨进程中继经 StatusPublisher 写入 status.json traceId 字段（D3 裁决）。
/// 与 OperationIdContext 同模式。
/// </summary>
public static class TraceIdContext
{
  private static readonly AsyncLocal<string?> _current = new();

  public static string? Current => _current.Value;

  public static void Set(string traceId) => _current.Value = traceId;

  public static void Clear() => _current.Value = null;

  public static string GenerateNew()
  {
    var id = "trace-" + Guid.NewGuid().ToString("N")[..16];
    Set(id);
    return id;
  }
}
