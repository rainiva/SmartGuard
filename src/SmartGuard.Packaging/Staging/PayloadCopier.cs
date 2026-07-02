using SmartGuard.Packaging.Versioning;

namespace SmartGuard.Packaging.Staging;

public static class PayloadCopier
{
    public static void Copy(string root, string stagingDir)
    {
        CopyDirectory(Path.Combine(root, "bin"), Path.Combine(stagingDir, "bin"));

        Directory.CreateDirectory(Path.Combine(stagingDir, "lib"));
        File.Copy(Path.Combine(root, "lib", "SmartGuard.ico"), Path.Combine(stagingDir, "lib", "SmartGuard.ico"), true);
        File.Copy(Path.Combine(root, "lib", "SmartGuard.Settings.xaml"), Path.Combine(stagingDir, "lib", "SmartGuard.Settings.xaml"), true);
        File.Copy(Path.Combine(root, "installer", "license_zh-CN.txt"), Path.Combine(stagingDir, "license_zh-CN.txt"), true);

        var version = InstallerVersionResolver.ReadCurrentVersion(Path.Combine(root, "installer", "version.txt"));
        File.WriteAllText(Path.Combine(stagingDir, "VERSION.txt"), version);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.Copy(file, target, true);
        }
    }
}
