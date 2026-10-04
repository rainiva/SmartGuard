using SmartGuard.Contracts;

namespace SmartGuard.Tray;

internal sealed class TrayDisplayState(Func<DateTimeOffset>? utcNow = null)
{
  private StatusPayload? _lastStatus;
  private bool _hasApplied;
  private bool _lastFresh;
  private string _lastHealthCode = LogHealthCodes.Unknown;

  public string StatusLine { get; private set; } = "加载中…";

  public string Tooltip { get; private set; } = "智能电源守护";

  public bool Apply(StatusPayload? status)
  {
    var now = (utcNow ?? (() => DateTimeOffset.UtcNow))();
    var fresh = status is not null && GuardStatusFreshness.IsFresh(status.timestamp, now);
    var healthCode = LogHealthCodes.TrustedCode(status?.logHealth, now);
    // Engine 停止后字段不再变化，但时间仍会跨越过期边界，必须撤销旧守护声明。
    if (_hasApplied && _lastFresh == fresh && _lastHealthCode == healthCode && StatusEquals(_lastStatus, status))
      return false;

    _hasApplied = true;
    _lastFresh = fresh;
    _lastHealthCode = healthCode;
    _lastStatus = Clone(status);
    StatusLine = TrayStatusFormatter.FormatStatusLine(status, now);
    Tooltip = TrayStatusFormatter.FormatTooltip(status, now);
    return true;
  }

  private static bool StatusEquals(StatusPayload? left, StatusPayload? right)
  {
    if (left is null || right is null)
      return left is null && right is null;

    return left.currentPlan == right.currentPlan
      && left.batteryPercent == right.batteryPercent
      && left.isOnAC == right.isOnAC
      && left.brightness == right.brightness
      && left.paused == right.paused
      && left.guardSource == right.guardSource
      && left.guardReason == right.guardReason
      && left.foregroundEventsAvailable == right.foregroundEventsAvailable
      && left.logHealth?.Code == right.logHealth?.Code
      && left.notificationEvent?.id == right.notificationEvent?.id;
  }

  private static StatusPayload? Clone(StatusPayload? status)
  {
    if (status is null)
      return null;

    return new StatusPayload
    {
      timestamp = status.timestamp,
      currentPlan = status.currentPlan,
      currentPlanGUID = status.currentPlanGUID,
      expectedPlan = status.expectedPlan,
      idleSeconds = status.idleSeconds,
      isOnAC = status.isOnAC,
      batteryPercent = status.batteryPercent,
      brightness = status.brightness,
      paused = status.paused,
      lastExternalChange = status.lastExternalChange,
      guardSource = status.guardSource,
      guardReason = status.guardReason,
      foregroundEventsAvailable = status.foregroundEventsAvailable,
      logHealth = status.logHealth,
      notificationEvent = status.notificationEvent is null
        ? null
        : new NotificationEvent
        {
          id = status.notificationEvent.id,
          kind = status.notificationEvent.kind,
          title = status.notificationEvent.title,
          body = status.notificationEvent.body,
          at = status.notificationEvent.at,
        },
    };
  }
}
