using SmartGuard.LogViewer;

namespace SmartGuard.Settings.Tests;

public class LogViewControllerTests : IDisposable
{
    private readonly string _logPath;
    private readonly string _fallbackPath;

    public LogViewControllerTests()
    {
        _logPath = Path.GetTempFileName();
        _fallbackPath = Path.GetTempFileName();
    }

    public void Dispose()
    {
        try { File.Delete(_logPath); } catch { }
        try { File.Delete(_fallbackPath); } catch { }
    }

    [Fact]
    public void LogViewController_reads_and_formats_log_lines()
    {
        File.WriteAllText(_logPath, "2026-06-19 10:00:00 system started\n2026-06-19 10:01:00 ERROR: disk full\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        var lines = controller.GetFilteredLines();

        lines.Should().HaveCount(2);
        lines[0].Should().Be("[INFO] 2026-06-19 10:00:00 system started");
        lines[1].Should().Be("[ERROR] 2026-06-19 10:01:00 ERROR: disk full");
    }

    [Fact]
    public void LogViewController_filters_by_keyword()
    {
        File.WriteAllText(_logPath, "2026-06-19 10:00:00 system started\n2026-06-19 10:01:00 ERROR: disk full\n2026-06-19 10:02:00 WARN: memory low\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.SearchKeyword = "disk";
        var lines = controller.GetFilteredLines();

        lines.Should().HaveCount(1);
        lines[0].Should().Contain("disk full");
    }

    [Fact]
    public void LogViewController_filters_by_tag()
    {
        File.WriteAllText(_logPath, "2026-06-19 10:00:00 system started\n2026-06-19 10:01:00 ERROR: disk full\n2026-06-19 10:02:00 WARN: memory low\n2026-06-19 10:03:00 [监控中] monitoring\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.ShowInfo = false;
        controller.ShowHeart = false;
        var lines = controller.GetFilteredLines();

        lines.Should().HaveCount(2);
        lines.Should().Contain(line => line.Contains("ERROR"));
        lines.Should().Contain(line => line.Contains("WARN"));
        lines.Should().NotContain(line => line.Contains("[INFO]"));
        lines.Should().NotContain(line => line.Contains("[HEART]"));
    }
}
