namespace SmartGuard.Architecture.Tests;

using FluentAssertions;

internal static class SourceScanHelper
{
  internal static string RepoRoot =>
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

  internal static string ReadSource(string relativePath)
  {
    var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    File.Exists(path).Should().BeTrue($"expected source at {path}");
    return File.ReadAllText(path);
  }

  internal static IEnumerable<string> EnumerateCsFilesUnder(string relativeDir)
  {
    var root = Path.Combine(RepoRoot, relativeDir.Replace('/', Path.DirectorySeparatorChar));
    return Directory.Exists(root)
      ? Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
      : [];
  }
}
