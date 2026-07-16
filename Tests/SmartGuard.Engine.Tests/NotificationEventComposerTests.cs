using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;

namespace SmartGuard.Engine.Tests;

public class NotificationEventComposerTests
{
  private static NotificationEvent PlanSwitch()
    => new()
    {
      id = "switch",
      kind = NotificationKinds.PlanSwitch,
      title = "电源计划已切换",
      body = "已切换至 [高性能] (亮度 36%)",
    };

  private static NotificationEvent External()
    => new()
    {
      id = "ext",
      kind = NotificationKinds.ExternalChange,
      title = "检测到外部计划变更",
      body = "计划被外部改为 [平衡]",
    };

  [Fact]
  public void Compose_skips_plan_switch_when_notify_on_plan_change_is_false()
  {
    var evt = NotificationEventComposer.Compose(
      externalChangeDetected: true,
      planSwitched: true,
      notifyOnPlanChange: false,
      notifyOnExternalChange: true,
      createPlanSwitch: PlanSwitch,
      createExternal: External);

    evt.Should().BeNull();
  }

  [Fact]
  public void Compose_emits_plan_switch_when_enabled_even_if_external_also_detected()
  {
    var evt = NotificationEventComposer.Compose(
      externalChangeDetected: true,
      planSwitched: true,
      notifyOnPlanChange: true,
      notifyOnExternalChange: true,
      createPlanSwitch: PlanSwitch,
      createExternal: External);

    evt!.kind.Should().Be(NotificationKinds.PlanSwitch);
  }

  [Fact]
  public void Compose_emits_external_when_no_switch_and_external_notify_enabled()
  {
    var evt = NotificationEventComposer.Compose(
      externalChangeDetected: true,
      planSwitched: false,
      notifyOnPlanChange: false,
      notifyOnExternalChange: true,
      createPlanSwitch: PlanSwitch,
      createExternal: External);

    evt!.kind.Should().Be(NotificationKinds.ExternalChange);
  }

  [Fact]
  public void ApplyCooldown_suppresses_identical_body_within_window()
  {
    var first = PlanSwitch();
    var again = PlanSwitch();
    again.id = "switch-2";
    var now = new DateTime(2026, 7, 16, 22, 0, 0);

    var suppressed = NotificationEventComposer.ApplyCooldown(
      again,
      first,
      lastEmittedAt: now,
      now: now.AddSeconds(30),
      cooldown: TimeSpan.FromMinutes(2));

    suppressed.Should().BeNull();
  }

  [Fact]
  public void ApplyCooldown_allows_after_window_elapses()
  {
    var first = PlanSwitch();
    var again = PlanSwitch();
    again.id = "switch-2";
    var now = new DateTime(2026, 7, 16, 22, 0, 0);

    var allowed = NotificationEventComposer.ApplyCooldown(
      again,
      first,
      lastEmittedAt: now,
      now: now.AddMinutes(3),
      cooldown: TimeSpan.FromMinutes(2));

    allowed.Should().BeSameAs(again);
  }
}
