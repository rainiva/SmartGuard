using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SmartGuard.Configuration;

/// <summary>
/// Identity of one physical log source, stable across a rename (rotation). Creation time plus a prefix hash
/// alone is not unique: a fallback that repeats the primary's first records, created in the same coarse
/// clock tick, would share it. For files on disk the file-system object id (volume serial and file index)
/// is part of the identity: it survives a rename and differs between files. Null when unavailable.
/// </summary>
public sealed record LogSourceIdentity(DateTimeOffset CreatedAtUtc, string ContentHashHex, string? FileId = null)
{
  public static LogSourceIdentity FromContentPrefix(DateTimeOffset createdAtUtc, ReadOnlySpan<byte> contentPrefix)
  {
    var hash = Convert.ToHexString(SHA256.HashData(contentPrefix)).ToLowerInvariant();
    return new LogSourceIdentity(createdAtUtc, hash);
  }

  public static LogSourceIdentity FromContentPrefix(DateTimeOffset createdAtUtc, string contentPrefix)
    => FromContentPrefix(createdAtUtc, Encoding.UTF8.GetBytes(contentPrefix));

  public static LogSourceIdentity FromFilePath(string path)
  {
    var createdAtUtc = File.Exists(path)
      ? new DateTimeOffset(File.GetCreationTimeUtc(path), TimeSpan.Zero)
      : DateTimeOffset.UtcNow;

    if (!File.Exists(path))
      return FromContentPrefix(createdAtUtc, Array.Empty<byte>());

    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    var prefix = ReadPrefixBytes(stream, 4096);
    return FromContentPrefix(createdAtUtc, prefix) with { FileId = TryGetFileId(stream.SafeFileHandle) };
  }

  private static byte[] ReadPrefixBytes(FileStream stream, int maxBytes)
  {
    var length = (int)Math.Min(maxBytes, stream.Length);
    var buffer = new byte[length];
    _ = stream.Read(buffer, 0, length);
    return buffer;
  }

  // Uses the handle already opened for the prefix read: no extra open.
  private static string? TryGetFileId(SafeFileHandle handle)
    => GetFileInformationByHandle(handle, out var info)
      ? $"{info.VolumeSerialNumber:x8}-{info.FileIndexHigh:x8}{info.FileIndexLow:x8}"
      : null;

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

  [StructLayout(LayoutKind.Sequential)]
  private struct ByHandleFileInformation
  {
    public uint FileAttributes;
    public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
    public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
    public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
    public uint VolumeSerialNumber;
    public uint FileSizeHigh;
    public uint FileSizeLow;
    public uint NumberOfLinks;
    public uint FileIndexHigh;
    public uint FileIndexLow;
  }
}
