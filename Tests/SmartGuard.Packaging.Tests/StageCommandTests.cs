using FluentAssertions;
using SmartGuard.Packaging.Commands;
using SmartGuard.Packaging.Runtime;

namespace SmartGuard.Packaging.Tests.Commands;

public class StageCommandTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _tempDir;

    public StageCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _repoRoot = _tempDir;
    }

    [Fact]
    public void Run_creates_valid_staging_with_fake_dependencies()
    {
        // Arrange: fake repo with bin + lib + installer/version + license
        Directory.CreateDirectory(Path.Combine(_repoRoot, "bin"));
        File.WriteAllText(Path.Combine(_repoRoot, "bin", "SmartGuard.Engine.exe"), "exe");
        File.WriteAllText(Path.Combine(_repoRoot, "bin", "SmartGuard.Tray.exe"), "exe");
        File.WriteAllText(Path.Combine(_repoRoot, "bin", "SmartGuard.LogViewer.exe"), "exe");
        File.WriteAllText(Path.Combine(_repoRoot, "bin", "SmartGuard.Settings.exe"), "exe");
        Directory.CreateDirectory(Path.Combine(_repoRoot, "lib"));
        File.WriteAllText(Path.Combine(_repoRoot, "lib", "SmartGuard.ico"), "ico");
        File.WriteAllText(Path.Combine(_repoRoot, "lib", "SmartGuard.Settings.xaml"), "xaml");
        Directory.CreateDirectory(Path.Combine(_repoRoot, "installer"));
        File.WriteAllText(Path.Combine(_repoRoot, "installer", "version.txt"), "1.0.25");
        File.WriteAllText(Path.Combine(_repoRoot, "installer", "license_zh-CN.txt"), "license");

        var staging = Path.Combine(_repoRoot, "installer", "staging");
        var publishCalls = new List<(string root, string cfg)>();
        var fakeDownloader = new FakeDownloader("windowsdesktop-runtime-8.0.18-win-x64.exe");
        var cmd = new StageCommand(_ => { }, (r, c) => { publishCalls.Add((r, c)); return 0; }, fakeDownloader);

        // Act
        var exit = cmd.Run(new StageOptions(_repoRoot, "Release", staging, "8.0.18", SkipPublish: false, SkipRedistDownload: false));

        // Assert
        exit.Should().Be(0);
        publishCalls.Should().ContainSingle();
        File.Exists(Path.Combine(staging, "bin", "SmartGuard.Engine.exe")).Should().BeTrue();
        File.Exists(Path.Combine(staging, "lib", "SmartGuard.ico")).Should().BeTrue();
        File.ReadAllText(Path.Combine(staging, "VERSION.txt")).Should().Be("1.0.25");
        File.ReadAllText(Path.Combine(staging, "redist", "runtime-installer.txt")).Should().Be("windowsdesktop-runtime-8.0.18-win-x64.exe");
        fakeDownloader.Calls.Should().ContainSingle();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private class FakeDownloader : IRuntimeRedistDownloader
    {
        private readonly string _fileName;
        public List<(string dir, string version)> Calls { get; } = new();

        public FakeDownloader(string fileName) => _fileName = fileName;

        public Task<string> EnsureRedistAsync(string redistDir, string runtimeVersion, CancellationToken cancellationToken = default)
        {
            Calls.Add((redistDir, runtimeVersion));
            Directory.CreateDirectory(redistDir);
            File.WriteAllText(Path.Combine(redistDir, _fileName), "redist");
            File.WriteAllText(Path.Combine(redistDir, "runtime-installer.txt"), _fileName);
            return Task.FromResult(_fileName);
        }
    }
}
