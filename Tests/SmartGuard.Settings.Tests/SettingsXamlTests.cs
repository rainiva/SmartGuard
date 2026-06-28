using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class SettingsXamlTests
{
    [Fact]
    public void Committed_settings_xaml_parses_without_error()
    {
        WpfStaTestHost.Run(() =>
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
            var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));
            var xamlPath = Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml");

            File.Exists(xamlPath).Should().BeTrue();
            var xaml = File.ReadAllText(xamlPath);
            xaml.Should().Contain("x:Name=\"tglPaused\"");
            xaml.Should().Contain("x:Name=\"tglNotifyExternal\"");
            xaml.Should().Contain("x:Name=\"tglAutoStart\"");

            var xamlForParse = xaml.Replace(
                "xmlns:local=\"clr-namespace:SmartGuard.Settings\"",
                "xmlns:local=\"clr-namespace:SmartGuard.Settings;assembly=SmartGuard.Settings\"");
            var result = XamlReader.Parse(xamlForParse);

            result.Should().BeOfType<Window>();
        });
    }
}
