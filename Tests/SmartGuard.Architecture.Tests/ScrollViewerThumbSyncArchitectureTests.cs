using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ScrollViewerThumbSyncArchitectureTests
{
  [Fact]
  public void ScrollViewerThumbSync_must_not_exist_or_be_referenced()
  {
    var thumbSyncPath = Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "ScrollViewerThumbSync.cs");
    File.Exists(thumbSyncPath).Should().BeFalse(
      "ScrollViewerThumbSync overwrites native scroll commands with stale Scroll event NewValue");

    var controllerSource = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    controllerSource.Should().NotContain("ScrollViewerThumbSync");

    var presenterSource = SourceScanHelper.ReadSource("src/SmartGuard.Settings/LogViewListPresenter.cs");
    presenterSource.Should().NotContain("ScrollViewerThumbSync");
  }
}
