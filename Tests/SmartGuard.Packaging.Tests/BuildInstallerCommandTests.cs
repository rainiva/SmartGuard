using FluentAssertions;
using SmartGuard.Packaging.Commands;
using SmartGuard.Packaging.Runtime;
using SmartGuard.Packaging.Staging;

namespace SmartGuard.Packaging.Tests.Commands;

public class BuildInstallerCommandTests : IDisposable
{
    private readonly string _tempDir;

    public BuildInstallerCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Run_bumps_version_and_invokes_stage()
    {
        var root = CreateFakeRepo();
        var staging = Path.Combine(root, "installer", "staging");
        var fakeDownloader = new FakeDownloader("windowsdesktop-runtime-8.0.18-win-x64.exe");
        var stageRan = false;
        var cmd = new BuildInstallerCommand(
            _ => { },
            (r, c) => { stageRan = true; return 0; },
            fakeDownloader,
            _ => 0);

        var exit = cmd.Run(new BuildInstallerOptions(root, "Release", staging, SkipPublish: false, SkipRedistDownload: true, SkipVersionBump: true));

        exit.Should().Be(0);
        stageRan.Should().BeTrue();
        File.ReadAllText(Path.Combine(root, "installer", "version.txt")).Trim().Should().Be("1.0.25");
    }

    [Fact]
    public void Run_bumps_version_before_stage_runs()
    {
        var root = CreateFakeRepo();
        var staging = Path.Combine(root, "installer", "staging");
        var fakeDownloader = new FakeDownloader("windowsdesktop-runtime-8.0.18-win-x64.exe");
        var versionAtStageTime = "not-set";
        var cmd = new BuildInstallerCommand(
            _ => { },
            (r, c) =>
            {
                versionAtStageTime = File.ReadAllText(Path.Combine(r, "installer", "version.txt")).Trim();
                return 0;
            },
            fakeDownloader,
            _ => 0);

        var exit = cmd.Run(new BuildInstallerOptions(root, "Release", staging, SkipPublish: false, SkipRedistDownload: true, SkipVersionBump: false));

        exit.Should().Be(0);
        versionAtStageTime.Should().Be("1.0.26", "version must be bumped before the publish/stage step reads it");
        File.ReadAllText(Path.Combine(root, "installer", "version.txt")).Trim().Should().Be("1.0.26");
    }

    private string CreateFakeRepo()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "bin"));
        foreach (var f in new[] { "SmartGuard.Engine.exe", "SmartGuard.Tray.exe", "SmartGuard.LogViewer.exe", "SmartGuard.Settings.exe" })
            File.WriteAllText(Path.Combine(_tempDir, "bin", f), "exe");
        Directory.CreateDirectory(Path.Combine(_tempDir, "lib"));
        File.WriteAllText(Path.Combine(_tempDir, "lib", "SmartGuard.ico"), "ico");
        File.WriteAllText(Path.Combine(_tempDir, "lib", "SmartGuard.Settings.xaml"), "xaml");
        Directory.CreateDirectory(Path.Combine(_tempDir, "installer"));
        File.WriteAllText(Path.Combine(_tempDir, "installer", "version.txt"), "1.0.25");
        File.WriteAllText(Path.Combine(_tempDir, "installer", "license_zh-CN.txt"), "license");
        return _tempDir;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private class FakeDownloader : IRuntimeRedistDownloader
    {
        private readonly string _fileName;
        public FakeDownloader(string fileName) => _fileName = fileName;
        public Task<string> EnsureRedistAsync(string redistDir, string runtimeVersion, CancellationToken ct = default)
        {
            Directory.CreateDirectory(redistDir);
            File.WriteAllText(Path.Combine(redistDir, _fileName), "redist");
            File.WriteAllText(Path.Combine(redistDir, "runtime-installer.txt"), _fileName);
            return Task.FromResult(_fileName);
        }
    }
}
