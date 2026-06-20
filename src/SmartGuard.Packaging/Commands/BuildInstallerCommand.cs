using System.Diagnostics;
using SmartGuard.Packaging.Runtime;
using SmartGuard.Packaging.Versioning;

namespace SmartGuard.Packaging.Commands;

public class BuildInstallerCommand
{
    private readonly Action<string> _log;
    private readonly Func<string, string, int>? _publishRunner;
    private readonly IRuntimeRedistDownloader _downloader;
    private readonly Func<string, int>? _isccRunner;

    public BuildInstallerCommand(Action<string>? log = null, Func<string, string, int>? publishRunner = null, IRuntimeRedistDownloader? downloader = null, Func<string, int>? isccRunner = null)
    {
        _log = log ?? Console.WriteLine;
        _publishRunner = publishRunner;
        _downloader = downloader ?? new DesktopRuntimeRedistDownloader();
        _isccRunner = isccRunner;
    }

    public int Run(BuildInstallerOptions options)
    {
        var root = Path.GetFullPath(options.Root);
        var stagingDir = string.IsNullOrEmpty(options.StagingDir)
            ? Path.Combine(root, "installer", "staging")
            : Path.GetFullPath(options.StagingDir);

        var versionFile = Path.Combine(root, "installer", "version.txt");
        var version = options.SkipVersionBump
            ? InstallerVersionResolver.ReadCurrentVersion(versionFile)
            : InstallerVersionResolver.BumpVersionFile(versionFile);
        _log($"Installer version: {version}");

        var stage = new StageCommand(_log, _publishRunner, _downloader);
        var stageExit = stage.Run(new StageOptions(root, options.Configuration, stagingDir, options.RuntimeVersion, options.SkipPublish, options.SkipRedistDownload));
        if (stageExit != 0) return stageExit;

        var runtimeMarker = Path.Combine(stagingDir, "redist", "runtime-installer.txt");
        var runtimeFile = File.ReadAllText(runtimeMarker).Trim();

        var iscc = ResolveIscc(options.IsccPath);
        var iss = Path.Combine(root, "installer", "SmartGuard.iss");
        _log($"Compiling installer with: {iscc}");

        int exitCode;
        if (_isccRunner != null)
        {
            exitCode = _isccRunner(iscc);
        }
        else
        {
            var psi = new ProcessStartInfo(iscc)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add($"/DStagingDir={stagingDir}");
            psi.ArgumentList.Add($"/DMyAppVersion={version}");
            psi.ArgumentList.Add($"/DRuntimeInstallerFile={runtimeFile}");
            psi.ArgumentList.Add(iss);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ISCC");
            p.WaitForExit();
            exitCode = p.ExitCode;
        }

        if (exitCode != 0)
        {
            _log("ISCC compilation failed.");
            return exitCode;
        }

        _log($"Installer output: {Path.Combine(root, "dist", $"SmartGuard-Setup-{version}-x64.exe")}");
        return 0;
    }

    private string ResolveIscc(string? preferred)
    {
        if (!string.IsNullOrEmpty(preferred) && File.Exists(preferred))
            return preferred!;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 6", "ISCC.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inno Setup 6", "ISCC.exe"),
            @"D:\Apps\Inno Setup 6\ISCC.exe"
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        var env = Environment.GetEnvironmentVariable("SMARTGUARD_ISCC");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        throw new FileNotFoundException("ISCC.exe not found. Install Inno Setup 6 or set SMARTGUARD_ISCC.");
    }
}
