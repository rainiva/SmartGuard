namespace SmartGuard.Engine.Infrastructure;

public static class FileLogger
{
  public static void RotateIfNeeded(string logPath, long maxBytes)
  {
    if (!File.Exists(logPath)) return;
    if (new FileInfo(logPath).Length <= maxBytes) return;
    var archive = logPath + ".old";
    if (File.Exists(archive)) File.Delete(archive);
    File.Move(logPath, archive);
  }

  public static void WriteLine(string logPath, string line)
  {
    var dir = Path.GetDirectoryName(logPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
    using var sw = new StreamWriter(fs, System.Text.Encoding.UTF8);
    sw.WriteLine(line);
  }
}
