using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Settings;

/// <summary>
/// 前台程序限制设置 Owner（轻量：一个开关、名单、添加和删除）。
/// UI 只编辑配置、展示 Engine 发布的守护状态：名单条目经
/// <see cref="GuardConfigValidator"/> 共享规则规范化与 OrdinalIgnoreCase 去重，
/// 保存走既有 ReadConfigFromUi → SettingsSnapshotMapper → 防抖保存链
/// （添加前拒绝超限，其他校验失败沿用保存链提示），不直接写配置仓库；
/// 状态区只读 status.json 共享读取链，不维护第二套路径规则、状态解析或健康存储。
/// </summary>
internal sealed partial class SettingsForegroundCoordinator : IDisposable
{
  private const string AddDialogFilter = "可执行程序 (*.exe)|*.exe";

  private readonly CheckBox _tglRestriction;
  private readonly NumberBox? _observationDuration;
  private readonly ListBox _lstPrograms;
  private readonly Button _btnAdd;
  private readonly Button _btnRemove;
  private readonly TextBlock _txtGuardStatus;
  private readonly TextBlock _lblListHint;
  private readonly Action<string, bool> _showFeedback;
  private readonly Action _queueSave;
  private readonly string _statusPath;
  private readonly Func<string?> _pickProgramFile;
  private bool _wired;
  private readonly Func<StatusPayload?> _readStatus;
  private readonly Func<DateTimeOffset> _utcNow;
  private readonly DispatcherTimer _statusTimer;
  private bool _refreshInFlight;
  private bool _pathCheckRequested;
  private bool _disposed;
  private StatusPayload? _displayedStatus;

  internal SettingsForegroundCoordinator(
    string root,
    CheckBox tglRestriction,
    ListBox lstPrograms,
    Button btnAdd,
    Button btnRemove,
    TextBlock txtGuardStatus,
    TextBlock lblListHint,
    Action<string, bool> showFeedback,
    Action queueSave,
    Func<string?>? pickProgramFile = null,
    Func<StatusPayload?>? readStatus = null,
    TimeSpan? refreshInterval = null,
    Func<DateTimeOffset>? utcNow = null,
    NumberBox? observationDuration = null,
    Button? identifyProgram = null,
    Button? verifyProgram = null,
    TextBlock? verificationHint = null,
    Window? window = null,
    Func<SettingsProgramCandidate?>? readWindow = null,
    Func<SettingsProgramCandidate, string?, bool>? confirmWindow = null,
    Func<string, bool>? programExists = null,
    Func<CancellationToken, Task>? waitForWindow = null)
  {
    _tglRestriction = tglRestriction;
    _observationDuration = observationDuration;
    _lstPrograms = lstPrograms;
    _btnAdd = btnAdd;
    _btnRemove = btnRemove;
    _txtGuardStatus = txtGuardStatus;
    _lblListHint = lblListHint;
    _showFeedback = showFeedback;
    _queueSave = queueSave;
    _statusPath = SmartGuardPaths.StatusFile(root);
    _pickProgramFile = pickProgramFile ?? PickProgramFileWithDialog;
    _readStatus = readStatus ?? (() => StatusJsonReader.TryRead(_statusPath));
    _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    _identifyProgram = identifyProgram;
    _verifyProgram = verifyProgram;
    _verificationHint = verificationHint;
    _window = window;
    _readWindow = readWindow ?? SettingsProgramCatalog.ReadForegroundCandidate;
    _confirmWindow = confirmWindow ?? ConfirmWindow;
    _programExists = programExists ?? System.IO.File.Exists;
    _waitForWindow = waitForWindow ?? (token => Task.Delay(TimeSpan.FromSeconds(5), token));
    _statusTimer = new DispatcherTimer(DispatcherPriority.Background, txtGuardStatus.Dispatcher)
    {
      Interval = refreshInterval ?? TimeSpan.FromSeconds(5),
    };
    _statusTimer.Tick += OnStatusTick;
  }

  internal bool RestrictionEnabled => _tglRestriction.IsChecked == true;
  internal int ObservationSeconds => _observationDuration?.Value ?? 180;

  internal IReadOnlyList<string> ProgramPaths
    => _lstPrograms.Items.OfType<object>().Select(item => item?.ToString() ?? string.Empty).ToList();

  /// <summary>开关与名单来自配置；状态区只读 status.json（与暂停初始展示同一读取链）。</summary>
  internal void ApplyConfigToUi(GuardConfig config)
  {
    _tglRestriction.IsChecked = config.ForegroundRestrictionEnabled;
    if (_observationDuration is not null) _observationDuration.Value = config.ForegroundObservationSec;
    _lstPrograms.Items.Clear();
    foreach (var path in config.ForegroundProgramPaths)
      _lstPrograms.Items.Add(new SettingsProgramCandidate(System.IO.Path.GetFileNameWithoutExtension(path), path));
    RequestPathCheck();
    UpdateListHint();
    UpdateRemoveEnabled();
  }

  internal void RefreshGuardStatus(DateTimeOffset? nowUtc = null)
    => ApplyGuardStatus(StatusJsonReader.TryRead(_statusPath), nowUtc);

