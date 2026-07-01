using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SingleRootResolverArchitectureTests
{
  [Fact]
  public void Only_InstallRootResolver_may_exist_as_root_resolver_type()
  {
    var srcRoot = Path.Combine(SourceScanHelper.RepoRoot, "src");
    var offenders = Directory
      .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
      .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}InstallRootResolver.cs", StringComparison.Ordinal))
      .Where(path => File.ReadAllText(path).Contains("class RootResolver", StringComparison.Ordinal))
      .Select(path => Path.GetRelativePath(SourceScanHelper.RepoRoot, path))
      .ToList();

    offenders.Should().BeEmpty(
      "all apps must use SmartGuard.Configuration.InstallRootResolver; found duplicate RootResolver in: "
      + string.Join(", ", offenders));
  }
}
