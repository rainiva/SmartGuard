using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class DesktopAppBootstrapTests
{
  [Theory]
  [InlineData("src/SmartGuard.Settings/Program.cs")]
  [InlineData("src/SmartGuard.LogViewer/Program.cs")]
  public void Desktop_programs_must_use_DesktopAppBootstrap_not_raw_mutex_pipe(string relativePath)
  {
    var source = SourceScanHelper.ReadSource(relativePath);
    source.Should().Contain("DesktopAppBootstrap.RunSingleInstanceApp");
    source.Should().NotContain("SingleInstanceGuard.TryAcquire");
    source.Should().NotContain("SingleInstanceActivation.RunActivationServer");
  }
}
