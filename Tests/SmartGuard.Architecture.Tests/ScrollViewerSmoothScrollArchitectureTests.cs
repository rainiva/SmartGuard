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

  [Fact]
  public void Log_view_must_use_native_pixel_scroll_without_custom_inertia()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/LogViewListPresenter.cs");
    source.Should().Contain("ScrollUnit.Pixel");
    source.Should().NotContain("ScrollViewerSmoothScroll.Attach(ScrollViewer)");
  }
}
