namespace SmartGuard.Tray.Tests;

public class ToastRegistrationReliabilityTests
{
    [Fact]
    public void WinRtToastNotifier_still_registers_before_showing()
    {
        var source = File.ReadAllText(WinRtToastNotifierSourcePath());

        source.Should().Contain(
            "ToastAumidRegistrar.EnsureRegistered(root)",
            "toast display must self-register so deferred tray startup cannot break notifications");
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
