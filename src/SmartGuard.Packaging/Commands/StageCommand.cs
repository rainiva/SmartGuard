using System.Diagnostics;
using SmartGuard.Packaging.Runtime;
using SmartGuard.Packaging.Staging;
using SmartGuard.Packaging.Versioning;

namespace SmartGuard.Packaging.Commands;

public class StageCommand
{
    private readonly Action<string> _log;
    private readonly Func<string, string, int>? _publishRunner;
    private readonly IRuntimeRedistDownloader _downloader;

    public StageCommand(Action<string>? log = null, Func<string, string, int>? publishRunner = null, IRuntimeRedistDownloader? downloader = null)
    {
        _log = log ?? Console.WriteLine;
        _publishRunner = publishRunner;
        _downloader = downloader ?? new DesktopRuntimeRedistDownloader();
    }

    public int Run(StageOptions options)
    {
        var root = Path.GetFullPath(options.Root);
        var stagingDir = string.IsNullOrEmpty(options.StagingDir)
            ? Path.Combine(root, "installer", "staging")
            : Path.GetFullPath(options.StagingDir);

        if (!File.Exists(Path.Combine(root, "lib", "SmartGuard.ico")))
            throw new FileNotFoundException("Missing required asset: SmartGuard.ico");

        if (!options.SkipPublish)
        {
            var exit = (_publishRunner ?? DefaultPublishRunner)(root, options.Configuration);
            if (exit != 0) return exit;
        }

        if (!Directory.Exists(Path.Combine(root, "bin")))
            throw new DirectoryNotFoundException($"Publish output not found: {Path.Combine(root, "bin")}");

        ResetStaging(stagingDir);
        PayloadCopier.Copy(root, stagingDir);

        var redistFile = _downloader.EnsureRedistAsync(Path.Combine(stagingDir, "redist"), options.RuntimeVersion).GetAwaiter().GetResult();

        var errors = StagingLayoutValidator.Validate(stagingDir, requireRedist: true);
        if (errors.Count > 0)
            throw new InvalidOperationException("Installer staging incomplete. Missing: " + string.Join(", ", errors));

        _log($"Staging ready: {stagingDir} (runtime: {redistFile})");
        return 0;
    }

    private static int DefaultPublishRunner(string root, string configuration)
    {
        var psi = new ProcessStartInfo("cmd.exe")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(Path.Combine(root, "build.cmd"));
        psi.ArgumentList.Add(configuration);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start build.cmd");
        p.WaitForExit();
        return p.ExitCode;
    }

    private static void ResetStaging(string stagingDir)
    {
        var redistDir = Path.Combine(stagingDir, "redist");
        string? backup = null;
        if (Directory.Exists(redistDir))
        {
            backup = Path.Combine(Path.GetTempPath(), "sg-redist-backup-" + Guid.NewGuid().ToString("N"));
            Directory.Move(redistDir, backup);
        }
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, true);
        Directory.CreateDirectory(stagingDir);
        if (backup != null && Directory.Exists(backup))
            Directory.Move(backup, redistDir);
    }
}
