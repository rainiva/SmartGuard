using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class CliLineCountTests
{
  [Theory]
  [InlineData("src/SmartGuard.Cli/Program.cs", 150)]
  [InlineData("src/SmartGuard.Cli/CliRouter.cs", 150)]
  public void Cli_entry_files_must_stay_under_line_budget(string relativePath, int maxLines)
  {
    var path = Path.Combine(SourceScanHelper.RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    File.Exists(path).Should().BeTrue(relativePath);
    var lineCount = File.ReadAllLines(path).Length;
    lineCount.Should().BeLessThanOrEqualTo(maxLines, $"{relativePath} must remain a thin facade");
  }
}
