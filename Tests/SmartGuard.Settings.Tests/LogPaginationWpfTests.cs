using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogPaginationWpfTests
{
    [Fact]
    public void Log_page_visual_order_matches_toolbar_table_pager()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();
        var logPage = SliceLogPage(xaml);

        IndexOf(logPage, "x:Name=\"logToolbar\"").Should().BeLessThan(IndexOf(logPage, "x:Name=\"logTable\""));
        IndexOf(logPage, "x:Name=\"logTable\"").Should().BeLessThan(IndexOf(logPage, "x:Name=\"lblLogTotalCount\""));
        IndexOf(logPage, "x:Name=\"lblLogTotalCount\"").Should().BeLessThan(IndexOf(logPage, "x:Name=\"btnLogFirstPage\""));
    }

    [Fact]
    public void Pager_page_one_of_sixty_three_matches_reference_tokens()
    {
        var xaml = SliceLogPage(SettingsSourcePaths.ReadSettingsXamlSource());
        var tokens = LogPagerTokenBuilder.Build(currentPage: 1, totalPages: 63);

        tokens.Select(RenderToken).Should().Equal("1", "2", "3", "4", "5", "…", "63");
        xaml.Should().Contain("x:Name=\"logPageNumberItems\"");
        xaml.Should().MatchRegex(
            "x:Name=\"logPageNumberItems\"[\\s\\S]{0,1600}ItemsPanel",
            "page numbers should be rendered by a dedicated horizontal items panel");
    }

    [Fact]
    public void Boundary_buttons_disable_on_first_and_last_page()
    {
        var firstPage = LogPaginationState.Initial.ApplyExactCount(1248, isScanComplete: true);
        firstPage.CanGoFirst.Should().BeFalse();
        firstPage.CanGoPrevious.Should().BeFalse();
        firstPage.CanGoNext.Should().BeTrue();
        firstPage.CanGoLast.Should().BeTrue();

        var lastPage = firstPage.StartNavigation(firstPage.TotalPages).ApplyExactCount(1248, isScanComplete: true);
        lastPage.CanGoFirst.Should().BeTrue();
        lastPage.CanGoPrevious.Should().BeTrue();
        lastPage.CanGoNext.Should().BeFalse();
        lastPage.CanGoLast.Should().BeFalse();

        var xaml = SliceLogPage(SettingsSourcePaths.ReadSettingsXamlSource());
        xaml.Should().MatchRegex(
            "btnLogFirstPage[\\s\\S]{0,800}Command=|Command=\\{x:Static local:LogPaginationCommands.FirstPage\\}[\\s\\S]{0,800}btnLogFirstPage",
            "boundary buttons must be wired to pagination commands");
    }

    [Fact]
    public void Pagination_commands_use_state_for_boundary_and_page_can_execute()
    {
        var state = LogPaginationState.Initial.ApplyExactCount(1248, isScanComplete: true);

        LogPaginationCommands.CanExecute(LogPaginationCommands.FirstPage, state).Should().BeFalse();
        LogPaginationCommands.CanExecute(LogPaginationCommands.PreviousPage, state).Should().BeFalse();
        LogPaginationCommands.CanExecute(LogPaginationCommands.NextPage, state).Should().BeTrue();
        LogPaginationCommands.CanExecute(LogPaginationCommands.LastPage, state).Should().BeTrue();
        LogPaginationCommands.CanExecute(LogPaginationCommands.GoToPage, state, 2).Should().BeTrue();
        LogPaginationCommands.CanExecute(LogPaginationCommands.GoToPage, state, 1).Should().BeFalse();
        LogPaginationCommands.TryGetPageNumber("63", out var pageNumber).Should().BeTrue();
        pageNumber.Should().Be(63);
    }

    [Fact]
    public void Pagination_commands_are_bound_with_state_driven_can_execute()
    {
        var wiring = ReadSource("src/SmartGuard.Settings/SettingsLogPageHostWiring.cs");
        wiring.Should().Contain("WirePaginationCommands");
        wiring.Should().Contain("window.CommandBindings.Add");
        wiring.Should().Contain("LogPaginationCommands.CanExecute");
        ReadSource("src/SmartGuard.Settings/SettingsLogPageHost.Refresh.cs")
            .Should().Contain("GoToPageAsync");
    }

    [Fact]
    public void Ellipsis_is_not_focusable_or_clickable()
    {
        var xaml = SliceLogPage(SettingsSourcePaths.ReadSettingsXamlSource());
        var pagerIndex = IndexOf(xaml, "x:Name=\"logPageNumberItems\"");
        var pager = xaml[pagerIndex..];

        pager.Should().MatchRegex("IsHitTestVisible=\"False\"[\\s\\S]{0,500}Focusable=\"False\"");
    }

    [Fact]
    public void DataGrid_time_sort_is_server_side_and_resets_page_one()
    {
        var xaml = SliceLogPage(SettingsSourcePaths.ReadSettingsXamlSource());
        xaml.Should().Contain("x:Name=\"logTable\"");
        xaml.Should().NotContain("Text=\"↕\"");
        xaml.Should().MatchRegex("Text=\"时间\"[\\s\\S]{0,700}<Path[\\s\\S]{0,700}<Path");

        var wiring = ReadSource("src/SmartGuard.Settings/SettingsLogPageHostWiring.cs");
        wiring.Should().Contain("logTable.Sorting");
        wiring.Should().Contain("toggleSortAsync");
        wiring.Should().Contain("e.Handled = true");
    }

    [Fact]
    public void Log_table_uses_shared_scrollbar_level_colors_and_aligned_column_separators()
    {
        var source = SettingsSourcePaths.ReadSettingsXamlSource();
        var xaml = SliceLogPage(source);

        source.Should().Contain("x:Key=\"LogTableLevelTextStyle\"");
        xaml.Should().Contain("ElementStyle=\"{StaticResource LogTableLevelTextStyle}\"");
        source.Should().Contain("#1A6FBF");
        source.Should().Contain("#C47A00");
        source.Should().Contain("#C62828");
        source.Should().Contain("#5F6B7A");
        source.Should().MatchRegex(
            "x:Key=\"LogTableHeaderStyle\"[\\s\\S]{0,500}Property=\"BorderThickness\" Value=\"0,0,1,1\"");
        source.Should().MatchRegex(
            "x:Key=\"LogTableCellStyle\"[\\s\\S]{0,500}Property=\"Padding\" Value=\"16,0\"");
        xaml.Should().MatchRegex(
            "x:Name=\"logTable\"[\\s\\S]{0,2400}<DataGrid.Resources>[\\s\\S]{0,500}SettingsScrollBar");
    }

    [Fact]
    public void Page_size_combo_contains_20_50_100_and_defaults_20()
    {
        var xaml = SliceLogPage(SettingsSourcePaths.ReadSettingsXamlSource());
        var comboIndex = IndexOf(xaml, "x:Name=\"cmbLogPageSize\"");
        var combo = xaml[comboIndex..];

        combo.Should().MatchRegex("SelectedIndex=\"0\"");
        combo.Should().MatchRegex("Content=\"20\"[\\s\\S]{0,1200}Content=\"50\"[\\s\\S]{0,1200}Content=\"100\"");
    }

    [Fact]
    public void Narrow_window_keeps_table_scrollable_without_overlapping_pager()
    {
        var xaml = SliceLogPage(SettingsSourcePaths.ReadSettingsXamlSource());
        xaml.Should().Contain("x:Name=\"logTable\"");
        xaml.Should().Contain("ScrollViewer.HorizontalScrollBarVisibility=\"Auto\"");
        IndexOf(xaml, "x:Name=\"logViewportBorder\"").Should().BeLessThan(IndexOf(xaml, "x:Name=\"logPager\""));
        IndexOf(xaml, "x:Name=\"logPager\"").Should().BeLessThan(IndexOf(xaml, "x:Name=\"btnLogFirstPage\""));
    }

    [Fact]
    public void Wrap_off_uses_fixed_44_row_height_and_wrap_on_uses_bounded_auto_height()
    {
        var source = SettingsSourcePaths.ReadSettingsXamlSource();
        var xaml = SliceLogPage(source);
        source.Should().Contain("x:Key=\"LogTableRowStyle\"");
        source.Should().MatchRegex("x:Key=\"LogTableRowStyle\"[\\s\\S]{0,500}Property=\"Height\" Value=\"44\"");
        xaml.Should().Contain("x:Name=\"chkLogWrap\"");
        xaml.Should().MatchRegex("x:Name=\"chkLogWrap\"[\\s\\S]{0,700}Content=\"自动换行\"");
        source.Should().Contain("Property=\"MaxHeight\" Value=\"88\"");
    }

    [Fact]
    public void Table_level_tokens_sort_glyph_and_current_page_style_match_the_compact_design()
    {
        var source = SettingsSourcePaths.ReadSettingsXamlSource();
        var xaml = SliceLogPage(source);
        var refreshSource = ReadSource("src/SmartGuard.Settings/SettingsLogPageHost.Refresh.cs");

        source.Should().Contain("Trigger Property=\"Text\" Value=\"WARN\"");
        source.Should().Contain("Trigger Property=\"Text\" Value=\"ERROR\"");
        source.Should().Contain("Trigger Property=\"Text\" Value=\"FATAL\"");
        source.Should().NotContain("Trigger Property=\"Text\" Value=\"警告\"");
        xaml.Should().NotContain("Text=\"↕\"");
        xaml.Should().MatchRegex("Text=\"时间\"[\\s\\S]{0,700}<Path[\\s\\S]{0,700}<Path");
        source.Should().Contain("x:Key=\"LogPagerCurrentButton\"");
        refreshSource.Should().Contain("number.IsCurrent ? currentButtonStyle : buttonStyle");
    }

    [Fact]
    public void Log_card_uses_page_specific_breathing_room()
    {
        var xaml = SliceLogPage(SettingsSourcePaths.ReadSettingsXamlSource());

        xaml.Should().MatchRegex(
            "x:Name=\"pageLogs\"[\\s\\S]{0,300}<Border[\\s\\S]{0,200}Padding=\"24,20\"");
    }

    [Fact]
    public void Wrap_toggle_switches_between_fixed_and_bounded_auto_row_height()
    {
        WpfStaTestHost.Run(() =>
        {
            var table = new DataGrid { RowHeight = 44 };
            var wrapToggle = new CheckBox();

            SettingsLogPageHostWiring.WireLogTableWrapMode(table, wrapToggle);

            wrapToggle.IsChecked = true;
            double.IsNaN(table.RowHeight).Should().BeTrue();

            wrapToggle.IsChecked = false;
            table.RowHeight.Should().Be(44);
        });
    }

    [Fact]
    public void Native_date_and_time_inputs_update_the_existing_canonical_range_text()
    {
        WpfStaTestHost.Run(() =>
        {
            var date = new DatePicker();
            var time = new TextBox { Text = "08:30:15" };
            var canonical = new TextBox();

            SettingsLogPageHostWiring.WireLogDateTimePicker(date, time, canonical);
            date.SelectedDate = new DateTime(2026, 8, 9);

            canonical.Text.Should().Be("2026-08-09 08:30:15");

            time.Text = "19:45";
            canonical.Text.Should().Be("2026-08-09 19:45:00");
        });
    }

    [Fact]
    public void Wrap_mode_is_applied_to_cells_created_after_page_switch()
    {
        WpfStaTestHost.Run(() =>
        {
            var cellTextStyle = new Style(typeof(TextBlock));
            cellTextStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
            var table = new DataGrid
            {
                AutoGenerateColumns = false,
                Height = 160,
                Width = 480,
            };
            table.Columns.Add(new DataGridTextColumn
            {
                Binding = new Binding(nameof(WrapRow.Text)),
                ElementStyle = cellTextStyle,
            });
            var wrapToggle = new CheckBox();
            SettingsLogPageHostWiring.WireLogTableWrapMode(table, wrapToggle);
            var window = new Window { Content = table, Width = 520, Height = 220 };

            try
            {
                table.ItemsSource = new[] { new WrapRow("page one") };
                WpfStaTestHost.ShowAndWait(window);
                wrapToggle.IsChecked = true;
                WpfStaTestHost.PumpDispatcher(window);

                table.ItemsSource = new[]
                {
                    new WrapRow("page two row one with enough text to require wrapping"),
                    new WrapRow("page two row two with enough text to require wrapping"),
                };
                WpfStaTestHost.PumpDispatcher(window);

                FindVisualChildren<TextBlock>(table)
                    .Where(textBlock => textBlock.DataContext is WrapRow)
                    .Should().NotBeEmpty()
                    .And.OnlyContain(textBlock => textBlock.TextWrapping == TextWrapping.Wrap);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static string SliceLogPage(string xaml)
    {
        var start = xaml.IndexOf("<!-- Logs Page -->", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        return xaml[start..];
    }

    private static int IndexOf(string text, string value)
    {
        var index = text.IndexOf(value, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"{value} should exist in the log page");
        return index;
    }

    private static string ReadSource(string relativePath)
        => File.ReadAllText(Path.Combine(SettingsSourcePaths.RepoRoot(), relativePath), Encoding.UTF8);

    private static string RenderToken(LogPagerToken token)
        => token switch
        {
            LogPageNumberToken number => number.PageNumber.ToString(),
            LogPageEllipsisToken => "…",
            _ => throw new InvalidOperationException($"Unknown token: {token.GetType().Name}"),
        };

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private sealed record WrapRow(string Text);
}
