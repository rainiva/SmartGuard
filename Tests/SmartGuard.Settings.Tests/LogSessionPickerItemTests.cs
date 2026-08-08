using System.ComponentModel;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogSessionPickerItemTests
{
  [Fact]
  public void Session_item_exposes_a_title_visible_to_wpf_binding()
  {
    var session = CreateSession();
    var item = LogSessionPickerItem.ForSession(session);

    var titleProperty = TypeDescriptor.GetProperties(item!)["Title"];
    titleProperty.Should().NotBeNull();
    titleProperty!.GetValue(item).Should().Be(LogSessionProjector.BuildSessionTitle(session));
  }

  [Fact]
  public void Factory_methods_use_expected_titles_and_selectability()
  {
    LogSessionPickerItem.AllSessions().Should().BeEquivalentTo(new
    {
      Kind = LogSessionPickerItemKind.All,
      Title = "全部会话",
      Session = (LogSessionMetadata?)null,
      IsSelectable = true,
    });

    LogSessionPickerItem.Loading().Should().BeEquivalentTo(new
    {
      Kind = LogSessionPickerItemKind.Loading,
      Title = "正在识别会话…",
      Session = (LogSessionMetadata?)null,
      IsSelectable = false,
    });

    LogSessionPickerItem.Empty().Should().BeEquivalentTo(new
    {
      Kind = LogSessionPickerItemKind.Empty,
      Title = "当前日志中没有可识别的运行会话",
      Session = (LogSessionMetadata?)null,
      IsSelectable = false,
    });

    LogSessionPickerItem.Error().Should().BeEquivalentTo(new
    {
      Kind = LogSessionPickerItemKind.Error,
      Title = "会话加载失败，重新展开以重试",
      Session = (LogSessionMetadata?)null,
      IsSelectable = false,
    });
  }

  private static LogSessionMetadata CreateSession()
    => new(
      "s:e:start",
      new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero),
      new DateTimeOffset(2026, 8, 2, 10, 30, 0, TimeSpan.Zero),
      100,
      LogComponentRegistry.Engine,
      false,
      "e:start",
      "e:end");
}
