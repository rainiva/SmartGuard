using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class SettingsAboutCoordinatorTests
{
    [Fact]
    public void GetDisplayVersion_always_shows_three_part_semver()
    {
        var display = SettingsAboutCoordinator.GetDisplayVersion();

        display.Should().MatchRegex(
            @"^\d+\.\d+\.\d+$",
            "about page version must always show major.minor.patch without omitting trailing zero");
    }
}
