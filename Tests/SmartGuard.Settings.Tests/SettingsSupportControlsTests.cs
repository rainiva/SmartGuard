using System.IO;
namespace SmartGuard.Settings.Tests;

public class SettingsSupportControlsTests
{
  [Fact]
  public void Support_controls_exist_in_xaml()
  {
    var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

    xaml.Should().Contain("x:Name=\"sldHeartbeat\"");
    xaml.Should().Contain("x:Name=\"cmbActivePlan\"");
    xaml.Should().Contain("x:Name=\"cmbBalancedPlan\"");
    xaml.Should().Contain("x:Name=\"cmbPowerSaverPlan\"");
    xaml.Should().Contain("x:Name=\"btnResetDefaults\"");
  }
}
