namespace SmartGuard.Configuration;

/// <summary>
/// 应急同步写入器：崩溃时绕过异步管线，直接同步追加到磁盘。
/// 全路径异常隔离，永不抛出。
/// </summary>
public sealed class EmergencySyncWriter
{
  public static bool TryWrite(string path, IReadOnlyList<string> lines)
  {
    if (lines.Count == 0) return true;
    try
    {
      var dir = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
      using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
      using var sw = new StreamWriter(fs, new System.Text.UTF8Encoding(false));
      foreach (var line in lines)
        sw.WriteLine(line);
      return true;
    }
    catch
    {
      return false; // 应急写入失败绝不上抛
    }
  }
}
