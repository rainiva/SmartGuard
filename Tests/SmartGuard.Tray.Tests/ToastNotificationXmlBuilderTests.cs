namespace SmartGuard.Tray.Tests;

public class ToastNotificationXmlBuilderTests
{
  [Fact]
  public void Build_includes_escaped_title_and_body()
  {
    var xml = ToastNotificationXmlBuilder.Build("电源计划已切换", "已切换至 [节能] 亮度 56%");
    xml.Should().Contain("<text hint-maxLines=\"1\">电源计划已切换</text>");
    xml.Should().Contain("<text hint-style=\"subtitle\">已切换至 [节能] 亮度 56%</text>");
    xml.Should().Contain("template=\"ToastGeneric\"");
  }

  [Fact]
  public void Build_escapes_xml_special_characters()
  {
    var xml = ToastNotificationXmlBuilder.Build("A & B", "C < D");
    xml.Should().Contain("A &amp; B");
    xml.Should().Contain("C &lt; D");
  }

  [Fact]
  public void Build_does_not_include_appLogoOverride_or_any_image_element()
  {
    var xml = ToastNotificationXmlBuilder.Build("标题", "正文");
    xml.Should().NotContain("appLogoOverride",
      "the title-bar icon is provided by the AUMID IconUri registry value, not the toast XML");
    xml.Should().NotContain("<image",
      "toast XML must contain only <text> elements, no <image>");
  }

  [Fact]
  public void Build_does_not_include_activation_attribute()
  {
    var xml = ToastNotificationXmlBuilder.Build("标题", "正文");
    xml.Should().NotContain("activation=",
      "toast XML must not contain non-standard activation attribute on <toast> element");
  }
}
