namespace SmartGuard.Packaging.Staging;

public static class FakeStagingBuilder
{
    public static void Build(string stagingDir, string runtimeVersion = "8.0.18")
    {
        Directory.CreateDirectory(stagingDir);
        foreach (var rel in StagingLayout.RequiredRelativePaths)
        {
            var full = Path.Combine(stagingDir, rel);
            var parent = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllText(full, "placeholder");
        }

        var redistDir = Path.Combine(stagingDir, "redist");
        Directory.CreateDirectory(redistDir);
        var runtimeFile = $"windowsdesktop-runtime-{runtimeVersion}-win-x64.exe";
        File.WriteAllText(Path.Combine(redistDir, runtimeFile), "placeholder");
        File.WriteAllText(Path.Combine(redistDir, "runtime-installer.txt"), runtimeFile);
    }
}
