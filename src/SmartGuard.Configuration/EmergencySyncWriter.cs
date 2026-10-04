namespace SmartGuard.Configuration;

/// <summary>
/// 应急同步写入器：崩溃时绕过异步管线，直接同步追加到磁盘。
/// 全路径异常隔离，永不抛出。
/// </summary>
public sealed class EmergencySyncWriter
{
  public static bool TryWrite(string path, IReadOnlyList<string> lines)
    => SmartGuardLogWriter.TryAppendSupplementalLines(path, lines, emergency: true);
}
