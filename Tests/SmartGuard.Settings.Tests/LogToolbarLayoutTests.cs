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
            "x:Name=\"logSearchFilterHost\"[\\s\\S]{0,500}x:Name=\"btnLogRefresh\"[\\s\\S]{0,240}x:Name=\"btnLogMore\"",
            "search should share the top action row with refresh and overflow actions");
        xaml.Should().MatchRegex(
            "x:Name=\"logMoreFlyoutLayer\"[\\s\\S]{0,120}Grid\\.RowSpan=\"3\"[\\s\\S]{0,240}x:Name=\"logMoreFlyout\"",
            "more flyout should live in a page-level overlay layer so it can float above lower log content without affecting header layout");
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

    [Fact]
    public void Log_page_places_session_time_and_case_scope_in_fixed_top_filter_row()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().Contain("MinWidth=\"600\"");
        xaml.Should().Contain("x:Name=\"logFilterScopePanel\"");
        xaml.Should().Contain("x:Name=\"cmbLogSession\"");

        var searchIndex = RequireIndex(xaml, "x:Name=\"logSearchFilterHost\"");
        var refreshIndex = RequireIndex(xaml, "x:Name=\"btnLogRefresh\"");
        var moreIndex = RequireIndex(xaml, "x:Name=\"btnLogMore\"");
        var tagIndex = RequireIndex(xaml, "x:Name=\"logTagFilterLinksPanel\"");
        var scopeIndex = RequireIndex(xaml, "x:Name=\"logFilterScopePanel\"");
        var sessionIndex = RequireIndex(xaml, "x:Name=\"cmbLogSession\"");
        var timeIndex = RequireIndex(xaml, "x:Name=\"cmbLogTimeRange\"");
        var caseIndex = RequireIndex(xaml, "x:Name=\"chkLogSearchCaseSensitive\"");
        var statusIndex = RequireIndex(xaml, "x:Name=\"lblLogStatus\"");

        searchIndex.Should().BeLessThan(refreshIndex);
        refreshIndex.Should().BeLessThan(moreIndex);
        moreIndex.Should().BeLessThan(tagIndex);
        tagIndex.Should().BeLessThan(scopeIndex);
        scopeIndex.Should().BeLessThan(statusIndex);

        sessionIndex.Should().BeGreaterThan(scopeIndex);
        sessionIndex.Should().BeLessThan(statusIndex);
        timeIndex.Should().BeGreaterThan(scopeIndex);
        timeIndex.Should().BeLessThan(statusIndex);
        caseIndex.Should().BeGreaterThan(scopeIndex);
        caseIndex.Should().BeLessThan(statusIndex);

        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogSession\"[\\s\\S]{0,500}MinWidth=\"1[6-9]0|x:Name=\"cmbLogSession\"[\\s\\S]{0,500}MinWidth=\"2[0-4]0",
            "the session picker should keep a practical lower bound in the wrapping scope row");
        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogSession\"[\\s\\S]{0,500}MaxWidth=\"2[4-9]0|x:Name=\"cmbLogSession\"[\\s\\S]{0,500}MaxWidth=\"3[0-2]0",
            "the session picker should have an upper bound so it cannot crush time and case filters");
        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogSession\"[\\s\\S]{0,500}MaxDropDownHeight=\"280\"",
            "the session dropdown should expose the Task 3 long-list height");
    }

    [Fact]
    public void Diagnostic_details_page_hosts_statistics_without_legacy_session_banner_or_back_controls()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        var logPageIndex = RequireIndex(xaml, "<!-- Logs Page -->");
        var diagnosticPageIndex = RequireIndex(xaml, "<!-- Diagnostic details page");
        xaml.Should().Contain("x:Name=\"logStatisticsInspector\"");
        diagnosticPageIndex.Should().BeGreaterThan(logPageIndex);
        xaml.Substring(logPageIndex, diagnosticPageIndex - logPageIndex)
            .Should().NotContain("x:Name=\"logStatisticsInspector\"");
        xaml.Should().MatchRegex(
            "x:Name=\"logStatisticsInspector\"[\\s\\S]{0,1200}x:Name=\"lblLogStatisticsEmpty\"[\\s\\S]{0,1200}x:Name=\"logErrorTrendChart\"[\\s\\S]{0,1200}x:Name=\"logComponentDistribution\"",
            "diagnostic details should host the statistics controls together");

        xaml.Should().NotContain("x:Name=\"logSessionInspector\"");
        xaml.Should().NotContain("x:Name=\"logSessionBanner\"");
        xaml.Should().NotContain("x:Name=\"lblLogSessionTitle\"");
        xaml.Should().NotContain("x:Name=\"btnLogSessionBack\"");
    }

    [Fact]
    public void Settings_combo_box_template_uses_template_bound_dropdown_height_and_session_item_template()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().MatchRegex(
            "<Style x:Key=\"SettingsComboBox\"[\\s\\S]*?<Setter Property=\"MaxDropDownHeight\" Value=\"240\"/>",
            "shared ComboBox style should keep the existing 240 DIP default");
        xaml.Should().MatchRegex(
            "<Popup[\\s\\S]*?<Border[\\s\\S]*?MaxHeight=\"\\{TemplateBinding MaxDropDownHeight\\}\"",
            "the popup must honor per-control MaxDropDownHeight overrides");
        xaml.Should().NotMatchRegex(
            "<Popup[\\s\\S]{0,800}MaxHeight=\"240\"",
            "hard-coded popup height prevents the session picker from using 280 DIP");

        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogSession\"[\\s\\S]{0,1200}<ComboBox.ItemTemplate>[\\s\\S]{0,800}Text=\"\\{Binding Title\\}\"[\\s\\S]{0,400}TextTrimming=\"CharacterEllipsis\"[\\s\\S]{0,400}ToolTip=\"\\{Binding Title\\}\"",
            "session items should render titles through a template with trimming and full tooltip");
        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogSession\"[\\s\\S]{0,1600}<ComboBox.ItemContainerStyle>[\\s\\S]{0,1000}Property=\"IsEnabled\" Value=\"\\{Binding IsSelectable\\}\"",
            "loading, empty and error session states should be non-selectable through the item container style");
        xaml.Should().NotMatchRegex(
            "x:Name=\"cmbLogSession\"[\\s\\S]{0,600}DisplayMemberPath=",
            "Task 3 should stop relying on DisplayMemberPath so item trimming and tooltip can be expressed in XAML");
    }

    private static int RequireIndex(string text, string value)
    {
        var index = text.IndexOf(value, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"{value} should exist in Settings XAML");
        return index;
    }
}
