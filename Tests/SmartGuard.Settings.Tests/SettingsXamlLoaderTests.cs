using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class SettingsXamlLoaderTests
{
    private static string RepoXamlPath()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml");
    }

    private static void EnsureApplication() => WpfStaTestHost.EnsureApplication();

    [Fact]
    public void PrepareLooseXamlForParse_qualifies_local_assembly()
    {
        const string raw = "<Window xmlns:local=\"clr-namespace:SmartGuard.Settings\" />";
        SettingsXamlLoader.PrepareLooseXamlForParse(raw)
            .Should().Contain("assembly=SmartGuard.Settings");
    }

    [Fact]
    public void LoadLooseWindow_parses_committed_settings_xaml()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var xamlPath = RepoXamlPath();
            File.Exists(xamlPath).Should().BeTrue();

            var window = SettingsXamlLoader.LoadLooseWindow(File.ReadAllText(xamlPath));
            window.Should().NotBeNull();
            window!.FindName("navList").Should().NotBeNull();
        });
    }

    [Fact]
    public void LoadLooseWindow_requires_assembly_qualifier_for_local_controls()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            const string raw = """
                <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                        xmlns:local="clr-namespace:SmartGuard.Settings">
                  <local:NumberBox Minimum="0" Maximum="10"/>
                </Window>
                """;

            var act = () => XamlReader.Parse(raw);
            act.Should().Throw<Exception>();

            var fixedWindow = SettingsXamlLoader.LoadLooseWindow(raw);
            fixedWindow.Should().NotBeNull();
            fixedWindow!.Content.Should().BeOfType<NumberBox>();
        });
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);
}
