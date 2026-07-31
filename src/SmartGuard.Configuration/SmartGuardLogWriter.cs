using System.Security.Cryptography;
using System.Text;

namespace SmartGuard.Configuration;

public static class SmartGuardLogWriter
{
  // 统一日志追加入口：命名 Mutex（最佳努力）+ FileShare 追加 + 有限重试；永不抛出。
  public static bool TryAppendLine(string path, string line)
  {
    try
    {
      var dir = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

      Mutex? mutex = null;
      var held = false;
      try
      {
        try
        {
          mutex = new Mutex(false, MutexName(path));
          held = mutex.WaitOne(TimeSpan.FromSeconds(2));
        }
        catch (AbandonedMutexException) { held = true; }
        catch { mutex = null; } // 无法创建/获取命名 Mutex 时降级为纯 FileShare + 重试

        for (var attempt = 0; attempt < 3; attempt++)
        {
          try
          {
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var sw = new StreamWriter(fs, new UTF8Encoding(false));
            sw.WriteLine(line);
            return true;
          }
          catch (IOException)
          {
            Thread.Sleep(20); // 文件被临时占用，退避后重试
          }
        }
        return false;
      }
      finally
      {
        if (mutex is not null)
        {
          if (held)
          {
            try { mutex.ReleaseMutex(); } catch { /* 忽略释放失败 */ }
          }
          mutex.Dispose();
        }
      }
    }
    catch
    {
      return false; // 任何未预期异常都不得上抛
    }
  }

  private static string MutexName(string path)
  {
    var normalized = Path.GetFullPath(path).ToLowerInvariant();
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    return @"Local\SmartGuardLog_" + hash[..16];
  }
}
