using System.IO;
using System.Reflection;

namespace SmartGuard.Settings.Tests;

public class LogToolbarLayoutTests
{
    private static string RepoXamlPath()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml");
    }

    [Fact]
    public void Log_toolbar_uses_wrap_layout_for_filters_to_avoid_overlap()
    {
        var xaml = File.ReadAllText(RepoXamlPath());

        xaml.Should().Contain("x:Name=\"logToolbarPanel\"");
        xaml.Should().Contain("x:Name=\"logToolbarDivider\"");
        xaml.Should().Contain("LogToolbarCompactButton");
        xaml.Should().Contain("x:Name=\"logFilterLevelPanel\"");
        xaml.Should().Contain("x:Name=\"logFilterTimePanel\"");
        xaml.Should().NotMatchRegex(
            "logToolbarPanel[\\s\\S]{0,1200}<Grid>[\\s\\S]{0,400}Grid\\.Column=\"1\"[\\s\\S]{0,200}chkError",
            "level filters and time filters should not share one cramped grid row");
    }
}
