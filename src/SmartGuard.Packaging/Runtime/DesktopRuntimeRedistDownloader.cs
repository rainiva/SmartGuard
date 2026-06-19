namespace SmartGuard.Packaging.Runtime;

public class DesktopRuntimeRedistDownloader : IRuntimeRedistDownloader
{
    private readonly HttpClient _httpClient;

    public DesktopRuntimeRedistDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string> EnsureRedistAsync(string redistDir, string runtimeVersion, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(redistDir);
        var fileName = $"windowsdesktop-runtime-{runtimeVersion}-win-x64.exe";
        var dest = Path.Combine(redistDir, fileName);
        var marker = Path.Combine(redistDir, "runtime-installer.txt");

        if (IsValidRedist(dest))
        {
            await File.WriteAllTextAsync(marker, fileName, cancellationToken);
            return fileName;
        }

        if (File.Exists(dest))
            File.Delete(dest);

        var url = $"https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/{runtimeVersion}/{fileName}";
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fs, cancellationToken);

        if (!IsValidRedist(dest))
            throw new InvalidOperationException($"Download incomplete or corrupt: {dest}");

        await File.WriteAllTextAsync(marker, fileName, cancellationToken);
        return fileName;
    }

    private static bool IsValidRedist(string path)
    {
        return File.Exists(path) && new FileInfo(path).Length > 50L * 1024 * 1024;
    }
}
