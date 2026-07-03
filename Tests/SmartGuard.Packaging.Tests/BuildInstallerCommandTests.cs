using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Configuration.Distribution;
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
    public void Run_bumps_version_and_generates_manifest_includes_before_iscc()
    {
        var root = CreateFakeRepo();
        var staging = Path.Combine(root, "installer", "staging");
        var fakeDownloader = new FakeDownloader("windowsdesktop-runtime-8.0.18-win-x64.exe");
        var generatedFiles = Path.Combine(root, "installer", "generated");
        var cmd = new BuildInstallerCommand(
            _ => { },
            (r, c) => 0,
            fakeDownloader,
            _ => 0);

        var exit = cmd.Run(new BuildInstallerOptions(root, "Release", staging, SkipPublish: false, SkipRedistDownload: true, SkipVersionBump: true));

        exit.Should().Be(0);
        var filesIncludePath = Path.Combine(generatedFiles, "SmartGuard.Distribution.Files.iss.inc");
        var processesIncludePath = Path.Combine(generatedFiles, "SmartGuard.Distribution.Processes.iss.inc");
        var constantsIncludePath = Path.Combine(generatedFiles, "SmartGuard.Distribution.Constants.iss.inc");
        var runIncludePath = Path.Combine(generatedFiles, "SmartGuard.Distribution.Run.iss.inc");
        File.Exists(filesIncludePath).Should().BeTrue();
        File.Exists(processesIncludePath).Should().BeTrue();
        File.Exists(constantsIncludePath).Should().BeTrue();
        File.Exists(runIncludePath).Should().BeTrue();
        File.ReadAllText(filesIncludePath).Should().NotContain("SmartGuard.LogViewer.exe");
        File.ReadAllText(processesIncludePath).Should().NotContain("SmartGuard.LogViewer.exe");
        var constantsInclude = File.ReadAllText(constantsIncludePath);
        constantsInclude.Should().Contain($"#define TrayExeName \"{SmartGuardPaths.TrayExeFileName}\"");
        constantsInclude.Should().Contain($"#define EngineExeName \"{SmartGuardPaths.EngineExeFileName}\"");
        constantsInclude.Should().Contain($"#define CliExeName \"{SmartGuardPaths.CliExeFileName}\"");
        constantsInclude.Should().Contain($"#define GuardianTaskName \"{ScheduledTaskRegistrar.GuardianTaskName}\"");
        var runInclude = File.ReadAllText(runIncludePath);
        runInclude.Should().Contain("{#EngineExeName}");
        runInclude.Should().Contain("{#CliExeName}");
        runInclude.Should().Contain("{#GuardianTaskName}");
        File.ReadAllText(Path.Combine(root, "installer", "version.txt")).Trim().Should().Be("1.0.5");
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
        versionAtStageTime.Should().Be("1.0.6", "version must be bumped before the publish/stage step reads it");
        File.ReadAllText(Path.Combine(root, "installer", "version.txt")).Trim().Should().Be("1.0.6");
    }

    private string CreateFakeRepo()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "bin"));
        foreach (var project in DesktopDistributionManifest.PublishedProjects)
            File.WriteAllText(Path.Combine(_tempDir, "bin", project.OutputFileName), "exe");
        Directory.CreateDirectory(Path.Combine(_tempDir, "installer"));
        File.WriteAllText(Path.Combine(_tempDir, "installer", "version.txt"), "1.0.5");
        foreach (var payload in DesktopDistributionManifest.PayloadEntries)
        {
            var sourcePath = Path.Combine(_tempDir, payload.SourceRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, "payload");
        }
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
