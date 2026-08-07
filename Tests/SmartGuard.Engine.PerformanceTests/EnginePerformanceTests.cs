using System.Diagnostics;
using System.Text;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Engine.PerformanceTests;

[Trait("Category", "Performance")]
public class EnginePerformanceTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _engineExe;
    private readonly string _logPath;

    public EnginePerformanceTests()
    {
        _repoRoot = RepoRootResolver.Resolve();
        _engineExe = Path.Combine(_repoRoot, "bin", "SmartGuard.Engine.exe");
        _logPath = SmartGuardPaths.DefaultLogFile(_repoRoot);
    }

    [SkippableFact]
    public void Startup_should_complete_within_budget()
    {
        Skip.IfNot(File.Exists(_engineExe), "Engine exe not published. Run build.cmd first.");
        StopEngine();
        Thread.Sleep(TimeSpan.FromMilliseconds(300));
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
        sw.ElapsedMilliseconds.Should().BeLessThan(5000);
    }

    [Fact]
    public void Appended_log_scan_keeps_marker_when_many_lines_follow()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "existing line" + Environment.NewLine);
            var startBytes = new FileInfo(path).Length;
            File.AppendAllText(
                path,
                "startup-marker" + Environment.NewLine +
                string.Join(Environment.NewLine, Enumerable.Range(1, 8).Select(i => $"later line {i}")) +
                Environment.NewLine);

            ReadAppendedText(path, startBytes).Should().Contain("startup-marker");
        }
        finally
        {
            File.Delete(path);
        }
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
        if (proc!.HasExited)
            Skip.If(true, "Engine exited before memory snapshot; ensure no conflicting SmartGuard instance is running.");
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
            if (File.Exists(_logPath))
            {
                var appended = ReadAppendedText(_logPath, startBytes);
                if (appended.Contains(marker, StringComparison.Ordinal)) return true;
            }
            Thread.Sleep(50);
        }
        return false;
    }

    private static string ReadAppendedText(string path, long startBytes)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(startBytes <= stream.Length ? startBytes : 0, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void StopEngine() => PerformanceTestEngineLifecycle.Stop(_repoRoot);

    public void Dispose() => StopEngine();
}
