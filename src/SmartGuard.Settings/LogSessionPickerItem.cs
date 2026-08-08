using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal enum LogSessionPickerItemKind
{
  All,
  Session,
  Loading,
  Empty,
  Error,
}

internal sealed class LogSessionPickerItem
{
  private LogSessionPickerItem(
    LogSessionPickerItemKind kind,
    string title,
    LogSessionMetadata? session,
    bool isSelectable)
  {
    Kind = kind;
    Title = title;
    Session = session;
    IsSelectable = isSelectable;
  }

  public LogSessionPickerItemKind Kind { get; }

  public string Title { get; }

  public LogSessionMetadata? Session { get; }

  public bool IsSelectable { get; }

  internal static LogSessionPickerItem AllSessions()
    => new(LogSessionPickerItemKind.All, "全部会话", session: null, isSelectable: true);

  internal static LogSessionPickerItem ForSession(LogSessionMetadata session)
    => new(
      LogSessionPickerItemKind.Session,
      LogSessionProjector.BuildSessionTitle(session),
      session,
      isSelectable: true);

  internal static LogSessionPickerItem Loading()
    => new(LogSessionPickerItemKind.Loading, "正在识别会话…", session: null, isSelectable: false);

  internal static LogSessionPickerItem Empty()
    => new(
      LogSessionPickerItemKind.Empty,
      "当前日志中没有可识别的运行会话",
      session: null,
      isSelectable: false);

  internal static LogSessionPickerItem Error()
    => new(
      LogSessionPickerItemKind.Error,
      "会话加载失败，重新展开以重试",
      session: null,
      isSelectable: false);
}
