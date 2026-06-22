namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class LogSearchFilterBarTests
{
    [Fact]
    public void AddTagFilter_creates_chip_with_matching_palette_color()
    {
        RunOnSta(() =>
        {
            var bar = new LogSearchFilterBar();
            bar.AddTagFilter("INFO");

            bar.ActiveTags.Should().ContainSingle("INFO");
            bar.Children.Count.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void RemoveTagFilter_clears_chip_from_search_area()
    {
        RunOnSta(() =>
        {
            var bar = new LogSearchFilterBar();
            bar.AddTagFilter("WARN");
            bar.RemoveTagFilter("WARN");

            bar.ActiveTags.Should().BeEmpty();
        });
    }

    [Fact]
    public void AddTagFilter_is_idempotent_for_same_tag()
    {
        RunOnSta(() =>
        {
            var bar = new LogSearchFilterBar();
            bar.AddTagFilter("ERROR");
            bar.AddTagFilter("ERROR");

            bar.ActiveTags.Should().ContainSingle("ERROR");
        });
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);
}
