using FluentAssertions;
using SmartGuard.Contracts;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayNotificationEvaluatorTests
{
    private static readonly TrayNotificationPreferences AllOn = new(true, true);

    [Fact]
    public void Evaluate_returns_null_when_status_missing()
    {
        var (decision, _) = TrayNotificationEvaluator.Evaluate(
            null,
            AllOn,
            TrayNotificationDedupeState.Empty);

        decision.Should().BeNull();
    }

    [Fact]
    public void Evaluate_emits_structured_event_for_plan_switch()
    {
        var status = new StatusPayload
        {
            currentPlan = "平衡",
            notificationEvent = new NotificationEvent
            {
                id = "evt-1",
                kind = NotificationKinds.PlanSwitch,
                title = "电源计划已切换",
                body = "已切换至 [平衡]",
            },
        };

        var (decision, next) = TrayNotificationEvaluator.Evaluate(
            status,
            AllOn,
            TrayNotificationDedupeState.Empty);

        decision.Should().NotBeNull();
        decision!.Value.Title.Should().Be("电源计划已切换");
        decision.Value.Body.Should().Be("已切换至 [平衡]");
        decision.Value.Tag.Should().Be("evt-1");
        decision.Value.UseBalloonFallback.Should().BeFalse();
        next.LastNotifiedEventId.Should().Be("evt-1");
        next.LastLegacyPlan.Should().Be("平衡");
    }

    [Fact]
    public void Evaluate_skips_duplicate_event_id()
    {
        var status = new StatusPayload
        {
            currentPlan = "平衡",
            notificationEvent = new NotificationEvent
            {
                id = "evt-1",
                kind = NotificationKinds.PlanSwitch,
                title = "电源计划已切换",
                body = "body",
            },
        };
        var state = new TrayNotificationDedupeState("evt-1", "平衡");

        var (decision, next) = TrayNotificationEvaluator.Evaluate(status, AllOn, state);

        decision.Should().BeNull();
        next.Should().Be(state);
    }

    [Fact]
    public void Evaluate_respects_external_change_switch()
    {
        var status = new StatusPayload
        {
            currentPlan = "平衡",
            notificationEvent = new NotificationEvent
            {
                id = "evt-ext",
                kind = NotificationKinds.ExternalChange,
                title = "外部变更",
                body = "body",
            },
        };
        var prefs = new TrayNotificationPreferences(true, false);

        var (decision, _) = TrayNotificationEvaluator.Evaluate(
            status,
            prefs,
            TrayNotificationDedupeState.Empty);

        decision.Should().BeNull();
    }

    [Fact]
    public void Evaluate_emits_legacy_balloon_when_plan_changes_without_event()
    {
        var status = new StatusPayload
        {
            currentPlan = "节能",
            brightness = 40,
        };
        var state = new TrayNotificationDedupeState(null, "平衡");

        var (decision, next) = TrayNotificationEvaluator.Evaluate(status, AllOn, state);

        decision.Should().NotBeNull();
        decision!.Value.UseBalloonFallback.Should().BeTrue();
        decision.Value.Title.Should().Be("智能电源守护");
        decision.Value.Body.Should().Contain("节能");
        decision.Value.Tag.Should().BeEmpty();
        next.LastLegacyPlan.Should().Be("节能");
    }
}
