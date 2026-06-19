using System.Text.RegularExpressions;

namespace SmartGuard.Packaging.Versioning;

public static class InstallerVersionResolver
{
    public static string ReadCurrentVersion(string versionFilePath)
    {
        if (!File.Exists(versionFilePath))
            throw new FileNotFoundException("Missing installer version file.", versionFilePath);
        return File.ReadAllText(versionFilePath).Trim();
    }

    public static string BumpPatchVersion(string version, int increment = 1)
    {
        var match = Regex.Match(version, @"^(\d+)\.(\d+)\.(\d+)$");
        if (!match.Success)
            throw new ArgumentException($"Invalid installer version '{version}'. Expected major.minor.patch.", nameof(version));

        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        var patch = int.Parse(match.Groups[3].Value) + increment;
        return $"{major}.{minor}.{patch}";
    }

    public static string BumpVersionFile(string versionFilePath)
    {
        var current = ReadCurrentVersion(versionFilePath);
        var next = BumpPatchVersion(current);
        File.WriteAllText(versionFilePath, next);
        return next;
    }
}
