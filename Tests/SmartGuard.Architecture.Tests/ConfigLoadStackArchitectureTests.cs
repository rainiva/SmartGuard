using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ConfigLoadStackArchitectureTests
{
  [Fact]
  public void Engine_must_not_call_GuardConfig_LoadFromFile()
  {
    var engineDir = Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Engine");
    var hits = Directory
      .EnumerateFiles(engineDir, "*.cs", SearchOption.AllDirectories)
      .Where(path => File.ReadAllText(path).Contains("GuardConfig.LoadFromFile", StringComparison.Ordinal))
      .Select(path => Path.GetRelativePath(SourceScanHelper.RepoRoot, path))
      .ToList();

    hits.Should().BeEmpty(
      "Engine must load config via GuardConfigRepository; LoadFromFile found in: "
      + string.Join(", ", hits));
  }
}
