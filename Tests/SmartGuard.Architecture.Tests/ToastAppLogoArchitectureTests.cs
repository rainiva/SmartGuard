using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ToastAppLogoArchitectureTests
{
  // 标题栏小图标由 AUMID 注册表 IconUri 控制（Windows Shell 自动渲染），
  // 不能通过 Toast XML 控制。appLogoOverride 是内容区的大图，在 Windows 11+
  // 不会替代标题栏图标，反而会错误地在内容区显示一个额外大图。因此 Toast XML
  // 必须只包含 <text> 元素，不得包含 appLogoOverride / <image>。

  [Fact]
  public void ToastNotificationXmlBuilder_must_not_include_appLogoOverride_or_image_element()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Tray/Toast/ToastNotificationXmlBuilder.cs");
    source.Should().NotContain("appLogoOverride",
      "toast XML must NOT include appLogoOverride; the title-bar icon is provided by the AUMID IconUri registry value, not the XML");
    source.Should().NotContain("<image",
      "toast XML must not contain any <image> element; only <text> elements are allowed");
    source.Should().NotContain("iconPath",
      "ToastNotificationXmlBuilder must not accept an icon path parameter");
  }

  [Fact]
  public void WinRtToastNotifier_must_not_carry_an_icon_path()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Tray/Toast/WinRtToastNotifier.cs");
    source.Should().NotContain("iconPath",
      "WinRtToastNotifier must not accept an icon path; the title-bar icon comes from the AUMID IconUri");
    source.Should().NotContain("_iconUri",
      "WinRtToastNotifier must not keep an icon URI field");
    source.Should().Contain("ToastNotificationXmlBuilder.Build(title, body)",
      "WinRtToastNotifier must call the two-argument Build(title, body) overload");
  }

  [Fact]
  public void TrayApplicationContext_must_not_deploy_or_pass_toast_png_icon()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Tray/TrayApplicationContext.cs");
    source.Should().NotContain("BrandToastIconFile",
      "TrayApplicationContext must not reference the retired toast PNG path");
    source.Should().NotContain("SaveEmbeddedToastIconToFile",
      "TrayApplicationContext must not deploy the toast PNG at runtime");
    source.Should().Contain("new WinRtToastNotifier()",
      "TrayApplicationContext must construct WinRtToastNotifier without an icon argument");
  }

  [Fact]
  public void TrayApplicationContext_must_not_self_register_shell_at_runtime()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Tray/TrayApplicationContext.cs");
    source.Should().NotContain("DesktopShellRegistration.Apply",
      "AUMID/shell registration is installer-owned (DesktopShellRegistration); Tray runtime must not self-register");
  }

  [Fact]
  public void SmartGuardPaths_must_define_dedicated_toast_png_path()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SmartGuardPaths.cs");
    source.Should().Contain("BrandToastIconFileName",
      "SmartGuardPaths must declare a dedicated toast PNG file name so AUMID IconUri can point at a high-quality raster asset");
    source.Should().Contain("BrandToastIconFile(",
      "SmartGuardPaths must expose a dedicated toast PNG path helper for install-time shell registration");
    source.Should().Contain("SmartGuard-toast.png",
      "the dedicated toast PNG file name must stay stable for installer-owned deployment");
  }

  [Fact]
  public void BrandIconLoader_must_provide_toast_png_deploy_method()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/BrandIconLoader.cs");
    source.Should().Contain("SaveEmbeddedToastIconToFile",
      "BrandIconLoader must deploy the dedicated toast PNG during installer-owned shell registration");
    source.Should().Contain("EmbeddedToastResourceName",
      "BrandIconLoader must embed a dedicated toast PNG resource for IconUri deployment");
    source.Should().Contain("OpenEmbeddedToastIconStream",
      "BrandIconLoader must provide a dedicated toast PNG stream opener");
  }

  [Fact]
  public void Configuration_csproj_must_embed_dedicated_toast_png_resource()
  {
    var csproj = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SmartGuard.Configuration.csproj");
    csproj.Should().Contain("SmartGuard-toast.png",
      "the Configuration project must embed the dedicated toast PNG resource used by AUMID IconUri");
  }

  [Fact]
  public void SmartGuardPaths_must_expose_brand_icon_file_path()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SmartGuardPaths.cs");
    source.Should().Contain("BrandIconFileName",
      "SmartGuardPaths must declare the brand icon file name constant");
    source.Should().Contain("BrandIconFile(",
      "SmartGuardPaths must expose a BrandIconFile(root) method");
  }

  [Fact]
  public void DesktopShellRegistration_must_use_dedicated_toast_png_for_iconUri()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Shell/DesktopShellRegistration.cs");
    source.Should().Contain("BrandToastIconFile",
      "AUMID IconUri must point to the dedicated deployed toast PNG, not the tray EXE or the generic .ico");
    source.Should().Contain("SaveEmbeddedToastIconToFile",
      "the dedicated toast PNG consumed by AUMID IconUri must be deployed via BrandIconLoader.SaveEmbeddedToastIconToFile");
    source.Should().Contain("IconUri",
      "RegisterAppUserModelId must set the IconUri registry value (the real title-bar icon source)");
    source.Should().NotContain("TrayExe(root)\";\n    if (File.Exists(iconTarget))\n      key.SetValue(\"IconUri\"",
      "IconUri must not reference TrayExe");
  }

  [Fact]
  public void DesktopShellRegistration_must_set_IconUri_with_raw_path_not_file_uri()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Shell/DesktopShellRegistration.cs");
    source.Should().NotContain("new Uri(iconPath).AbsoluteUri",
      "IconUri must be set to the raw file path, not a file:/// URI; Windows Shell expects a plain path");
    source.Should().NotContain("AbsoluteUri",
      "IconUri registry value must not use Uri.AbsoluteUri conversion");
  }

  [Fact]
  public void BrandIconLoader_must_provide_save_to_file_for_install_time_deployment()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/BrandIconLoader.cs");
    source.Should().Contain("SaveEmbeddedIconToFile",
      "BrandIconLoader must provide SaveEmbeddedIconToFile for install-time .ico deployment");
    source.Should().Contain("SaveEmbeddedToastIconToFile",
      "BrandIconLoader must also provide SaveEmbeddedToastIconToFile for install-time toast IconUri deployment");
  }
}
