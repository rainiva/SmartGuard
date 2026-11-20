using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsProgramSearchTests
{
  [Fact]
  public void Search_can_refresh_after_a_program_is_opened_without_reopening_settings()
  {
    WpfStaTestHost.Run(() =>
    {
      var query = new TextBox();
      var results = new ListBox();
      var loads = 0;
      using var search = new SettingsProgramSearchCoordinator(query, results, new TextBlock(), _ => { },
        _ => Task.FromResult<IReadOnlyList<SettingsProgramCandidate>>(++loads == 1 ? [] : [new("文档", @"C:\Apps\document.exe")]));
      query.Text = "文档";
      results.Items.Count.Should().Be(0);
      search.Refresh();
      results.Items.Count.Should().Be(1);
      loads.Should().Be(2);
    });
  }

  [Fact]
  public void Candidates_explain_their_discovery_source_without_claiming_a_guard_match()
  {
    var candidate = new SettingsProgramCandidate("文档", @"C:\Apps\launcher.exe");
    candidate.SourceText.Should().Be("浏览文件或已有名单 · 尚未验证");
    new SettingsProgramCandidate(candidate.Name, candidate.ExecutablePath, source: ProgramCandidateSource.Shortcut)
      .SourceText.Should().Contain("快捷方式");
    new SettingsProgramCandidate(candidate.Name, candidate.ExecutablePath, source: ProgramCandidateSource.RunningWindow)
      .SourceText.Should().Be("运行中的窗口程序");
    var normalized = SettingsProgramCatalog.Normalize([
      new(candidate.Name, candidate.ExecutablePath, source: ProgramCandidateSource.RelatedComponent),
      new(candidate.Name, candidate.ExecutablePath, source: ProgramCandidateSource.RunningWindow)]);
    normalized.Single().Source.Should().Be(ProgramCandidateSource.RunningWindow);
  }

  [Fact]
  public void Refresh_is_single_flight_preserves_the_latest_query_and_cannot_run_after_disposal()
  {
    WpfStaTestHost.Run(() =>
    {
      var query = new TextBox();
      var results = new ListBox();
      var refresh = new Button();
      var pending = new TaskCompletionSource<IReadOnlyList<SettingsProgramCandidate>>();
      var loads = 0;
      using var search = new SettingsProgramSearchCoordinator(query, results, new TextBlock(), _ => { },
        _ => { loads++; return pending.Task; }, refresh);
      query.Text = "文档";
      refresh.IsEnabled.Should().BeFalse();
      search.Refresh();
      query.Text = "表格";
      loads.Should().Be(1);
      pending.SetResult([new("表格", @"C:\Apps\sheet.exe")]);
      WpfStaTestHost.WaitUntil(() => results.Items.Count == 1, TimeSpan.FromSeconds(2), "latest query after refresh");
      refresh.IsEnabled.Should().BeTrue();
      search.Dispose();
      search.Refresh();
      loads.Should().Be(1);
    });
  }

  [Fact]
  public void Wps_components_are_separate_clear_candidates_and_launcher_is_not_a_document_program()
  {
    var candidates = SettingsProgramCatalog.Normalize([
      new SettingsProgramCandidate("WPS Office", @"D:\Apps\WPS\ksolaunch.exe"),
      new SettingsProgramCandidate("WPS Office", @"D:\Apps\WPS\office6\wps.exe"),
      new SettingsProgramCandidate("WPS Spreadsheets", @"D:\Apps\WPS\office6\et.exe"),
      new SettingsProgramCandidate("WPS Presentation", @"D:\Apps\WPS\office6\wpp.exe"),
      new SettingsProgramCandidate("WPS PDF", @"D:\Apps\WPS\office6\wpspdf.exe"),
    ]);
    candidates.Should().HaveCount(4);
    SettingsProgramCatalog.Search(candidates, "WPS").Select(x => x.Name)
      .Should().BeEquivalentTo("WPS Office", "WPS 表格", "WPS 演示", "WPS PDF");
    SettingsProgramCatalog.Search(candidates, "表格").Single().ExecutablePath.Should().EndWith(@"office6\et.exe");
  }

  [Fact]
  public void Exact_program_name_ranks_first_and_uninstall_shortcuts_are_not_candidates()
  {
    var candidates = SettingsProgramCatalog.Normalize([
      new SettingsProgramCandidate("卸载微信", @"C:\Apps\Uninstall.exe"),
      new SettingsProgramCandidate("Uninstall WeChat", @"C:\Apps\unins000.exe"),
      new SettingsProgramCandidate("企业微信", @"C:\Apps\WXWork.exe"),
      new SettingsProgramCandidate("微信输入法", @"C:\Apps\WeType.exe"),
      new SettingsProgramCandidate("微信", @"C:\Apps\Weixin.exe"),
    ]);
    candidates.Should().HaveCount(3);
    SettingsProgramCatalog.Search(candidates, "微信").First().Name.Should().Be("微信");
  }

  [Fact]
  public void Chinese_name_and_executable_name_find_the_same_concrete_program()
  {
    var candidates = SettingsProgramCatalog.Normalize([
      new SettingsProgramCandidate("微信", @"C:\Apps\WeChat.exe"),
      new SettingsProgramCandidate("WeChat", @"c:\apps\WECHAT.exe"),
      new SettingsProgramCandidate("文档", @"C:\Apps\readme.txt"),
    ]);
    candidates.Should().ContainSingle();
    SettingsProgramCatalog.Search(candidates, "微信").Single().ExecutablePath.Should().Be(@"C:\Apps\WeChat.exe");
    SettingsProgramCatalog.Search(candidates, "wechat").Should().ContainSingle();
    SettingsProgramCatalog.Search(candidates, "").Should().BeEmpty();
  }

  [Fact]
  public void Same_name_different_paths_remain_separate_and_results_are_bounded()
  {
    var candidates = SettingsProgramCatalog.Normalize(Enumerable.Range(0, 600)
      .Select(index => new SettingsProgramCandidate("微信", $@"C:\Apps\{index}\WeChat.exe")));
    candidates.Count.Should().Be(512);
    SettingsProgramCatalog.Search(candidates, "微信").Count.Should().Be(20);
    candidates.Select(item => item.ExecutablePath).Distinct().Count().Should().Be(512);
  }

  [Fact]
  public void Typing_does_not_add_until_a_concrete_search_result_is_selected()
  {
    WpfStaTestHost.Run(() =>
    {
      var query = new TextBox();
      var results = new ListBox();
      var added = new List<SettingsProgramCandidate>();
      using var search = new SettingsProgramSearchCoordinator(query, results, new TextBlock(), added.Add,
        _ => Task.FromResult<IReadOnlyList<SettingsProgramCandidate>>([
          new("微信", @"C:\Apps\WeChat.exe"), new("Chrome", @"C:\Apps\chrome.exe")
        ]));
      query.Text = "微信";
      WpfStaTestHost.WaitUntil(() => results.Items.Count == 1, TimeSpan.FromSeconds(2), "Chinese search results");
      added.Should().BeEmpty();
      search.AcceptSelected();
      added.Should().BeEmpty("unselected free text must never authorize a program");
      results.SelectedIndex = 0;
      search.AcceptSelected();
      added.Single().ExecutablePath.Should().Be(@"C:\Apps\WeChat.exe");
      query.Text.Should().BeEmpty();
    });
  }

  [Fact]
  public void Latest_query_wins_and_disposal_cancels_single_pending_discovery()
  {
    WpfStaTestHost.Run(() =>
    {
      var query = new TextBox();
      var results = new ListBox();
      var hint = new TextBlock();
      var pending = new TaskCompletionSource<IReadOnlyList<SettingsProgramCandidate>>();
      var loads = 0;
      CancellationToken token = default;
      using var search = new SettingsProgramSearchCoordinator(query, results, hint, _ => { }, cancellation =>
      {
        token = cancellation;
        loads++;
        return pending.Task;
      });
      query.Text = "微信";
      query.Text = "chrome";
      loads.Should().Be(1);
      search.Dispose();
      token.IsCancellationRequested.Should().BeTrue();
      pending.SetResult([new("Chrome", @"C:\Apps\chrome.exe")]);
      WpfStaTestHost.WaitUntil(() => pending.Task.IsCompleted, TimeSpan.FromSeconds(2), "late discovery completion");
      results.Items.Cast<object>().Should().BeEmpty();
    });
  }

  [Fact]
  public void Keyboard_selection_adds_the_chosen_result_and_escape_clears_suggestions()
  {
    WpfStaTestHost.Run(() =>
    {
      var query = new TextBox();
      var results = new ListBox();
      var added = new List<SettingsProgramCandidate>();
      using var search = new SettingsProgramSearchCoordinator(query, results, new TextBlock(), added.Add,
        _ => Task.FromResult<IReadOnlyList<SettingsProgramCandidate>>([new("微信", @"C:\Apps\WeChat.exe")]));
      query.Text = "微信";
      void Key(System.Windows.Input.Key key) => query.RaiseEvent(new System.Windows.Input.KeyEventArgs(
        System.Windows.Input.Keyboard.PrimaryDevice, new TestPresentationSource(), 0, key)
        { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
      Key(System.Windows.Input.Key.Down);
      Key(System.Windows.Input.Key.Enter);
      added.Should().ContainSingle();
      query.Text = "微信";
      Key(System.Windows.Input.Key.Escape);
      query.Text.Should().BeEmpty();
      results.Visibility.Should().Be(System.Windows.Visibility.Collapsed);
    });
  }

  [Fact]
  public void A_slow_discovery_filters_the_latest_query_and_a_failed_load_can_retry()
  {
    WpfStaTestHost.Run(() =>
    {
      var query = new TextBox();
      var results = new ListBox();
      var hint = new TextBlock();
      var pending = new TaskCompletionSource<IReadOnlyList<SettingsProgramCandidate>>();
      var attempts = 0;
      using var search = new SettingsProgramSearchCoordinator(query, results, hint, _ => { }, _ =>
        ++attempts == 1 ? Task.FromException<IReadOnlyList<SettingsProgramCandidate>>(new IOException("unavailable")) : pending.Task);
      query.Text = "微信";
      hint.Text.Should().Contain("无法读取");
      query.Text = "chrome";
      query.Text = "微信";
      attempts.Should().Be(2);
      pending.SetResult([new("微信", @"C:\Apps\WeChat.exe"), new("Chrome", @"C:\Apps\chrome.exe")]);
      WpfStaTestHost.WaitUntil(() => results.Items.Count == 1, TimeSpan.FromSeconds(2), "latest query after slow discovery");
      ((SettingsProgramCandidate)results.Items[0]).Name.Should().Be("微信");
    });
  }

  private sealed class TestPresentationSource : System.Windows.PresentationSource
  {
    public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
    public override bool IsDisposed => false;
    protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
  }

  [Fact]
  public void A_full_program_list_rejects_the_next_candidate_without_unsaved_ui_state()
  {
    WpfStaTestHost.Run(() =>
    {
      var saves = 0;
      var feedback = new List<string>();
      using var foreground = new SettingsForegroundCoordinator(Path.GetTempPath(), new CheckBox(), new ListBox(),
        new Button(), new Button(), new TextBlock(), new TextBlock(),
        (message, _) => feedback.Add(message), () => saves++);
      foreground.ApplyConfigToUi(new GuardConfig { ForegroundProgramPaths = Enumerable.Range(0, 128)
        .Select(index => $@"C:\Apps\{index}.exe").ToList() });
      foreground.AddProgramPath(@"C:\Apps\WeChat.exe", "微信");
      foreground.ProgramPaths.Count.Should().Be(128);
      saves.Should().Be(0);
      feedback.Single().Should().Contain("128");
    });
  }

  [Fact]
  public void Selected_search_result_uses_existing_path_validation_and_save_chain()
  {
    WpfStaTestHost.Run(() =>
    {
      var saves = 0;
      var feedback = new List<string>();
      using var foreground = new SettingsForegroundCoordinator(Path.GetTempPath(), new CheckBox(), new ListBox(),
        new Button(), new Button(), new TextBlock(), new TextBlock(),
        (message, _) => feedback.Add(message), () => saves++);
      foreground.AddProgramPath(@"C:\Apps\WeChat.exe", "微信");
      foreground.AddProgramPath(@"c:\apps\WECHAT.exe", "微信");
      foreground.AddProgramPath("微信", "微信");
      foreground.ProgramPaths.Should().Equal(@"C:\Apps\WeChat.exe");
      saves.Should().Be(1);
      feedback.Should().HaveCount(2);
    });
  }
}
