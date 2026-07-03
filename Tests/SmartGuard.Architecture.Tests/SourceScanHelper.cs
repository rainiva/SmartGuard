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

  internal static string ExtractMethodBody(string source, string methodSignatureFragment)
  {
    var start = source.IndexOf(methodSignatureFragment, StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1, $"expected method containing {methodSignatureFragment}");
    var braceStart = source.IndexOf('{', start);
    var depth = 0;
    for (var i = braceStart; i < source.Length; i++)
    {
      if (source[i] == '{') depth++;
      else if (source[i] == '}')
      {
        depth--;
        if (depth == 0)
          return source[braceStart..(i + 1)];
      }
    }

    throw new InvalidOperationException($"Could not extract body for {methodSignatureFragment}");
  }
}
