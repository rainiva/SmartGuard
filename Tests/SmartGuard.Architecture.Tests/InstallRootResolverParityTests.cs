using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class InstallRootResolverParityTests
{
  [Theory]
  [InlineData("Engine")]
  [InlineData("Tray")]
  [InlineData("Settings")]
  [InlineData("LogViewer")]
  public void Apps_resolve_same_install_root_from_bin_directory(string appFolder)
  {
    var installRoot = Path.Combine(Path.GetTempPath(), "sg-parity-" + Guid.NewGuid().ToString("N"));
    var binDir = Path.Combine(installRoot, "bin");
    Directory.CreateDirectory(binDir);
    File.WriteAllText(Path.Combine(installRoot, SmartGuard.Configuration.SmartGuardPaths.ConfigFileName), "{}");

    try
    {
      var resolved = SmartGuard.Configuration.InstallRootResolver.ResolveFromBaseDirectory(binDir, null, []);
      resolved.Should().Be(Path.GetFullPath(installRoot), $"app folder {appFolder} should share resolver behavior");
    }
    finally
    {
      Directory.Delete(installRoot, true);
    }
  }
}
