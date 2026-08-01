namespace SmartGuard.Configuration;

/// <summary>
/// 日志索引集合：由 LogIndexBuilder 从日志文件构建的内存索引。
/// 原始日志文件仍为真源，索引损坏可重建。
/// </summary>
public sealed record LogIndex(IReadOnlyList<LogIndexEntry> Entries, string SourcePath)
{
  public static LogIndex Empty(string sourcePath) =>
    new(Array.Empty<LogIndexEntry>(), sourcePath);
}
