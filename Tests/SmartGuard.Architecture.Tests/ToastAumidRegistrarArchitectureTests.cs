using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ToastAumidRegistrarArchitectureTests
{
  [Fact]
  public void ToastAumidRegistrar_must_delegate_registry_and_shortcut_writers()
  {
    File.Exists(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Tray",
        "Toast",
        "ToastRegistryWriter.cs"))
      .Should().BeTrue();
    File.Exists(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Tray",
        "Toast",
        "StartMenuShortcutWriter.cs"))
      .Should().BeTrue();

    var registrar = SourceScanHelper.ReadSource("src/SmartGuard.Tray/Toast/ToastAumidRegistrar.cs");
    registrar.Should().Contain("ToastRegistryWriter");
    registrar.Should().Contain("StartMenuShortcutWriter");
    registrar.Should().NotContain("Registry.CurrentUser.CreateSubKey");
    registrar.Should().NotContain("WScript.Shell");

    File.ReadAllLines(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Tray",
        "Toast",
        "ToastAumidRegistrar.cs")).Length.Should().BeLessThan(300);
  }
}
