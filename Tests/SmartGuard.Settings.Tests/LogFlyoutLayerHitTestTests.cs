using System.IO;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogFlyoutLayerHitTestTests
{
    [Fact]
    public void Log_more_flyout_layer_does_not_block_log_scrollbar_hit_tests()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().MatchRegex(
            "x:Name=\"logMoreFlyoutLayer\"[\\s\\S]{0,180}IsHitTestVisible=\"False\"",
            "the log flyout overlay must stay transparent to mouse input unless the menu is open");
    }
}
