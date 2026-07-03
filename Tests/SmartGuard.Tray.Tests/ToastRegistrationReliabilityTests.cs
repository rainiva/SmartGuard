namespace SmartGuard.Tray.Tests;

public class ToastRegistrationReliabilityTests
{
    [Fact]
    public void WinRtToastNotifier_must_not_register_shell_state_before_showing()
    {
        var source = File.ReadAllText(WinRtToastNotifierSourcePath());

        source.Should().NotContain(
            "ToastAumidRegistrar.EnsureRegistered(root)",
            "desktop shell registration must be installer-owned, not repaired by tray runtime");
    }

    private static string WinRtToastNotifierSourcePath()
    {
        var assemblyLocation = typeof(ToastRegistrationReliabilityTests).Assembly.Location;
        var repoRoot = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(assemblyLocation)!,
            "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "SmartGuard.Tray", "Toast", "WinRtToastNotifier.cs");
    }
}
