using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace SmartGuard.Settings;

public sealed class HttpUpdateAssetDownloader : IUpdateAssetDownloader, IDisposable
{
    private readonly HttpClient _client;

    public HttpUpdateAssetDownloader(HttpClient client)
    {
        _client = client;
    }

    public async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            8192,
            true);

        var totalRead = 0L;
        var buffer = new byte[8192];
        int read;
        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
            totalRead += read;
            if (totalBytes > 0)
                progress.Report((double)totalRead / totalBytes);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
