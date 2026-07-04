using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ScrollBarAutoHideArchitectureTests
{
  [Fact]
  public void ScrollBarAutoHide_must_cache_scrollbar_parts_instead_of_reapplying_templates_on_scroll()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/ScrollBarAutoHide.cs");
    source.Should().Contain("VerticalScrollBarProperty");
    source.Should().Contain("HorizontalScrollBarProperty");

    var findScrollBar = ExtractMethodBody(source, "private static ScrollBar? FindScrollBar");
    findScrollBar.Should().NotContain("ApplyTemplate()",
      "scroll hot paths must not force template application on every ScrollChanged event");
    findScrollBar.Should().Contain("GetValue(VerticalScrollBarProperty)");
    findScrollBar.Should().Contain("GetValue(HorizontalScrollBarProperty)");
  }

  private static string ExtractMethodBody(string source, string marker)
  {
    var start = source.IndexOf(marker, StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1, $"{marker} must exist");

    var braceStart = source.IndexOf('{', start);
    braceStart.Should().BeGreaterThan(start);

    var depth = 0;
    for (var index = braceStart; index < source.Length; index++)
    {
      if (source[index] == '{')
        depth++;
      else if (source[index] == '}')
      {
        depth--;
        if (depth == 0)
          return source[braceStart..(index + 1)];
      }
    }

    throw new InvalidOperationException($"Could not extract method body for {marker}");
  }
}
