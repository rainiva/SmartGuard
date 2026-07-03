using System.IO;
using System.Windows;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsXamlLoaderTests
{
    private static void EnsureApplication() => WpfStaTestHost.EnsureApplication();

    [Fact]
    public void Project_settings_xaml_lives_inside_settings_project()
    {
        var xamlPath = SettingsSourcePaths.ProjectSettingsXamlPath();
        File.Exists(xamlPath).Should().BeTrue();
        File.ReadAllText(xamlPath).Should().Contain("x:Name=\"navList\"");
    }

    [Fact]
    public void TryLoadEmbeddedWindow_loads_compiled_settings_xaml()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var window = SettingsXamlLoader.TryLoadEmbeddedWindow(out var error);
            window.Should().NotBeNull();
            window!.FindName("navList").Should().NotBeNull();
            error.Should().BeNull();
        });
    }

    [Fact]
    public void SettingsXamlLoader_does_not_expose_loose_runtime_loading_paths()
    {
        var source = File.ReadAllText(Path.Combine(
            SettingsSourcePaths.RepoRoot(),
            "src",
            "SmartGuard.Settings",
            "SettingsXamlLoader.cs"));
        source.Should().NotContain("TryLoadLooseWindowFromFile");
        source.Should().NotContain("LoadLooseWindow(");
        source.Should().NotContain("PrepareLooseXamlForParse");
        source.Should().NotContain("File.ReadAllText");
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);
}
