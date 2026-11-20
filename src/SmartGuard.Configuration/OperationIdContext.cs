namespace SmartGuard.Configuration;

/// <summary>
/// OperationId 全进程传播上下文：AsyncLocal 进程内异步流传播，
/// 跨进程经 status.json operationId 字段中继（D2 裁决）。
/// </summary>
public static class OperationIdContext
{
  private static readonly AsyncLocal<string?> _current = new();

  public static string? Current => _current.Value;

  public static void Set(string operationId) => _current.Value = operationId;

  public static void Clear() => _current.Value = null;

  public static string GenerateNew()
  {
    var id = "op-" + Guid.NewGuid().ToString("N")[..8];
    Set(id);
    return id;
  }
}
