using System.Security.Cryptography;

namespace SmartGuard.Configuration.Build;

public static class SystemEventsRidAssetGuard
{
  public const string FileName = "Microsoft.Win32.SystemEvents.dll";
  public const string WinRidRelativePath = @"runtimes\win\lib\net8.0\Microsoft.Win32.SystemEvents.dll";

  public static IReadOnlyList<string> Validate(string binDirectory)
  {
    var errors = new List<string>();
    var rootPath = Path.Combine(binDirectory, FileName);
    var ridPath = Path.Combine(binDirectory, WinRidRelativePath);
    var rootExists = File.Exists(rootPath);
    var ridExists = File.Exists(ridPath);

    if (!rootExists && !ridExists)
      return errors;

    if (!ridExists)
    {
      errors.Add($"{WinRidRelativePath} missing while {FileName} is present at bin root");
      return errors;
    }

    if (!rootExists)
    {
      errors.Add($"{FileName} missing at bin root while RID asset exists");
      return errors;
    }

    if (!HashesEqual(rootPath, ridPath))
      errors.Add($"{FileName} root hash mismatch vs {WinRidRelativePath}");

    return errors;
  }

  public static void EnsureAligned(string binDirectory)
  {
    var ridPath = Path.Combine(binDirectory, WinRidRelativePath);
    if (!File.Exists(ridPath))
      return;

    var rootPath = Path.Combine(binDirectory, FileName);
    Directory.CreateDirectory(binDirectory);
    File.Copy(ridPath, rootPath, overwrite: true);
  }

  private static bool HashesEqual(string leftPath, string rightPath)
  {
    var left = SHA256.HashData(File.ReadAllBytes(leftPath));
    var right = SHA256.HashData(File.ReadAllBytes(rightPath));
    return left.AsSpan().SequenceEqual(right);
  }
}
