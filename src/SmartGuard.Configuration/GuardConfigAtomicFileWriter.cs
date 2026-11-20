namespace SmartGuard.Configuration;

internal static class GuardConfigAtomicFileWriter
{
  internal static Action<string, string>? BeforeMoveForTests;

  internal static void WriteAllText(string path, string content)
  {
    var temp = path + ".tmp";
    File.WriteAllText(temp, content);
    BeforeMoveForTests?.Invoke(temp, path);
    // Keep the last published policy in place until the OS commits its replacement.
    // Delete-then-Move loses it if publication fails or the process is interrupted.
    if (File.Exists(path))
      File.Replace(temp, path, destinationBackupFileName: null);
    else
      File.Move(temp, path);
  }
}
