namespace SmartGuard.Packaging.Staging;

public static class StagingLayoutValidator
{
    public static IReadOnlyList<string> Validate(string stagingDir, bool requireRedist = true)
    {
        var missing = new List<string>();
        foreach (var rel in StagingLayout.RequiredRelativePaths)
        {
            if (!File.Exists(Path.Combine(stagingDir, rel)))
                missing.Add(rel);
        }

        if (requireRedist)
        {
            var redistDir = Path.Combine(stagingDir, "redist");
            var runtimeFiles = Directory.Exists(redistDir)
                ? Directory.EnumerateFiles(redistDir, "windowsdesktop-runtime-*-win-x64.exe").ToList()
                : new List<string>();
            if (runtimeFiles.Count == 0)
                missing.Add(StagingLayout.RedistPattern);
            if (!File.Exists(Path.Combine(redistDir, "runtime-installer.txt")))
                missing.Add(@"redist\runtime-installer.txt");
        }

        return missing;
    }
}
