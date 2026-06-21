using System.IO;
using System.Reflection;

namespace SmartGuard.Settings.Tests;

public class SettingsSupportControlsTests
{
  private static string RepoXamlPath()
  {
    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
    var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
    var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));
    return Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml");
  }

  [Fact]
  public void Support_controls_exist_in_xaml()
  {
    var xaml = File.ReadAllText(RepoXamlPath());

    xaml.Should().Contain("x:Name=\"sldHeartbeat\"");
    xaml.Should().Contain("x:Name=\"cmbActivePlan\"");
    xaml.Should().Contain("x:Name=\"cmbBalancedPlan\"");
    xaml.Should().Contain("x:Name=\"cmbPowerSaverPlan\"");
    xaml.Should().Contain("x:Name=\"btnResetDefaults\"");
  }
}
