using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ToastPngIconArchitectureTests
{
  [Fact]
  public void SmartGuardPaths_must_define_toast_png_icon_path()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SmartGuardPaths.cs");
    source.Should().Contain("BrandToastIconFileName",
      "SmartGuardPaths must declare BrandToastIconFileName constant for the 48x48 PNG toast icon");
    source.Should().Contain("SmartGuard-toast.png",
      "BrandToastIconFileName value must be 'SmartGuard-toast.png'");
    source.Should().Contain("BrandToastIconFile(",
      "SmartGuardPaths must expose a BrandToastIconFile(root) method");
  }

  [Fact]
  public void BrandIconLoader_must_provide_save_embedded_toast_icon_method()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/BrandIconLoader.cs");
    source.Should().Contain("SaveEmbeddedToastIconToFile",
      "BrandIconLoader must provide SaveEmbeddedToastIconToFile to extract the embedded 48x48 PNG");
  }

  [Fact]
  public void Toast_notification_must_use_png_icon_not_ico()
  {
    var trayContext = SourceScanHelper.ReadSource("src/SmartGuard.Tray/TrayApplicationContext.cs");
    trayContext.Should().Contain("BrandToastIconFile",
      "TrayApplicationContext must pass BrandToastIconFile (PNG) path to WinRtToastNotifier, not BrandIconFile (.ico)");
  }

  [Fact]
  public void Configuration_csproj_must_embed_toast_png_resource()
  {
    var csproj = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SmartGuard.Configuration.csproj");
    csproj.Should().Contain("SmartGuard-toast.png",
      "SmartGuard.Configuration.csproj must include EmbeddedResource for the toast PNG icon");
  }
}
