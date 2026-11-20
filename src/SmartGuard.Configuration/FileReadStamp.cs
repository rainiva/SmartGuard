namespace SmartGuard.Configuration;

/// <summary>
/// Decides whether a cached read of a small file can be reused without touching the disk. The stamp is the
/// file's last write time and length, captured before the content is read. File timestamps come from a clock
/// that can be as coarse as about 16 ms, so a write inside that tick can carry the cached stamp unchanged
/// (the "racy git" problem). A cached read is therefore trusted only when its write is older than
/// <see cref="SettleWindow"/>; until then every read goes back to disk. A file left unchanged for longer
/// than the window is read once and then served from memory.
/// </summary>
internal readonly record struct FileReadStamp(DateTime WriteTimeUtc, long Length)
{
  internal static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(1);

  /// <summary>
  /// Null when no stamp can be taken (missing file, a directory at the path, access failure). A null stamp
  /// never matches a cache, so the caller's own content read decides how the failure is classified.
  /// </summary>
  internal static FileReadStamp? TryCapture(string path)
  {
    try
    {
      var info = new FileInfo(path);
      return info.Exists ? new(info.LastWriteTimeUtc, info.Length) : null;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
    {
      return null;
    }
  }

  /// <summary>Wall-clock UTC on both sides; a write time in the future (clock moved back) is never settled.</summary>
  internal bool IsSettled(DateTime nowUtc) => nowUtc - WriteTimeUtc > SettleWindow;
}
