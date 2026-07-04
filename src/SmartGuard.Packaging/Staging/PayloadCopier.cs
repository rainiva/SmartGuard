using SmartGuard.Configuration.Distribution;
using SmartGuard.Packaging.Versioning;

namespace SmartGuard.Packaging.Staging;

public static class PayloadCopier
{
    public static void Copy(string root, string stagingDir)
    {
        CopyDirectory(Path.Combine(root, "bin"), Path.Combine(stagingDir, "bin"));

        foreach (var payload in DesktopDistributionManifest.PayloadEntries)
        {
            var sourcePath = Path.Combine(root, payload.SourceRelativePath);
            var destinationPath = Path.Combine(stagingDir, payload.DestinationRelativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);
            File.Copy(sourcePath, destinationPath, true);
        }

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