  internal void WireInstantApply()
  {
    if (_wired || _disposed)
      return;
    _wired = true;
    _tglRestriction.Checked += (_, _) => _queueSave();
    _tglRestriction.Unchecked += (_, _) => _queueSave();
    if (_observationDuration is not null) _observationDuration.ValueChanged += OnObservationDurationChanged;
    _btnAdd.Click += (_, _) => AddProgram();
    _btnRemove.Click += (_, _) => RemoveSelected();
    _lstPrograms.SelectionChanged += (_, _) => UpdateRemoveEnabled();
    if (_identifyProgram is not null) _identifyProgram.Click += OnIdentifyProgram;
    if (_verifyProgram is not null) _verifyProgram.Click += OnVerifyProgram;
    UpdateRemoveEnabled();
    _statusTimer.Start();
  }

  private async void OnStatusTick(object? sender, EventArgs e)
  {
    // 单个在途读取：慢文件不阻塞 Dispatcher，也不让每个 tick 堆积后台任务。
    if (_disposed)
      return;
    // 即使文件读取阻塞，展示快照也必须随时间过期；不需要另一个健康缓存或读取任务。
    ApplyGuardStatus(_displayedStatus, null);
    if (_refreshInFlight)
      return;
    _refreshInFlight = true;
    var checkPaths = _verificationHint is not null && _pathCheckRequested;
    var programs = checkPaths ? _lstPrograms.Items.OfType<SettingsProgramCandidate>().ToArray() : [];
    if (checkPaths) _pathCheckRequested = false;
    try
    {
      var result = await Task.Run(() => (Status: _readStatus(), Programs: CheckProgramAvailability(programs)));
      if (_disposed)
        return;
      ApplyGuardStatus(result.Status, null);
      foreach (var (program, available) in result.Programs)
      {
        if (!ContainsProgram(program)) continue;
        if (!available) program.VerificationText = "路径不存在或无法访问 · 请重新识别";
        else if (program.VerificationText == "路径不存在或无法访问 · 请重新识别") program.VerificationText = "尚未验证";
      }
    }
    catch
    {
      // 读取不可用仍明确显示未知；下一周期可恢复，不静默停止刷新。
      if (!_disposed)
      {
        if (checkPaths) _pathCheckRequested = true;
        ApplyGuardStatus(null, null);
      }
    }
    finally
    {
      _refreshInFlight = false;
    }
  }

  private void ApplyGuardStatus(StatusPayload? status, DateTimeOffset? nowUtc)
  {
    _displayedStatus = status;
    var text = ForegroundGuardStatusText.Describe(status, nowUtc ?? _utcNow());
    if (_txtGuardStatus.Text != text)
      _txtGuardStatus.Text = text;
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _statusTimer.Stop();
    _statusTimer.Tick -= OnStatusTick;
    _identificationCancellation.Cancel();
    _identificationCancellation.Dispose();
    if (_identifyProgram is not null) _identifyProgram.Click -= OnIdentifyProgram;
    if (_verifyProgram is not null) _verifyProgram.Click -= OnVerifyProgram;
    if (_observationDuration is not null) _observationDuration.ValueChanged -= OnObservationDurationChanged;
    // 文件读取不可中断；其晚到结果由 _disposed 丢弃，不更新已关闭窗口。
  }

  private void OnObservationDurationChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
  {
    if (!_disposed) _queueSave();
  }

  internal void AddProgram()
  {
    var picked = _pickProgramFile();
    if (string.IsNullOrWhiteSpace(picked))
      return;

    AddProgramPath(picked);
  }

  /// <summary>文件选择和名称搜索共用同一名单入口，显示名永远不作为配置身份。</summary>
  internal void AddProgramPath(string picked, string? displayName = null, System.Windows.Media.ImageSource? icon = null,
    ProgramCandidateSource source = ProgramCandidateSource.ExistingOrFile)
  {
    if (!GuardConfigValidator.TryNormalizeProgramPath(picked, out var normalized))
    {
      _showFeedback("无法添加：只能添加以 .exe 结尾的完整程序路径", true);
      return;
    }

    if (ProgramPaths.Any(existing =>
          string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
    {
      _showFeedback("该程序已在名单中", false);
      return;
    }

    // Reject before touching the list: a failed save must not leave a 129th, unsaved UI entry.
    if (_lstPrograms.Items.Count >= GuardConfigValidator.MaxForegroundProgramPaths)
    {
      _showFeedback($"程序名单最多 {GuardConfigValidator.MaxForegroundProgramPaths} 项，请先删除不需要的程序", true);
      return;
    }
    _lstPrograms.Items.Add(new SettingsProgramCandidate(displayName ?? System.IO.Path.GetFileNameWithoutExtension(normalized), normalized, icon, source));
    RequestPathCheck();
    UpdateListHint();
    _queueSave();
  }

  internal void RemoveSelected()
  {
    if (_lstPrograms.SelectedItem is not object selected)
      return;

    _lstPrograms.Items.Remove(selected);
    UpdateListHint();
    _queueSave();
  }

  private void UpdateRemoveEnabled()
  {
    _btnRemove.IsEnabled = _lstPrograms.SelectedItem is not null;
    if (_verifyProgram is not null) _verifyProgram.IsEnabled = !_identificationInFlight && _lstPrograms.SelectedItem is not null;
    if (_identifyProgram is not null) _identifyProgram.IsEnabled = !_identificationInFlight;
  }

  private void UpdateListHint()
    => _lblListHint.Visibility = _lstPrograms.Items.Count == 0
      ? Visibility.Visible
      : Visibility.Collapsed;

  private static string? PickProgramFileWithDialog()
  {
    var dialog = new Microsoft.Win32.OpenFileDialog
    {
      Filter = AddDialogFilter,
      CheckFileExists = true,
      Multiselect = false,
    };
    return dialog.ShowDialog() == true ? dialog.FileName : null;
  }
}
