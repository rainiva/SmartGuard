using System.Security.Cryptography;
using System.Text;

namespace SmartGuard.Configuration;

public sealed record LogSourceIdentity(DateTimeOffset CreatedAtUtc, string ContentHashHex)
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

    var prefix = ReadPrefixBytes(path, 4096);
    return FromContentPrefix(createdAtUtc, prefix);
  }

  private static byte[] ReadPrefixBytes(string path, int maxBytes)
  {
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    var length = (int)Math.Min(maxBytes, stream.Length);
    var buffer = new byte[length];
    _ = stream.Read(buffer, 0, length);
    return buffer;
  }
}
