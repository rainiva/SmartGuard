using System.Net;
using FluentAssertions;
using SmartGuard.Packaging.Runtime;

namespace SmartGuard.Packaging.Tests.Runtime;

public class DesktopRuntimeRedistDownloaderTests : IDisposable
{
    private readonly string _tempDir;

    public DesktopRuntimeRedistDownloaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task EnsureRedistAsync_reuses_existing_valid_file_and_writes_marker()
    {
        var fileName = "windowsdesktop-runtime-8.0.18-win-x64.exe";
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(path, new byte[51 * 1024 * 1024]);

        var handler = new TestHandler((req, ct) => throw new InvalidOperationException("Should not download"));
        var downloader = new DesktopRuntimeRedistDownloader(new HttpClient(handler));

        var result = await downloader.EnsureRedistAsync(_tempDir, "8.0.18");
        result.Should().Be(fileName);
        File.ReadAllText(Path.Combine(_tempDir, "runtime-installer.txt")).Should().Be(fileName);
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task EnsureRedistAsync_downloads_when_file_missing()
    {
        var fileName = "windowsdesktop-runtime-8.0.18-win-x64.exe";
        var handler = new TestHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[51 * 1024 * 1024])
            });
        });
        var downloader = new DesktopRuntimeRedistDownloader(new HttpClient(handler));

        var result = await downloader.EnsureRedistAsync(_tempDir, "8.0.18");
        result.Should().Be(fileName);
        File.Exists(Path.Combine(_tempDir, fileName)).Should().BeTrue();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;
        public int RequestCount { get; private set; }

        public TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return await _sendAsync(request, cancellationToken);
        }
    }
}
