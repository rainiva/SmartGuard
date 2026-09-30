using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Settings;

/// <summary>
/// 前台程序限制设置 Owner（轻量：一个开关、名单、添加和删除）。
/// UI 只编辑配置、展示 Engine 发布的守护状态：名单条目经
/// <see cref="GuardConfigValidator"/> 共享规则规范化与 OrdinalIgnoreCase 去重，
/// 保存走既有 ReadConfigFromUi → SettingsSnapshotMapper → 防抖保存链
/// （超限等校验失败由保存链的既有错误提示呈现），不直接写配置仓库；
/// 状态区只读 status.json 共享读取链，不维护第二套路径规则、状态解析或健康存储。
/// </summary>
internal sealed class SettingsForegroundCoordinator
{
  private const string AddDialogFilter = "可执行程序 (*.exe)|*.exe";

  private readonly CheckBox _tglRestriction;
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
    Func<string?>? pickProgramFile = null)
  {
    _tglRestriction = tglRestriction;
    _lstPrograms = lstPrograms;
    _btnAdd = btnAdd;
    _btnRemove = btnRemove;
    _txtGuardStatus = txtGuardStatus;
    _lblListHint = lblListHint;
    _showFeedback = showFeedback;
    _queueSave = queueSave;
    _statusPath = SmartGuardPaths.StatusFile(root);
    _pickProgramFile = pickProgramFile ?? PickProgramFileWithDialog;
  }

  internal bool RestrictionEnabled => _tglRestriction.IsChecked == true;

  internal IReadOnlyList<string> ProgramPaths
    => _lstPrograms.Items.OfType<object>().Select(item => item?.ToString() ?? string.Empty).ToList();

  /// <summary>开关与名单来自配置；状态区只读 status.json（与暂停初始展示同一读取链）。</summary>
  internal void ApplyConfigToUi(GuardConfig config)
  {
    _tglRestriction.IsChecked = config.ForegroundRestrictionEnabled;
    _lstPrograms.Items.Clear();
    foreach (var path in config.ForegroundProgramPaths)
      _lstPrograms.Items.Add(path);
    UpdateListHint();
    UpdateRemoveEnabled();
  }

  internal void RefreshGuardStatus(DateTimeOffset? nowUtc = null)
    => _txtGuardStatus.Text = ForegroundGuardStatusText.Describe(
      StatusJsonReader.TryRead(_statusPath),
      nowUtc);

  internal void WireInstantApply()
  {
    if (_wired)
      return;
    _wired = true;
    _tglRestriction.Checked += (_, _) => _queueSave();
    _tglRestriction.Unchecked += (_, _) => _queueSave();
    _btnAdd.Click += (_, _) => AddProgram();
    _btnRemove.Click += (_, _) => RemoveSelected();
    _lstPrograms.SelectionChanged += (_, _) => UpdateRemoveEnabled();
    UpdateRemoveEnabled();
  }

  internal void AddProgram()
  {
    var picked = _pickProgramFile();
    if (string.IsNullOrWhiteSpace(picked))
      return;

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

    _lstPrograms.Items.Add(normalized);
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

  private void UpdateRemoveEnabled() => _btnRemove.IsEnabled = _lstPrograms.SelectedItem is not null;

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
