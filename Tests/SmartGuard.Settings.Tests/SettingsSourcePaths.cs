using System.IO;

namespace SmartGuard.Settings.Tests;

internal static class SettingsSourcePaths
{
    internal static string RepoRoot()
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(SettingsSourcePaths).Assembly.Location)!,
            "..", "..", "..", "..", ".."));

    internal static string ProjectSettingsXamlPath()
        => Path.Combine(RepoRoot(), "src", "SmartGuard.Settings", "SmartGuard.Settings.xaml");

    internal static string ProjectThemePath(string themeFileName)
        => Path.Combine(RepoRoot(), "src", "SmartGuard.Settings", "Themes", themeFileName);

    internal static string BrandIconPath()
        => Path.Combine(RepoRoot(), "assets", "branding", "SmartGuard.ico");

    internal static string ReadSettingsXamlSource()
        => File.ReadAllText(ProjectSettingsXamlPath());
}
