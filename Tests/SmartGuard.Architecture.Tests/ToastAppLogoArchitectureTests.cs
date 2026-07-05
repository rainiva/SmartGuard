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
  public void SmartGuardPaths_must_not_define_retired_toast_png_path()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SmartGuardPaths.cs");
    source.Should().NotContain("BrandToastIconFileName",
      "the retired toast PNG file-name constant must be removed");
    source.Should().NotContain("BrandToastIconFile",
      "the retired BrandToastIconFile(root) method must be removed");
    source.Should().NotContain("SmartGuard-toast.png",
      "the retired toast PNG literal must not remain in SmartGuardPaths");
  }

  [Fact]
  public void BrandIconLoader_must_not_provide_retired_toast_png_deploy_method()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/BrandIconLoader.cs");
    source.Should().NotContain("SaveEmbeddedToastIconToFile",
      "the retired SaveEmbeddedToastIconToFile method must be removed");
    source.Should().NotContain("EmbeddedToastResourceName",
      "the retired toast PNG embedded-resource constant must be removed");
    source.Should().NotContain("OpenEmbeddedToastIconStream",
      "the retired toast PNG stream opener must be removed");
  }

  [Fact]
  public void Configuration_csproj_must_not_embed_retired_toast_png_resource()
  {
    var csproj = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SmartGuard.Configuration.csproj");
    csproj.Should().NotContain("SmartGuard-toast.png",
      "the retired toast PNG EmbeddedResource must be removed from the Configuration project");
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
  public void DesktopShellRegistration_must_use_brand_icon_file_for_iconUri()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Shell/DesktopShellRegistration.cs");
    source.Should().Contain("BrandIconFile",
      "AUMID IconUri must point to the deployed .ico file, not the .exe");
    source.Should().Contain("SaveEmbeddedIconToFile",
      "the .ico consumed by AUMID IconUri must be deployed via BrandIconLoader.SaveEmbeddedIconToFile");
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
  }
}
