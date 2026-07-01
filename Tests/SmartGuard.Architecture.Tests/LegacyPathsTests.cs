using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class LegacyPathsTests
{
  [Fact]
  public void Legacy_SmartPowerPlan_path_literal_only_in_LegacyPaths()
  {
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      var relative = Path.GetRelativePath(SourceScanHelper.RepoRoot, file).Replace('\\', '/');
      if (relative.Contains("/obj/") || relative.Contains("/bin/"))
        continue;

      var source = File.ReadAllText(file);
      if (!source.Contains(@"C:\\Tools\\lib\\SmartPowerPlan") && !source.Contains(@"C:\Tools\lib\SmartPowerPlan"))
        continue;

      relative.Should().Be("src/SmartGuard.Configuration/LegacyPaths.cs",
        $"legacy install path constant must live in LegacyPaths, not {relative}");
    }
  }
}
