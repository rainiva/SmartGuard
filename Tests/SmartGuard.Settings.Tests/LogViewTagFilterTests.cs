using SmartGuard.LogViewer;

namespace SmartGuard.Settings.Tests;

public class LogViewTagFilterTests
{
    private readonly string _logPath;
    private readonly string _fallbackPath;

    public LogViewTagFilterTests()
    {
        _logPath = Path.GetTempFileName();
        _fallbackPath = Path.GetTempFileName();
    }

    [Fact]
    public void Default_shows_all_tag_levels_including_heart()
    {
        File.WriteAllText(_logPath,
            "[INFO] 2026-06-21 10:00:00 info\n" +
            "[WARN] 2026-06-21 10:01:00 warn\n" +
            "[ERROR] 2026-06-21 10:02:00 error\n" +
            "[HEART] 2026-06-21 10:03:00 heart\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.ActiveTagFilters.Should().BeEmpty();

        var lines = controller.GetFilteredLines();
        lines.Should().HaveCount(4);
        lines.Should().Contain(line => line.Contains("[HEART]"));
    }

    [Fact]
    public void Active_tag_filters_use_or_logic()
    {
        File.WriteAllText(_logPath,
            "[INFO] 2026-06-21 10:00:00 info\n" +
            "[WARN] 2026-06-21 10:01:00 warn\n" +
            "[ERROR] 2026-06-21 10:02:00 error\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            ActiveTagFilters = ["INFO", "ERROR"],
        };

        var lines = controller.GetFilteredLines();
        lines.Should().HaveCount(2);
        lines.Should().Contain(line => line.Contains("[INFO]"));
        lines.Should().Contain(line => line.Contains("[ERROR]"));
        lines.Should().NotContain(line => line.Contains("[WARN]"));
    }

    [Fact]
    public void Tag_filter_combines_with_text_search()
    {
        File.WriteAllText(_logPath,
            "[INFO] 2026-06-21 10:00:00 alpha\n" +
            "[INFO] 2026-06-21 10:01:00 beta\n" +
            "[WARN] 2026-06-21 10:02:00 beta\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            ActiveTagFilters = ["INFO"],
            SearchKeyword = "beta",
        };

        controller.GetFilteredLines().Should().ContainSingle(line => line.Contains("beta") && line.Contains("[INFO]"));
    }

    [Fact]
    public void Removing_all_tag_filters_restores_all_levels()
    {
        File.WriteAllText(_logPath,
            "[INFO] 2026-06-21 10:00:00 info\n" +
            "[HEART] 2026-06-21 10:01:00 heart\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            ActiveTagFilters = ["INFO"],
        };
        controller.GetFilteredLines().Should().ContainSingle();

        controller.ActiveTagFilters = [];
        controller.GetFilteredLines().Should().HaveCount(2);
    }

    [Fact]
    public void Catalog_exposes_clickable_tags_with_palette_colors()
    {
        foreach (var tag in LogTagFilterCatalog.SelectableTags)
        {
            LogTagFilterCatalog.GetTagColor(tag).Should().Be(LogViewerTagPalette.GetTagColor(tag));
        }
    }
}
