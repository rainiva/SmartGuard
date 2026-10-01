using System.Text.Json;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Settings.Tests;

/// <summary>
/// 任务 4：前台程序限制的轻量设置与状态展示（窗口级行为）。
/// UI 只编辑配置（经 ReadConfigFromUi → 防抖保存链）并展示 Engine 发布的守护状态；
/// 等待名单等守护原因不得切换暂停开关；旧状态缺字段或状态过期显示"状态未知"。
/// </summary>
[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsForegroundUiTests
{
  private const string NotepadPath = @"C:\Windows\System32\notepad.exe";

  [Fact]
  public void Foreground_toggle_flows_into_read_config_from_ui()
  {
    RunOnSta(() =>
    {
      using var scope = ForegroundScope.Create(config => { });
      scope.OpenWindow();
      var toggle = Find<CheckBox>(scope.Window!, "tglForegroundRestriction");
      toggle.Should().NotBeNull("设置窗口须提供前台程序限制开关");

      toggle!.IsChecked = true;

      scope.Controller!.ReadConfigFromUi().ForegroundRestrictionEnabled.Should().BeTrue();
    });
  }

  [Fact]
  public void Foreground_list_flows_into_read_config_and_survives_config_round_trip()
  {
    RunOnSta(() =>
    {
      using var scope = ForegroundScope.Create(config =>
      {
        config.ForegroundRestrictionEnabled = true;
        config.ForegroundProgramPaths = [NotepadPath];
      });
      scope.OpenWindow();
      var list = Find<ListBox>(scope.Window!, "lstForegroundPrograms");
      list.Should().NotBeNull();

      list!.Items.OfType<object>().Select(item => item?.ToString()).Should().Contain(NotepadPath);

      var fromUi = scope.Controller!.ReadConfigFromUi();
      fromUi.ForegroundRestrictionEnabled.Should().BeTrue();
      fromUi.ForegroundProgramPaths.Should().Equal(NotepadPath);

      scope.Repository.Save(fromUi);
      var reloaded = scope.Repository.TryLoad();
      reloaded.Should().NotBeNull();
      reloaded!.ForegroundRestrictionEnabled.Should().BeTrue("配置须原样落盘并在重启后保留");
      reloaded.ForegroundProgramPaths.Should().Equal(NotepadPath);
    });
  }

  [Fact]
  public void Empty_list_with_restriction_on_shows_add_program_hint()
  {
    RunOnSta(() =>
    {
      using var scope = ForegroundScope.Create(config =>
      {
        config.ForegroundRestrictionEnabled = true;
        config.ForegroundProgramPaths = [];
      });
      scope.OpenWindow();
      var hint = Find<TextBlock>(scope.Window!, "lblForegroundListHint");
      hint.Should().NotBeNull("空名单须提供添加程序提示");
      hint!.Visibility.Should().Be(Visibility.Visible);
      hint.Text.Should().Be("添加程序后生效");
    });
  }

  [Fact]
  public void Guard_status_area_maps_waiting_reason_from_status_json()
  {
    RunOnSta(() =>
    {
      using var scope = ForegroundScope.Create(
        config => { },
        status =>
        {
          status.guardSource = GuardStatusCodes.Sources.None;
          status.guardReason = GuardStatusCodes.Reasons.WaitingForListedProgram;
        });
      scope.OpenWindow();
      var statusText = Find<TextBlock>(scope.Window!, "txtForegroundGuardStatus");
      statusText.Should().NotBeNull("设置窗口须提供自动守护状态区");

      statusText!.Text.Should().Be("等待指定程序");
    });
  }

  [Fact]
  public void Waiting_reason_does_not_flip_pause_toggle()
  {
    RunOnSta(() =>
    {
      using var scope = ForegroundScope.Create(
        config => config.Paused = true,
        status =>
        {
          status.guardSource = GuardStatusCodes.Sources.None;
          status.guardReason = GuardStatusCodes.Reasons.WaitingForListedProgram;
        });
      scope.OpenWindow();

      var tglPaused = Find<CheckBox>(scope.Window!, "tglPaused")!;
      tglPaused.IsChecked.Should().BeFalse(
        "status.paused=false 的等待名单状态不得切换暂停开关（暂停展示只读 status.paused）");
      Find<TextBlock>(scope.Window!, "txtForegroundGuardStatus")!.Text.Should().Be("等待指定程序");
    });
  }

  [Fact]
  public void Old_status_without_guard_fields_shows_unknown_not_a_guarding_claim()
  {
    RunOnSta(() =>
    {
      using var scope = ForegroundScope.CreateRawStatus(
        config => { },
        $$"""{"timestamp":"{{DateTimeOffset.UtcNow.ToString("O")}}","currentPlan":"高性能","paused":false}""");
      scope.OpenWindow();

      Find<TextBlock>(scope.Window!, "txtForegroundGuardStatus")!.Text
        .Should().Be("状态未知", "旧状态缺守护字段时不得显示确定的正在守护");
    });
  }

  [Fact]
  public void Stale_status_shows_unknown_even_with_waiting_reason()
  {
    RunOnSta(() =>
    {
      using var scope = ForegroundScope.Create(
        config => { },
        status => status.guardReason = GuardStatusCodes.Reasons.WaitingForListedProgram,
        timestampUtc: DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));
      scope.OpenWindow();

      Find<TextBlock>(scope.Window!, "txtForegroundGuardStatus")!.Text
        .Should().Be("状态未知", "状态过期不得显示确定的正在守护");
    });
  }

  [Fact]
  public void Open_window_updates_guard_text_when_engine_publishes_a_new_reason()
  {
    RunOnSta(() =>
    {
      using var scope = ForegroundScope.Create(
        config => { },
        status =>
        {
          status.guardSource = GuardStatusCodes.Sources.Automatic;
          status.guardReason = GuardStatusCodes.Reasons.PolicySelected;
        });
      scope.OpenWindow();
      WpfStaTestHost.ShowAndWait(scope.Window!);
      var guardText = Find<TextBlock>(scope.Window!, "txtForegroundGuardStatus")!;
      guardText.Text.Should().Be("守护中");

      scope.WriteStatus(GuardStatusCodes.Sources.None,
        GuardStatusCodes.Reasons.WaitingForListedProgram);

      WpfStaTestHost.WaitUntil(
        () => guardText.Text == "等待指定程序",
        TimeSpan.FromSeconds(7),
        "the open Settings window to display the Engine's new guard reason");
    });
  }

  [Fact]
  public void Closed_window_stops_applying_later_guard_status_changes()
  {
    RunOnSta(() =>
    {
      using var scope = ForegroundScope.Create(
        config => { },
        status =>
        {
          status.guardSource = GuardStatusCodes.Sources.None;
          status.guardReason = GuardStatusCodes.Reasons.WaitingForListedProgram;
        });
      scope.OpenWindow();
      WpfStaTestHost.ShowAndWait(scope.Window!);
      var guardText = Find<TextBlock>(scope.Window!, "txtForegroundGuardStatus")!;
      guardText.Text.Should().Be("等待指定程序");

      scope.Window!.Close();
      scope.WriteStatus(GuardStatusCodes.Sources.Automatic,
        GuardStatusCodes.Reasons.PolicySelected);
      var elapsed = Stopwatch.StartNew();
      WpfStaTestHost.WaitUntil(
        () => elapsed.Elapsed >= TimeSpan.FromSeconds(6),
        TimeSpan.FromSeconds(7),
        "a closed Settings window to remain quiescent beyond one status refresh interval");

      guardText.Text.Should().Be("等待指定程序",
        "closing the window must stop its guard-status refresh lifecycle");
    });
  }

  // ---- helpers ----

  private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);

  private static T? Find<T>(Window window, string name) where T : class
    => window.FindName(name) as T;

  /// <summary>临时配置/状态环境：创建仓库，窗口控制器按需打开，结束后清理临时目录。</summary>
  private sealed class ForegroundScope : IDisposable
  {
    private ForegroundScope(string root, GuardConfigRepository repository)
    {
      Root = root;
      Repository = repository;
    }

    public string Root { get; }
    public GuardConfigRepository Repository { get; }
    public SettingsWindowController? Controller { get; private set; }
    public Window? Window { get; private set; }

    public static ForegroundScope Create(
      Action<GuardConfig> configure,
      Action<StatusPayload>? configureStatus = null,
      DateTimeOffset? timestampUtc = null)
    {
      var scope = CreateScope(configure);
      if (configureStatus is not null)
      {
        var status = new StatusPayload
        {
          timestamp = (timestampUtc ?? DateTimeOffset.UtcNow).ToString("O"),
          currentPlan = "高性能",
          paused = false,
        };
        configureStatus(status);
        File.WriteAllText(
          SmartGuardPaths.StatusFile(scope.Root),
          JsonSerializer.Serialize(status));
      }

      return scope;
    }

    public static ForegroundScope CreateRawStatus(
      Action<GuardConfig> configure,
      string rawStatusJson)
    {
      var scope = CreateScope(configure);
      File.WriteAllText(SmartGuardPaths.StatusFile(scope.Root), rawStatusJson);
      return scope;
    }

    private static ForegroundScope CreateScope(Action<GuardConfig> configure)
    {
      var root = Path.Combine(
        Path.GetTempPath(), "SmartGuardForegroundUi_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(root);
      var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(root));
      var config = repository.LoadOrDefault(root);
      configure(config);
      repository.Save(config);
      return new ForegroundScope(root, repository);
    }

    public void OpenWindow()
    {
      var config = Repository.TryLoad() ?? Repository.LoadOrDefault(Root);
      Controller = SettingsWindowController.TryCreate(Root, Repository, config);
      Controller.Should().NotBeNull("设置窗口须能以默认 XAML 创建");
      Window = (Window)typeof(SettingsWindowController)
        .GetField("_window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
        .GetValue(Controller!)!;
    }

    public void WriteStatus(string source, string reason)
    {
      var status = new StatusPayload
      {
        timestamp = DateTimeOffset.UtcNow.ToString("O"),
        currentPlan = "高性能",
        paused = false,
        guardSource = source,
        guardReason = reason,
      };
      File.WriteAllText(
        SmartGuardPaths.StatusFile(Root),
        JsonSerializer.Serialize(status),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void Dispose()
    {
      Controller?.Dispose();
      try { Directory.Delete(Root, true); } catch { }
    }
  }
}
