using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ScrollViewerSmoothScrollArchitectureTests
{
  [Fact]
  public void ScrollViewerSmoothScroll_must_not_reapply_templates_on_wheel_or_render()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/ScrollViewerSmoothScroll.cs");
    source.Should().NotContain("ApplyTemplate()");
    source.Should().Contain("PreviewMouseWheel");
    source.Should().Contain("Friction");
  }
}
