using System.IO;
using System.Reflection;

namespace SmartGuard.Settings.Tests;

public class SettingsTypographyTests
{
    private static string RepoXamlPath()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml");
    }

    [Fact]
    public void Settings_xaml_defines_shared_description_typography_styles()
    {
        var xaml = File.ReadAllText(RepoXamlPath());

        xaml.Should().Contain("x:Key=\"SettingsGroupTitle\"");
        xaml.Should().Contain("x:Key=\"SettingsCardDescription\"");
        xaml.Should().Contain("x:Key=\"SettingsItemTitle\"");
        xaml.Should().Contain("x:Key=\"SettingsItemHint\"");
    }

    [Fact]
    public void Settings_xaml_uses_clear_type_text_rendering_on_window()
    {
        var xaml = File.ReadAllText(RepoXamlPath());
        xaml.Should().Contain("TextOptions.TextRenderingMode=\"ClearType\"");
    }

    [Fact]
    public void Settings_item_hints_no_longer_use_11px_secondary_text()
    {
        var xaml = File.ReadAllText(RepoXamlPath());
        xaml.Should().NotContain("FontSize=\"11\" Foreground=\"{DynamicResource TextSecondary}\"");
    }

    [Fact]
    public void Settings_card_description_sample_uses_shared_style()
    {
        var xaml = File.ReadAllText(RepoXamlPath());
        xaml.Should().MatchRegex(
            "Text=\"调整引擎轮询频率、心跳日志和亮度恢复延迟\"[\\s\\S]{0,120}Style=\"\\{StaticResource SettingsCardDescription\\}\"",
            "card descriptions should use the shared typography style");
    }

    [Fact]
    public void SettingsTypographyMetrics_item_hint_uses_minimum_12px()
    {
        SettingsTypographyMetrics.ItemHintFontSize.Should().BeGreaterThanOrEqualTo(12);
    }
}
