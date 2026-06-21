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

    [Fact]
    public void RefreshFromDisk_picks_up_appended_lines()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 first line\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.GetFilteredLines().Should().HaveCount(1);

        File.AppendAllText(_logPath, "[INFO] 2026-06-19 10:01:00 second line\n");
        controller.RefreshFromDisk();

        var lines = controller.GetFilteredLines();
        lines.Should().HaveCount(2);
        lines[1].Should().Contain("second line");
    }

    [Fact]
    public void GetSnapshot_reports_tail_truncation_when_log_exceeds_tail_limit()
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < 4000; i++)
            builder.AppendLine($"[INFO] 2026-06-19 10:00:{i % 60:D2} line {i} {new string('x', 80)}");

        File.WriteAllText(_logPath, builder.ToString());

        var controller = new LogViewController(_logPath, _fallbackPath);
        var snapshot = controller.GetSnapshot();

        snapshot.IsTailTruncated.Should().BeTrue();
        snapshot.TotalLineCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RefreshFromDisk_marks_content_changed_only_when_file_grows()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 first line\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.RefreshFromDisk();
        controller.GetSnapshot().ContentChanged.Should().BeFalse();

        File.AppendAllText(_logPath, "[INFO] 2026-06-19 10:01:00 second line\n");
        controller.RefreshFromDisk();
        controller.GetSnapshot().ContentChanged.Should().BeTrue();
    }

    [Fact]
    public void GetSnapshot_reports_log_path_for_status_bar()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 ok\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.GetSnapshot().LogPath.Should().Be(_logPath);
    }

    [Fact]
    public void GetFilteredLines_includes_unparsed_lines_with_raw_prefix()
    {
        File.WriteAllText(_logPath, "plain unstructured line\n[INFO] 2026-06-19 10:00:00 ok\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        var lines = controller.GetFilteredLines();

        lines.Should().Contain("[RAW] plain unstructured line");
        lines.Should().Contain("[INFO] 2026-06-19 10:00:00 ok");
    }

    [Fact]
    public void GetSnapshot_total_line_count_includes_raw_lines()
    {
        File.WriteAllText(_logPath, "plain unstructured line\n[INFO] 2026-06-19 10:00:00 ok\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.GetSnapshot().TotalLineCount.Should().Be(2);
    }

    [Fact]
    public void GetSnapshot_reports_empty_state_when_all_level_filters_are_off()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 ok\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.ShowInfo = false;
        controller.ShowWarn = false;
        controller.ShowError = false;
        controller.ShowHeart = false;

        controller.GetSnapshot().EmptyStateMessage.Should().Be("请至少选择一种日志级别");
    }

    [Fact]
    public void GetSnapshot_reports_empty_state_when_search_has_no_matches()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 alpha\n[WARN] 2026-06-19 10:01:00 beta\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.SearchKeyword = "missing-term";

        controller.GetSnapshot().EmptyStateMessage.Should().Be("无匹配结果");
    }

    [Fact]
    public void GetSnapshot_has_no_empty_state_when_search_matches()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 alpha\n[WARN] 2026-06-19 10:01:00 beta\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.SearchKeyword = "beta";

        var snapshot = controller.GetSnapshot();
        snapshot.EmptyStateMessage.Should().BeNull();
        snapshot.FilteredLines.Should().HaveCount(1);
    }

    [Fact]
    public void GetFilteredLines_limits_to_today()
    {
        File.WriteAllText(_logPath,
            "[INFO] 2026-06-21 09:00:00 today line\n" +
            "[INFO] 2026-06-20 23:00:00 yesterday line\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            TimeRange = LogViewTimeRange.Today,
            NowProvider = () => new DateTime(2026, 6, 21, 10, 0, 0),
        };

        controller.GetFilteredLines().Should().ContainSingle(line => line.Contains("today line"));
    }

    [Fact]
    public void GetFilteredLines_limits_to_custom_range()
    {
        File.WriteAllText(_logPath,
            "[INFO] 2026-06-21 10:00:00 in range\n" +
            "[INFO] 2026-06-21 12:00:00 out of range\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            TimeRange = LogViewTimeRange.Custom,
            CustomRangeStart = new DateTime(2026, 6, 21, 9, 0, 0),
            CustomRangeEnd = new DateTime(2026, 6, 21, 11, 0, 0),
        };

        controller.GetFilteredLines().Should().ContainSingle(line => line.Contains("in range"));
    }

    [Fact]
    public void GetSnapshot_reports_empty_state_when_time_range_excludes_all_lines()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-20 10:00:00 yesterday\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            TimeRange = LogViewTimeRange.Today,
            NowProvider = () => new DateTime(2026, 6, 21, 10, 0, 0),
        };

        controller.GetSnapshot().EmptyStateMessage.Should().Be("无匹配结果");
    }

    [Fact]
    public void GetFilteredLines_supports_case_sensitive_search()
    {
        File.WriteAllText(_logPath,
            "[INFO] 2026-06-21 10:00:00 Battery low\n" +
            "[INFO] 2026-06-21 10:01:00 battery low\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            SearchKeyword = "Battery",
            SearchCaseSensitive = true,
        };

        controller.GetFilteredLines().Should().ContainSingle(line => line.Contains("Battery"));
    }
}
