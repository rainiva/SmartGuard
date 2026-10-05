using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsProgramIdentificationTests
{
  private const string Launcher = @"C:\Apps\launcher.exe";
  private const string Document = @"C:\Apps\document.exe";

  [Fact]
  public void Wps_host_mismatch_explains_outer_window_without_verifying_the_pdf_component()
  {
    WpfStaTestHost.Run(() =>
    {
      var list = new ListBox();
      var hint = new TextBlock();
      using var coordinator = Create(list, () => new("WPS Office", @"C:\Apps\wps.exe"),
        (_, _) => false, () => throw new Exception("must not save"), hint: hint);
      coordinator.ApplyConfigToUi(new GuardConfig { ForegroundProgramPaths = [@"C:\Apps\wpspdf.exe"] });
      list.SelectedIndex = 0;
      Complete(Begin(coordinator, true));
      hint.Text.Should().Contain("外层窗口").And.Contain("整合窗口").And.Contain("wpspdf.exe");
      ((SettingsProgramCandidate)list.Items[0]).VerificationText.Should().NotContain("实际组件").And.NotContain("已确认");
      coordinator.ProgramPaths.Should().Equal(@"C:\Apps\wpspdf.exe");
    });
  }

  [Fact]
  public void Cancelling_a_path_repair_preserves_the_original_list_and_does_not_save()
  {
    WpfStaTestHost.Run(() =>
    {
      var list = new ListBox();
      using var coordinator = Create(list, () => new("文档", Document), (_, _) => false,
        () => throw new Exception("must not save"), path => path != Launcher);
      coordinator.ApplyConfigToUi(new GuardConfig { ForegroundProgramPaths = [Launcher] });
      list.SelectedIndex = 0;
      Complete(Begin(coordinator, true));
      coordinator.ProgramPaths.Should().Equal(Launcher);
      ((SettingsProgramCandidate)list.Items[0]).VerificationText.Should().Contain("原路径不可用");
    });
  }

  [Fact]
  public void A_modal_confirmation_cannot_replace_a_reloaded_entry_and_duplicate_actual_paths_merge()
  {
    WpfStaTestHost.Run(() =>
    {
      var list = new ListBox();
      var saves = 0;
      SettingsForegroundCoordinator? owner = null;
      var reload = true;
      var feedback = new List<string>();
      var config = new GuardConfig { ForegroundProgramPaths = [Launcher, Document] };
      using var coordinator = Create(list, () => new("文档", Document), (_, _) =>
      {
        if (reload) owner!.ApplyConfigToUi(config);
        return true;
      }, () => saves++, path => path != Launcher, feedback: feedback.Add);
      owner = coordinator;
      coordinator.ApplyConfigToUi(config);
      list.SelectedIndex = 0;
      Complete(Begin(coordinator, true));
      coordinator.ProgramPaths.Should().Equal(Launcher, Document);
      saves.Should().Be(0);
      reload = false;
      list.SelectedIndex = 0;
      Complete(Begin(coordinator, true));
      coordinator.ProgramPaths.Should().Equal([Document], string.Join(" | ", feedback));
      saves.Should().Be(1);
    });
  }

  [Fact]
  public void Unavailable_paths_are_explained_once_and_rechecked_only_after_explicit_refresh()
  {
    WpfStaTestHost.Run(() =>
    {
      var list = new ListBox();
      var lookups = 0;
      var available = false;
      using var coordinator = new SettingsForegroundCoordinator(Path.GetTempPath(), new CheckBox(), list,
        new Button(), new Button(), new TextBlock(), new TextBlock(), (_, _) => { }, () => throw new Exception("must not save"),
        readStatus: () => null, refreshInterval: TimeSpan.FromMilliseconds(15), verificationHint: new TextBlock(),
        programExists: _ => { Interlocked.Increment(ref lookups); return available; });
      coordinator.ApplyConfigToUi(new GuardConfig { ForegroundProgramPaths = [Launcher] });
      coordinator.WireInstantApply();
      var entry = (SettingsProgramCandidate)list.Items[0];
      WpfStaTestHost.WaitUntil(() => entry.VerificationText.Contains("路径不存在"), TimeSpan.FromSeconds(2), "path warning");
      var elapsed = System.Diagnostics.Stopwatch.StartNew();
      WpfStaTestHost.WaitUntil(() => elapsed.ElapsedMilliseconds >= 100, TimeSpan.FromSeconds(2), "several status ticks");
      Volatile.Read(ref lookups).Should().Be(1, "unchanged paths do not need recurring disk scans");
      available = true;
      coordinator.RequestPathCheck();
      WpfStaTestHost.WaitUntil(() => entry.VerificationText == "尚未验证", TimeSpan.FromSeconds(2), "restored path is not automatically verified");
      Volatile.Read(ref lookups).Should().Be(2);
    });
  }

  [Fact]
  public void A_reloaded_same_path_list_during_capture_cannot_receive_the_previous_operations_confirmation()
  {
    WpfStaTestHost.Run(() =>
    {
      var list = new ListBox();
      var delay = new TaskCompletionSource();
      var confirmations = 0;
      using var coordinator = Create(list, () => new("文档", Document),
        (_, _) => { confirmations++; return true; }, () => throw new Exception("must not save"), wait: _ => delay.Task);
      var config = new GuardConfig { ForegroundProgramPaths = [Launcher] };
      coordinator.ApplyConfigToUi(config);
      list.SelectedIndex = 0;
      var operation = Begin(coordinator, true);
      coordinator.ApplyConfigToUi(config);
      delay.SetResult();
      Complete(operation);
      confirmations.Should().Be(0);
      coordinator.ProgramPaths.Should().Equal(Launcher);
    });
  }

  [Theory]
  [InlineData(null)]
  [InlineData(@"\\server\share\document.exe")]
  [InlineData("document.exe")]
  public void Unknown_network_or_nonabsolute_identity_never_reaches_confirmation(string? path)
  {
    WpfStaTestHost.Run(() =>
    {
      var list = new ListBox();
      var confirmations = 0;
      using var coordinator = Create(list, () => path is null ? null : new("文档", path),
        (_, _) => { confirmations++; return true; }, () => throw new Exception("must not save"));
      Complete(Begin(coordinator));
      confirmations.Should().Be(0);
      coordinator.ProgramPaths.Should().BeEmpty();
    });
  }

  [Fact]
  public void Window_capture_requires_confirmation_and_adds_only_actual_component()
  {
    WpfStaTestHost.Run(() =>
    {
      var list = new ListBox();
      var saves = 0;
      var confirm = false;
      using var coordinator = Create(list, () => new("文档", Document), (_, _) => confirm, () => saves++);
      Complete(Begin(coordinator));
      list.Items.Count.Should().Be(0);
      saves.Should().Be(0);
      confirm = true;
      Complete(Begin(coordinator));
      coordinator.ProgramPaths.Should().Equal(Document);
      ((SettingsProgramCandidate)list.Items[0]).VerificationText.Should().Contain("已确认前台匹配");
      saves.Should().Be(1);
    });
  }

  [Fact]
  public void Verification_matches_exact_path_and_does_not_expand_suite_authorization()
  {
    WpfStaTestHost.Run(() =>
    {
      var list = new ListBox();
      var confirmations = 0;
      var saves = 0;
      using var coordinator = Create(list, () => new("文档", Document.ToUpperInvariant()),
        (_, _) => { confirmations++; return true; }, () => saves++);
      coordinator.ApplyConfigToUi(new GuardConfig { ForegroundProgramPaths = [Document, Launcher] });
      list.SelectedIndex = 0;
      Complete(Begin(coordinator, true));
      ((SettingsProgramCandidate)list.Items[0]).VerificationText.Should().Contain("已确认前台匹配");
      ((SettingsProgramCandidate)list.Items[1]).VerificationText.Should().Be("尚未验证");
      confirmations.Should().Be(0);
      saves.Should().Be(0);
    });
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void Mismatch_adds_a_component_or_explicitly_replaces_only_an_unavailable_old_path(bool oldExists)
  {
    WpfStaTestHost.Run(() =>
    {
      var list = new ListBox();
      string? proposedReplacement = null;
      var saves = 0;
      using var coordinator = Create(list, () => new("文档", Document),
        (_, old) => { proposedReplacement = old; return true; }, () => saves++, path => path != Launcher || oldExists);
      coordinator.ApplyConfigToUi(new GuardConfig { ForegroundProgramPaths = [Launcher, @"C:\Apps\other.exe"] });
      list.SelectedIndex = 0;
      Complete(Begin(coordinator, true));
      proposedReplacement.Should().Be(oldExists ? null : Launcher);
      coordinator.ProgramPaths.Should().Contain(Document).And.Contain(@"C:\Apps\other.exe");
      coordinator.ProgramPaths.Contains(Launcher).Should().Be(oldExists);
      saves.Should().Be(1);
    });
  }

  [Fact]
  public void Unidentified_or_throwing_reader_shows_failure_and_never_adds_a_guess()
  {
    WpfStaTestHost.Run(() =>
    {
      var list = new ListBox();
      var feedback = new List<string>();
      using var coordinator = Create(list, () => throw new IOException("unavailable"), (_, _) => true,
        () => throw new Exception("must not save"), feedback: feedback.Add);
      Complete(Begin(coordinator));
      coordinator.ProgramPaths.Should().BeEmpty();
      feedback.Should().Contain(message => message.Contains("无法识别"));
    });
  }

  [Fact]
  public void Repeated_click_during_capture_is_single_flight_and_close_discards_late_result()
  {
    WpfStaTestHost.Run(() =>
    {
      var list = new ListBox();
      var delay = new TaskCompletionSource();
      var waits = 0;
      var confirmations = 0;
      CancellationToken cancellation = default;
      using var coordinator = Create(list, () => new("文档", Document),
        (_, _) => { confirmations++; return true; }, () => throw new Exception("must not save"),
        wait: token => { waits++; cancellation = token; return delay.Task; });
      var first = Begin(coordinator);
      Complete(Begin(coordinator));
      waits.Should().Be(1);
      coordinator.Dispose();
      cancellation.IsCancellationRequested.Should().BeTrue();
      delay.SetResult();
      Complete(first);
      list.Items.Count.Should().Be(0);
      confirmations.Should().Be(0);
    });
  }

  private static SettingsForegroundCoordinator Create(ListBox list, Func<SettingsProgramCandidate?> read,
    Func<SettingsProgramCandidate, string?, bool> confirm, Action save, Func<string, bool>? exists = null,
    Func<CancellationToken, Task>? wait = null, Action<string>? feedback = null, TextBlock? hint = null)
  {
    return new SettingsForegroundCoordinator(Path.GetTempPath(), new CheckBox(), list,
      new Button(), new Button(), new TextBlock(), new TextBlock(), (message, _) => feedback?.Invoke(message), save,
      verificationHint: hint, readWindow: read, confirmWindow: confirm, programExists: exists ?? (_ => true),
      waitForWindow: wait ?? (_ => Task.CompletedTask));
  }

  private static Task Begin(SettingsForegroundCoordinator coordinator, bool verify = false)
  {
    return coordinator.IdentifyWindowAsync(verify);
  }

  private static void Complete(Task task)
  {
    WpfStaTestHost.WaitUntil(() => task.IsCompleted, TimeSpan.FromSeconds(3), "window identification completion");
    task.IsCompletedSuccessfully.Should().BeTrue();
  }
}
