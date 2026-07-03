using System.IO;
namespace SmartGuard.Settings.Tests;

public class LogToolbarLayoutTests
{
    [Fact]
    public void Log_page_uses_reader_style_header_instead_of_heavy_toolbar_card()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().NotContain("Text=\"实时日志\"");
        xaml.Should().NotContain("x:Name=\"logToolbarPanel\"");
        xaml.Should().NotContain("x:Name=\"logToolbarDivider\"");
        xaml.Should().Contain("x:Name=\"logTagFilterLinksPanel\"");
        xaml.Should().Contain("x:Name=\"lstLogView\"");
        xaml.Should().Contain("x:Name=\"btnLogRefresh\"");
        xaml.Should().Contain("x:Name=\"btnLogMore\"");
        xaml.Should().Contain("x:Name=\"cmbLogTimeRange\"");
        xaml.Should().Contain("x:Name=\"chkLogSearchCaseSensitive\"");
        xaml.Should().Contain("x:Name=\"chkLogFollowTail\"");
        xaml.Should().Contain("x:Name=\"lblLogStatus\"");
        xaml.Should().NotContain("<ContextMenu");
        xaml.Should().NotContain("<Button.ContextMenu>");
        xaml.Should().Contain("x:Name=\"logMoreFlyout\"");
        xaml.Should().Contain("x:Name=\"logMoreFlyoutLayer\"");
        xaml.Should().Contain("x:Name=\"btnLogMoreCopy\"");
        xaml.Should().Contain("x:Name=\"btnLogMoreExport\"");
        xaml.Should().Contain("x:Name=\"btnLogMoreOpenFolder\"");
        xaml.Should().MatchRegex(
            "x:Name=\"logSearchFilterHost\"[\\s\\S]{0,500}x:Name=\"btnLogRefresh\"[\\s\\S]{0,240}x:Name=\"btnLogMore\"[\\s\\S]{0,500}x:Name=\"logMoreFlyout\"",
            "search should share the top action row with refresh and overflow actions");
        xaml.Should().MatchRegex(
            "x:Name=\"logMoreFlyoutLayer\"[\\s\\S]{0,240}x:Name=\"logMoreFlyout\"",
            "more flyout should live inside a dedicated overlay layer so it can float without affecting header layout");
        var statusIndex = xaml.IndexOf("x:Name=\"lblLogStatus\"", StringComparison.Ordinal);
        var viewportIndex = xaml.IndexOf("x:Name=\"logViewportBorder\"", StringComparison.Ordinal);
        var followTailIndex = xaml.IndexOf("x:Name=\"chkLogFollowTail\"", StringComparison.Ordinal);
        statusIndex.Should().BeGreaterThan(-1);
        viewportIndex.Should().BeGreaterThan(-1);
        followTailIndex.Should().BeGreaterThan(-1);
        statusIndex.Should().BeLessThan(viewportIndex,
            "status summary should sit above the log viewport");
        viewportIndex.Should().BeLessThan(followTailIndex,
            "reader controls should stay below the log viewport");
    }
}
