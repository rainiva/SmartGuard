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

    [Fact]
    public void Tray_program_must_set_process_app_user_model_id_before_showing_ui()
    {
        var source = File.ReadAllText(TrayProgramSourcePath());

        source.Should().Contain(
            "SetCurrentProcessExplicitAppUserModelID(SmartGuardToastAppId.AppId);",
            "the tray process itself must opt into the SmartGuard AUMID so Windows can attribute the toast header icon correctly");
    }

    private static string WinRtToastNotifierSourcePath()
    {
        var assemblyLocation = typeof(ToastRegistrationReliabilityTests).Assembly.Location;
        var repoRoot = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(assemblyLocation)!,
            "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "SmartGuard.Tray", "Toast", "WinRtToastNotifier.cs");
    }

    private static string TrayProgramSourcePath()
    {
        var assemblyLocation = typeof(ToastRegistrationReliabilityTests).Assembly.Location;
        var repoRoot = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(assemblyLocation)!,
            "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "SmartGuard.Tray", "Program.cs");
    }
}
