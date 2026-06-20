using System.Diagnostics;
using FluentAssertions;

namespace SmartGuard.Engine.PerformanceTests;

public class EnginePerformanceTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _engineExe;
    private readonly string _logPath;

    public EnginePerformanceTests()
    {
        _repoRoot = RepoRootResolver.Resolve();
        _engineExe = Path.Combine(_repoRoot, "bin", "SmartGuard.Engine.exe");
        _logPath = Path.Combine(_repoRoot, "SmartGuard.log");
    }

    [SkippableFact]
    public void Startup_should_complete_within_500ms()
    {
        Skip.IfNot(File.Exists(_engineExe), "Engine exe not published. Run build.cmd first.");
        StopEngine();
        var logBytesBefore = File.Exists(_logPath) ? new FileInfo(_logPath).Length : 0L;

        var sw = Stopwatch.StartNew();
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = _engineExe,
            Arguments = $"--root \"{_repoRoot}\"",
            WorkingDirectory = _repoRoot,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false
        });
        proc.Should().NotBeNull();

        var found = WaitForLogAfter(logBytesBefore, "SmartGuard Engine 启动", TimeSpan.FromSeconds(10));
        sw.Stop();
        StopEngine();

        found.Should().BeTrue("engine should write startup marker to log");
        sw.ElapsedMilliseconds.Should().BeLessThan(3000);
    }

    [SkippableFact]
    public void Memory_should_be_below_20MB()
    {
        Skip.IfNot(File.Exists(_engineExe), "Engine exe not published. Run build.cmd first.");
        StopEngine();
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = _engineExe,
            Arguments = $"--root \"{_repoRoot}\"",
            WorkingDirectory = _repoRoot,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false
        });
        proc.Should().NotBeNull();
        Thread.Sleep(TimeSpan.FromSeconds(1));
        proc.Refresh();
        var mb = proc.PrivateMemorySize64 / (1024.0 * 1024.0);
        StopEngine();
        mb.Should().BeLessThan(20);
    }

    private bool WaitForLogAfter(long startBytes, string marker, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(_logPath) && new FileInfo(_logPath).Length > startBytes)
            {
                var tail = ReadTail(_logPath, 3);
                if (tail.Contains(marker, StringComparison.Ordinal)) return true;
            }
            Thread.Sleep(50);
        }
        return false;
    }

    private static string ReadTail(string path, int lines)
    {
        try
        {
            var all = File.ReadAllLines(path);
            var take = Math.Min(lines, all.Length);
            return string.Join(Environment.NewLine, all[^take..]);
        }
        catch
        {
            return string.Empty;
        }
    }

    private void StopEngine()
    {
        foreach (var name in new[] { "SmartGuard.Engine" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }
        }
    }

    public void Dispose() => StopEngine();
}
