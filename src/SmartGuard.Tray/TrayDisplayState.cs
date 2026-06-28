using SmartGuard.Contracts;

namespace SmartGuard.Tray;

internal sealed class TrayDisplayState
{
  private StatusPayload? _lastStatus;
  private bool _hasApplied;

  public string StatusLine { get; private set; } = "加载中…";

  public string Tooltip { get; private set; } = "智能电源守护";

  public bool Apply(StatusPayload? status)
  {
    if (_hasApplied && StatusEquals(_lastStatus, status))
      return false;

    _hasApplied = true;
    _lastStatus = Clone(status);
    StatusLine = TrayStatusFormatter.FormatStatusLine(status);
    Tooltip = TrayStatusFormatter.FormatTooltip(status);
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
