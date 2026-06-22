namespace SmartGuard.Configuration;

internal static class GuardConfigAtomicFileWriter
{
  internal static Action<string, string>? BeforeMoveForTests;

  internal static void WriteAllText(string path, string content)
  {
    var temp = path + ".tmp";
    File.WriteAllText(temp, content);
    BeforeMoveForTests?.Invoke(temp, path);
    if (File.Exists(path))
      File.Delete(path);
    File.Move(temp, path);
  }
}
