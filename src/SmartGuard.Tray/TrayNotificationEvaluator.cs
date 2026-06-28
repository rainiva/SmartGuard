using SmartGuard.Contracts;

namespace SmartGuard.Tray;

internal readonly record struct TrayNotificationDedupeState(
  string? LastNotifiedEventId,
  string? LastLegacyPlan)
{
  public static TrayNotificationDedupeState Empty { get; } = new(null, null);
}

internal readonly record struct TrayNotificationDecision(
  string Title,
  string Body,
  string Tag,
  bool UseBalloonFallback);

internal static class TrayNotificationEvaluator
{
  public static (TrayNotificationDecision? Decision, TrayNotificationDedupeState NextState) Evaluate(
    StatusPayload? status,
    TrayNotificationPreferences preferences,
    TrayNotificationDedupeState state)
  {
    if (status is null)
      return (null, state);

    var evt = status.notificationEvent;
    if (evt is null || string.IsNullOrEmpty(evt.id))
      return EvaluateLegacy(status, preferences, state);

    if (!TrayNotificationPreferences.ShouldNotify(
          evt.kind,
          preferences.NotifyOnPlanChange,
          preferences.NotifyOnExternalChange))
      return (null, state);

    if (!NotificationDeduper.ShouldShow(state.LastNotifiedEventId, evt))
      return (null, state);

    var title = string.IsNullOrEmpty(evt.title) ? "智能电源守护" : evt.title;
    var body = string.IsNullOrEmpty(evt.body) ? status.currentPlan : evt.body;
    var decision = new TrayNotificationDecision(title, body, evt.id, UseBalloonFallback: false);
    var next = new TrayNotificationDedupeState(evt.id, status.currentPlan);
    return (decision, next);
  }

  private static (TrayNotificationDecision? Decision, TrayNotificationDedupeState NextState) EvaluateLegacy(
    StatusPayload status,
    TrayNotificationPreferences preferences,
    TrayNotificationDedupeState state)
  {
    if (!TrayNotificationPreferences.ShouldNotify(
          null,
          preferences.NotifyOnPlanChange,
          preferences.NotifyOnExternalChange))
      return (null, state);

    if (!TrayNotificationHelper.PlanChangedForNotification(state.LastLegacyPlan, status.currentPlan))
      return (null, state);

    var body = TrayNotificationHelper.FormatPlanChangeBalloon(status.currentPlan, status.brightness);
    var decision = new TrayNotificationDecision("智能电源守护", body, Tag: string.Empty, UseBalloonFallback: true);
    var next = new TrayNotificationDedupeState(state.LastNotifiedEventId, status.currentPlan);
    return (decision, next);
  }
}
