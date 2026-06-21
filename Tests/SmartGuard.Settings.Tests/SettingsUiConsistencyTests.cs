using System.IO;
using System.Reflection;

namespace SmartGuard.Settings.Tests;

public class SettingsUiConsistencyTests
{
  private static string RepoXamlPath()
  {
    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
    var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
    var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));
    return Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml");
  }

  [Fact]
  public void Settings_xaml_uses_unified_combo_and_filter_checkbox_styles()
  {
    var xaml = File.ReadAllText(RepoXamlPath());

    xaml.Should().Contain("x:Key=\"SettingsTextBox\"");
    xaml.Should().Contain("TargetType=\"{x:Type ComboBox}\"");
    xaml.Should().Contain("TargetType=\"{x:Type TextBox}\"");
    xaml.Should().NotContain("TargetType=\"{x:Type CheckBox}\"");
    xaml.Should().NotContain("TargetType=\"{x:Type Button}\"");
    xaml.Should().Contain("OverridesDefaultStyle\" Value=\"True\"");
    xaml.Should().Contain("x:Key=\"SettingsFilterCheckBox\"");
    xaml.Should().Contain("Style=\"{StaticResource SettingsComboBox}\"");
    xaml.Should().Contain("Style=\"{StaticResource SettingsFilterCheckBox}\"");
  }

  [Fact]
  public void Settings_xaml_defines_numberbox_button_before_numberbox_style()
  {
    var xaml = File.ReadAllText(RepoXamlPath());
    var buttonStyleIndex = xaml.IndexOf("x:Key=\"NumberBoxButton\"", StringComparison.Ordinal);
    var numberBoxStyleIndex = xaml.IndexOf("TargetType=\"local:NumberBox\"", StringComparison.Ordinal);

    buttonStyleIndex.Should().BeGreaterThan(0);
    numberBoxStyleIndex.Should().BeGreaterThan(0);
    buttonStyleIndex.Should().BeLessThan(numberBoxStyleIndex);
  }

  [Fact]
  public void Settings_xaml_shows_legacy_app_icon_in_header()
  {
    var xaml = File.ReadAllText(RepoXamlPath());
    xaml.Should().Contain("x:Name=\"imgAppIcon\"");
  }
}
