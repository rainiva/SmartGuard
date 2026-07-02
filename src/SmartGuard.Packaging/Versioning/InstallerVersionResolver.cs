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

        if (increment < 0)
            throw new ArgumentOutOfRangeException(nameof(increment), increment, "Increment must be non-negative.");

        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        var patch = int.Parse(match.Groups[3].Value);

        if (minor is < 0 or > 9 || patch is < 0 or > 9)
            throw new ArgumentException($"Invalid installer version '{version}'. Minor and patch must be 0-9.", nameof(version));

        var total = major * 100 + minor * 10 + patch + increment;
        major = total / 100;
        minor = (total / 10) % 10;
        patch = total % 10;
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
