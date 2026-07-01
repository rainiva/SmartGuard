using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsLogModuleArchitectureTests
{
  [Theory]
  [InlineData("src/SmartGuard.Settings/SettingsLogExportActions.cs")]
  [InlineData("src/SmartGuard.Settings/SettingsLogFollowTailCoordinator.cs")]
  [InlineData("src/SmartGuard.Settings/SettingsLogSearchCoordinator.cs")]
  public void Log_page_extracted_modules_must_exist(string relativePath)
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
      .Should().BeTrue();
  }
}
