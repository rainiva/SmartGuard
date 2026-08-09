using System.IO;
namespace SmartGuard.Settings.Tests;

public class LogToolbarLayoutTests
{
    [Fact]
    public void Log_page_exposes_reference_table_toolbar_and_pager_controls()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        foreach (var controlName in new[]
        {
            "logToolbar",
            "cmbLogTimeRange",
            "txtLogRangeStart",
            "txtLogRangeEnd",
            "btnLogRefresh",
            "btnLogFilter",
            "btnLogExport",
            "logSearchFilterHost",
            "logTable",
            "lblLogTotalCount",
            "cmbLogPageSize",
            "btnLogFirstPage",
            "btnLogPreviousPage",
            "logPageNumberItems",
            "btnLogNextPage",
            "btnLogLastPage",
        })
        {
            xaml.Should().Contain($"x:Name=\"{controlName}\"");
        }
    }

    [Fact]
    public void Log_page_uses_reader_style_header_instead_of_heavy_toolbar_card()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().NotContain("Text=\"实时日志\"");
        xaml.Should().NotContain("x:Name=\"logToolbarPanel\"");
        xaml.Should().NotContain("x:Name=\"logToolbarDivider\"");
        xaml.Should().NotContain("x:Name=\"logTagFilterLinksPanel\"");
        xaml.Should().Contain("x:Name=\"lstLogView\"");
        xaml.Should().Contain("x:Name=\"btnLogRefresh\"");
        xaml.Should().Contain("x:Name=\"btnLogMore\"");
        xaml.Should().Contain("x:Name=\"cmbLogTimeRange\"");
        xaml.Should().NotContain("x:Name=\"chkLogSearchCaseSensitive\"");
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
            "x:Name=\"logSearchFilterHost\"[\\s\\S]{0,500}x:Name=\"btnLogRefresh\"[\\s\\S]{0,600}x:Name=\"btnLogFilter\"[\\s\\S]{0,300}x:Name=\"btnLogExport\"",
            "search should share the top action row with refresh, filter, and export actions");
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
    public void Log_page_moves_time_level_and_source_into_bounded_filter_popup()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();
        var refreshSource = File.ReadAllText(
            Path.Combine(SettingsSourcePaths.RepoRoot(), "src", "SmartGuard.Settings", "SettingsLogPageHost.Refresh.cs"),
            System.Text.Encoding.UTF8);

        xaml.Should().Contain("MinWidth=\"600\"");
        xaml.Should().Contain("x:Name=\"logFilterPopup\"");
        xaml.Should().Contain("x:Name=\"logFilterPanel\"");
        xaml.Should().Contain("x:Name=\"cmbLogLevel\"");
        xaml.Should().Contain("x:Name=\"cmbLogSource\"");
        xaml.Should().NotContain("x:Name=\"logFilterScopePanel\"");
        xaml.Should().NotContain("x:Name=\"logTagFilterLinksPanel\"");
        xaml.Should().Contain("x:Name=\"cmbLogSession\"");
        xaml.Should().NotContain("x:Name=\"chkLogSearchCaseSensitive\"");

        var searchIndex = RequireIndex(xaml, "x:Name=\"logSearchFilterHost\"");
        var refreshIndex = RequireIndex(xaml, "x:Name=\"btnLogRefresh\"");
        var moreIndex = RequireIndex(xaml, "x:Name=\"btnLogMore\"");
        var filterPopupIndex = RequireIndex(xaml, "x:Name=\"logFilterPopup\"");
        var filterPanelIndex = RequireIndex(xaml, "x:Name=\"logFilterPanel\"");
        var levelIndex = RequireIndex(xaml, "x:Name=\"cmbLogLevel\"");
        var sourceIndex = RequireIndex(xaml, "x:Name=\"cmbLogSource\"");
        var sessionIndex = RequireIndex(xaml, "x:Name=\"cmbLogSession\"");
        var timeIndex = RequireIndex(xaml, "x:Name=\"cmbLogTimeRange\"");
        var statusIndex = RequireIndex(xaml, "x:Name=\"lblLogStatus\"");

        searchIndex.Should().BeLessThan(refreshIndex);
        refreshIndex.Should().BeLessThan(moreIndex);
        moreIndex.Should().BeLessThan(filterPopupIndex);
        filterPopupIndex.Should().BeLessThan(filterPanelIndex);
        filterPanelIndex.Should().BeLessThan(levelIndex);
        filterPanelIndex.Should().BeLessThan(timeIndex);
        timeIndex.Should().BeLessThan(levelIndex);
        levelIndex.Should().BeLessThan(sourceIndex);
        sourceIndex.Should().BeLessThan(statusIndex);

        sessionIndex.Should().BeGreaterThan(sourceIndex);
        sessionIndex.Should().BeLessThan(statusIndex);
        timeIndex.Should().BeLessThan(statusIndex);

        xaml.Should().MatchRegex(
            "x:Name=\"logFilterPopup\"[\\s\\S]{0,500}PlacementTarget=\"\\{Binding ElementName=logToolbar\\}\"",
            "the filter surface should be anchored to the toolbar so its left edge stays inside the log card");
        xaml.Should().NotContain("HorizontalOffset=\"-320\"");
        xaml.Should().MatchRegex(
            "x:Name=\"logFilterPanel\"[\\s\\S]{0,500}Width=\"\\{Binding ActualWidth, ElementName=logToolbar\\}\"[\\s\\S]{0,500}MaxWidth=\"420\"",
            "the filter surface should shrink with the toolbar instead of overflowing narrow windows");
        refreshSource.Should().Contain("popup.IsOpen = !popup.IsOpen");

        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogSession\"[\\s\\S]{0,300}Visibility=\"Collapsed\"",
            "session selection remains available to internal state restoration but is removed from the visible filter surface");
        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogLevel\"[\\s\\S]{0,500}Style=\"\\{StaticResource SettingsComboBox\\}\"");
        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogSource\"[\\s\\S]{0,500}Style=\"\\{StaticResource SettingsComboBox\\}\"");
    }

    [Fact]
    public void Log_toolbar_search_actions_and_wrap_toggle_share_the_time_range_height()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        foreach (var controlName in new[]
                 {
                     "logSearchBox",
                     "btnLogRefresh",
                     "btnLogFilter",
                     "btnLogExport",
                     "chkLogWrap",
                 })
        {
            xaml.Should().MatchRegex(
                $"x:Name=\"{controlName}\"[\\s\\S]{{0,240}}Height=\"36\"|Height=\"36\"[\\s\\S]{{0,240}}x:Name=\"{controlName}\"",
                $"{controlName} should use the shared 36 DIP toolbar height");
        }

        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogTimeRange\"[\\s\\S]{0,240}Height=\"36\"|Height=\"36\"[\\s\\S]{0,240}x:Name=\"cmbLogTimeRange\"");
        xaml.Should().Contain("x:Name=\"dateLogRangeStart\"");
        xaml.Should().Contain("x:Name=\"dateLogRangeEnd\"");
        xaml.Should().Contain("x:Name=\"txtLogRangeStartTime\"");
        xaml.Should().Contain("x:Name=\"txtLogRangeEndTime\"");
        xaml.Should().MatchRegex(
            "x:Name=\"panelLogCustomRange\"[\\s\\S]{0,1600}<DatePicker[\\s\\S]{0,1600}<DatePicker");
    }

    [Fact]
    public void About_maintenance_hosts_diagnostic_statistics_without_a_standalone_page()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        var aboutPageIndex = RequireIndex(xaml, "<!-- About Page -->");
        var logPageIndex = RequireIndex(xaml, "<!-- Logs Page -->");
        var diagnosticInspectorIndex = RequireIndex(xaml, "x:Name=\"logStatisticsInspector\"");
        diagnosticInspectorIndex.Should().BeGreaterThan(aboutPageIndex);
        diagnosticInspectorIndex.Should().BeLessThan(logPageIndex);
        xaml.Should().NotContain("<!-- Diagnostic details page");
        xaml.Substring(logPageIndex).Should().NotContain("x:Name=\"logStatisticsInspector\"");
        xaml.Should().MatchRegex(
            "x:Name=\"logStatisticsInspector\"[\\s\\S]{0,1200}x:Name=\"lblLogStatisticsEmpty\"[\\s\\S]{0,1200}x:Name=\"logErrorTrendChart\"[\\s\\S]{0,1200}x:Name=\"logComponentDistribution\"",
            "About maintenance should host the diagnostic statistics controls together");
        xaml.Should().Contain("基于保留期内全部日志统计错误趋势和组件分布");
        xaml.Should().NotContain("按日志页当前筛选条件查看错误趋势和组件分布");

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
