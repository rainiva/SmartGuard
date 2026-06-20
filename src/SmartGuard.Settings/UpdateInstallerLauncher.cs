using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SmartGuard.Settings;

public interface IUpdateAssetDownloader
{
    Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<double> progress,
        CancellationToken cancellationToken);
}

public sealed class UpdateInstallerLauncher
{
    public static (string? AssetName, string? DownloadUrl) ResolveAsset(JsonElement releaseRoot, Version targetVersion)
    {
        var expectedName = $"SmartGuard-Setup-{targetVersion}-x64.exe";
        if (!releaseRoot.TryGetProperty("assets", out var assets))
            return (null, null);

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name == expectedName && asset.TryGetProperty("browser_download_url", out var urlProp))
            {
                var url = urlProp.GetString();
                if (!string.IsNullOrEmpty(url))
                    return (name, url);
            }
        }

        return (null, null);
    }

    public static string GetLocalInstallerPath(string assetName)
    {
        return Path.Combine(Path.GetTempPath(), assetName);
    }

    public static void StartInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath, "/SILENT")
        {
            UseShellExecute = true,
            Verb = "runas"
        });
    }
}
