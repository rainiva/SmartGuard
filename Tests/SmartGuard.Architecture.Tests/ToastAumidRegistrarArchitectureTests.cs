using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ToastAumidRegistrarArchitectureTests
{
  [Fact]
  public void DesktopShellRegistration_must_own_registry_and_shortcut_writes()
  {
    File.Exists(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Configuration",
        "Shell",
        "DesktopShellRegistration.cs"))
      .Should().BeTrue();

    var registration = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Shell/DesktopShellRegistration.cs");
    registration.Should().Contain("Registry.CurrentUser.CreateSubKey");
    registration.Should().Contain("WScript.Shell");
    registration.Should().Contain("ShellLinkAppUserModelId");

    foreach (var obsoleteRuntimeOwner in new[]
    {
      "src/SmartGuard.Tray/Toast/ToastAumidRegistrar.cs",
      "src/SmartGuard.Tray/Toast/ToastRegistryWriter.cs",
      "src/SmartGuard.Tray/Toast/StartMenuShortcutWriter.cs",
      "src/SmartGuard.Tray/Toast/ToastShortcutResolver.cs",
    })
    {
      File.Exists(Path.Combine(SourceScanHelper.RepoRoot, obsoleteRuntimeOwner))
        .Should().BeFalse($"{obsoleteRuntimeOwner} was replaced by installer-owned shell registration");
    }
  }
}
