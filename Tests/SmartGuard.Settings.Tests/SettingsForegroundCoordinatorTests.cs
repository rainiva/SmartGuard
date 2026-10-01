using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Settings.Tests;

/// <summary>
/// 任务 4：SettingsForegroundCoordinator 行为（添加规范化/去重、删除、空名单提示、
/// 开关触发的既有防抖保存链）。文件选择与提示均经注入接缝，不弹真实对话框。
/// </summary>
[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsForegroundCoordinatorTests
{
  [Fact]
  public void Add_normalizes_picked_executable_and_queues_save()
  {
    WpfStaTestHost.Run(() =>
    {
      var saved = new List<string>();
      string? picked = null;
      using var coordinator = CreateCoordinator(
        pickProgramFile: () => picked,
        queueSave: () => saved.Add("save"));

      picked = @"C:\Program Files\App\tool.exe";
      coordinator.AddProgram();

      coordinator.ProgramPaths.Should().Equal(@"C:\Program Files\App\tool.exe");
      saved.Should().ContainSingle("添加后走既有防抖保存链");
      coordinator.IsAddHintVisible().Should().BeFalse("名单非空后隐藏空名单提示");
    });
  }

  [Fact]
  public void Add_reports_duplicate_after_case_insensitive_normalize_and_does_not_duplicate()
  {
    WpfStaTestHost.Run(() =>
    {
      var feedback = new List<(string Message, bool IsError)>();
      var saves = 0;
      using var coordinator = CreateCoordinator(
        initialPaths: [@"C:\Windows\System32\notepad.exe"],
        pickProgramFile: () => @"c:\WINDOWS\system32\NOTEPAD.exe",
        showFeedback: (message, isError) => feedback.Add((message, isError)),
        queueSave: () => saves++);

      coordinator.AddProgram();

      coordinator.ProgramPaths.Should().ContainSingle("规范化后 OrdinalIgnoreCase 重复项合并");
      feedback.Should().ContainSingle(x => !x.IsError, "重复项给出合并提示");
      saves.Should().Be(0, "合并不改变配置，不触发保存");
    });
  }

  [Fact]
  public void Add_rejects_invalid_selection_with_error_feedback_without_saving()
  {
    WpfStaTestHost.Run(() =>
    {
      var feedback = new List<(string Message, bool IsError)>();
      var saves = 0;
      using var coordinator = CreateCoordinator(
        pickProgramFile: () => @"C:\Users\public\readme.txt",
        showFeedback: (message, isError) => feedback.Add((message, isError)),
        queueSave: () => saves++);

      coordinator.AddProgram();

      coordinator.ProgramPaths.Should().BeEmpty("非法选择不得加入名单");
      feedback.Should().ContainSingle(x => x.IsError, "非法选择给错误提示");
      saves.Should().Be(0, "非法选择不得保存");
    });
  }

  [Fact]
  public void Add_cancelled_dialog_changes_nothing()
  {
    WpfStaTestHost.Run(() =>
    {
      var saves = 0;
      using var coordinator = CreateCoordinator(
        pickProgramFile: () => null,
        queueSave: () => saves++);

      coordinator.AddProgram();

      coordinator.ProgramPaths.Should().BeEmpty();
      saves.Should().Be(0);
    });
  }

  [Fact]
  public void Remove_selected_entry_and_queue_save()
  {
    WpfStaTestHost.Run(() =>
    {
      var saves = 0;
      using var coordinator = CreateCoordinator(
        initialPaths: [@"C:\Windows\System32\notepad.exe", @"C:\Windows\System32\calc.exe"],
        queueSave: () => saves++);
      coordinator.WireInstantApply();
      coordinator.SelectProgram(1);

      coordinator.RemoveSelected();

      coordinator.ProgramPaths.Should().Equal(@"C:\Windows\System32\notepad.exe");
      saves.Should().Be(1, "删除后走既有防抖保存链");
      coordinator.IsAddHintVisible().Should().BeFalse();

      coordinator.SelectProgram(0);
      coordinator.RemoveSelected();

      coordinator.ProgramPaths.Should().BeEmpty();
      coordinator.IsAddHintVisible().Should().BeTrue("名单清空后重新显示空名单提示");
    });
  }

  [Fact]
  public void Remove_button_disabled_without_selection()
  {
    WpfStaTestHost.Run(() =>
    {
      using var coordinator = CreateCoordinator(initialPaths: [@"C:\Windows\System32\notepad.exe"]);
      coordinator.WireInstantApply();

      coordinator.RemoveButtonEnabled().Should().BeFalse("未选中条目时删除按钮不可用");

      coordinator.SelectProgram(0);
      coordinator.RemoveButtonEnabled().Should().BeTrue();
    });
  }

  [Fact]
  public void Toggle_change_queues_save_through_existing_chain()
  {
    WpfStaTestHost.Run(() =>
    {
      var saves = 0;
      using var coordinator = CreateCoordinator(queueSave: () => saves++);
      coordinator.WireInstantApply();

      coordinator.SetRestrictionEnabled(true);

      coordinator.RestrictionEnabled.Should().BeTrue();
      saves.Should().Be(1);
    });
  }

  [Fact]
  public void ApplyConfigToUi_reflects_saved_config_fields()
  {
    WpfStaTestHost.Run(() =>
    {
      using var coordinator = CreateCoordinator();

      coordinator.ApplyConfigToUi(new GuardConfig
      {
        ForegroundRestrictionEnabled = true,
        ForegroundProgramPaths = [@"C:\Windows\System32\notepad.exe"],
      });

      coordinator.RestrictionEnabled.Should().BeTrue();
      coordinator.ProgramPaths.Should().Equal(@"C:\Windows\System32\notepad.exe");
    });
  }

  [Fact]
  public void Blocked_status_read_keeps_dispatcher_responsive_and_does_not_start_another_read()
  {
    WpfStaTestHost.Run(() =>
    {
      using var entered = new ManualResetEventSlim();
      using var release = new ManualResetEventSlim();
      using var completed = new ManualResetEventSlim();
      var reads = 0;
      using var coordinator = CreateCoordinator(
        readStatus: () =>
        {
          Interlocked.Increment(ref reads);
          entered.Set();
          release.Wait();
          var status = GuardStatus(GuardStatusCodes.Reasons.WaitingForListedProgram);
          completed.Set();
          return status;
        },
        refreshInterval: TimeSpan.FromMilliseconds(20));
      try
      {
        coordinator.WireInstantApply();
        WpfStaTestHost.WaitUntil(() => entered.IsSet, TimeSpan.FromSeconds(2),
          "the background status read to start");

        // A blocked read must leave the dispatcher free to process several timer ticks.
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        WpfStaTestHost.WaitUntil(() => elapsed.Elapsed >= TimeSpan.FromMilliseconds(150),
          TimeSpan.FromSeconds(2), "the dispatcher to remain responsive during a blocked read");
        Volatile.Read(ref reads).Should().Be(1,
          "the status timer must keep at most one read in flight");
      }
      finally
      {
        coordinator.Dispose();
        release.Set();
        if (entered.IsSet)
          WpfStaTestHost.WaitUntil(() => completed.IsSet, TimeSpan.FromSeconds(2),
            "the released background read to finish before disposing its test signal");
      }
    });
  }

  [Fact]
  public void Dispose_discards_a_status_read_that_completes_after_closing()
  {
    WpfStaTestHost.Run(() =>
    {
      using var entered = new ManualResetEventSlim();
      using var release = new ManualResetEventSlim();
      using var completed = new ManualResetEventSlim();
      var reads = 0;
      var guardText = new TextBlock { Text = "状态未知" };
      using var coordinator = CreateCoordinator(
        guardText: guardText,
        readStatus: () =>
        {
          Interlocked.Increment(ref reads);
          entered.Set();
          release.Wait();
          completed.Set();
          return GuardStatus(GuardStatusCodes.Reasons.PolicySelected);
        },
        refreshInterval: TimeSpan.FromMilliseconds(20));
      try
      {
        coordinator.WireInstantApply();
        WpfStaTestHost.WaitUntil(() => entered.IsSet, TimeSpan.FromSeconds(2),
          "the background status read to start");

        coordinator.Dispose();
        release.Set();
        WpfStaTestHost.WaitUntil(() => completed.IsSet, TimeSpan.FromSeconds(2),
          "the late status read to complete");
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        WpfStaTestHost.WaitUntil(() => elapsed.Elapsed >= TimeSpan.FromMilliseconds(100),
          TimeSpan.FromSeconds(2), "the late status continuation to return to the dispatcher");

        guardText.Text.Should().Be("状态未知",
          "a disposed Settings window must not apply a late read result");
        Volatile.Read(ref reads).Should().Be(1,
          "disposing the coordinator must stop future timer reads");
      }
      finally
      {
        release.Set();
      }
    });
  }

  [Fact]
  public void Guard_claim_expires_while_the_next_status_read_is_blocked()
  {
    WpfStaTestHost.Run(() =>
    {
      var now = DateTimeOffset.UtcNow;
      var firstStatus = new StatusPayload
      {
        timestamp = now.ToString("O"),
        guardSource = GuardStatusCodes.Sources.Automatic,
        guardReason = GuardStatusCodes.Reasons.PolicySelected,
      };
      using var secondReadEntered = new ManualResetEventSlim();
      using var releaseSecondRead = new ManualResetEventSlim();
      using var secondReadCompleted = new ManualResetEventSlim();
      var reads = 0;
      var guardText = new TextBlock { Text = "状态未知" };
      using var coordinator = CreateCoordinator(
        guardText: guardText,
        readStatus: () =>
        {
          if (Interlocked.Increment(ref reads) == 1)
            return firstStatus;
          secondReadEntered.Set();
          releaseSecondRead.Wait();
          secondReadCompleted.Set();
          return firstStatus;
        },
        refreshInterval: TimeSpan.FromMilliseconds(20),
        utcNow: () => now);
      try
      {
        coordinator.WireInstantApply();
        WpfStaTestHost.WaitUntil(() => guardText.Text == "守护中", TimeSpan.FromSeconds(2),
          "the first fresh status to show an active guard claim");
        WpfStaTestHost.WaitUntil(() => secondReadEntered.IsSet, TimeSpan.FromSeconds(2),
          "the next background status read to block");

        now += GuardStatusFreshness.MaxAge + TimeSpan.FromSeconds(1);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        WpfStaTestHost.WaitUntil(() => elapsed.Elapsed >= TimeSpan.FromMilliseconds(120),
          TimeSpan.FromSeconds(2), "the dispatcher to process several ticks while the read is blocked");

        Volatile.Read(ref reads).Should().Be(2,
          "a slow status read must remain single-flight");
        guardText.Text.Should().Be("状态未知",
          "the freshness limit must revoke a stale guard claim without waiting for disk IO");
      }
      finally
      {
        coordinator.Dispose();
        releaseSecondRead.Set();
        if (secondReadEntered.IsSet)
          WpfStaTestHost.WaitUntil(() => secondReadCompleted.IsSet, TimeSpan.FromSeconds(2),
            "the blocked status read to finish before disposing its test signal");
      }
    });
  }

  private static StatusPayload GuardStatus(string reason) => new()
  {
    timestamp = DateTimeOffset.UtcNow.ToString("O"),
    guardSource = reason == GuardStatusCodes.Reasons.PolicySelected
      ? GuardStatusCodes.Sources.Automatic
      : GuardStatusCodes.Sources.None,
    guardReason = reason,
  };

  // ---- helpers ----

  private static SettingsForegroundCoordinator CreateCoordinator(
    IReadOnlyList<string>? initialPaths = null,
    Func<string?>? pickProgramFile = null,
    Action<string, bool>? showFeedback = null,
    Action? queueSave = null,
    TextBlock? guardText = null,
    Func<StatusPayload?>? readStatus = null,
    TimeSpan? refreshInterval = null,
    Func<DateTimeOffset>? utcNow = null)
  {
    var coordinator = new SettingsForegroundCoordinator(
      Path.GetTempPath(),
      new CheckBox(),
      new ListBox(),
      new Button(),
      new Button(),
      guardText ?? new TextBlock(),
      new TextBlock(),
      showFeedback ?? ((_, _) => { }),
      queueSave ?? (() => { }),
      pickProgramFile,
      readStatus,
      refreshInterval,
      utcNow);
    if (initialPaths is { } paths)
    {
      var config = new GuardConfig
      {
        ForegroundRestrictionEnabled = true,
        ForegroundProgramPaths = new List<string>(paths),
      };
      coordinator.ApplyConfigToUi(config);
    }

    return coordinator;
  }
}

/// <summary>测试辅助：以类型安全方式驱动协调器内部控件的少量入口。</summary>
internal static class SettingsForegroundCoordinatorTestExtensions
{
  private static T Control<T>(this SettingsForegroundCoordinator coordinator, string fieldName)
    where T : class
  {
    var field = typeof(SettingsForegroundCoordinator)
      .GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    return (T)field!.GetValue(coordinator)!;
  }

  public static void SetRestrictionEnabled(this SettingsForegroundCoordinator coordinator, bool value)
    => coordinator.Control<CheckBox>("_tglRestriction").IsChecked = value;

  public static void SelectProgram(this SettingsForegroundCoordinator coordinator, int index)
    => coordinator.Control<ListBox>("_lstPrograms").SelectedIndex = index;

  public static bool IsAddHintVisible(this SettingsForegroundCoordinator coordinator)
    => coordinator.Control<TextBlock>("_lblListHint").Visibility == Visibility.Visible;

  public static bool RemoveButtonEnabled(this SettingsForegroundCoordinator coordinator)
    => coordinator.Control<Button>("_btnRemove").IsEnabled;
}
