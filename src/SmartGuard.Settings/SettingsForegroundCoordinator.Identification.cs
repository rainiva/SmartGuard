using System.IO;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

/// <summary>单次窗口识别交互归既有名单 Owner；不持久化验证、不增加常驻前台监控。</summary>
internal sealed partial class SettingsForegroundCoordinator
{
  private readonly Button? _identifyProgram;
  private readonly Button? _verifyProgram;
  private readonly TextBlock? _verificationHint;
  private readonly Window? _window;
  private readonly Func<SettingsProgramCandidate?> _readWindow;
  private readonly Func<SettingsProgramCandidate, string?, bool> _confirmWindow;
  private readonly Func<string, bool> _programExists;
  private readonly Func<CancellationToken, Task> _waitForWindow;
  private readonly CancellationTokenSource _identificationCancellation = new();
  private bool _identificationInFlight;
  private string? _mismatchedSelection;

  internal void RequestPathCheck()
  {
    if (!_disposed) _pathCheckRequested = true;
  }

  private async void OnIdentifyProgram(object sender, RoutedEventArgs e) => await IdentifyWindowAsync();
  private async void OnVerifyProgram(object sender, RoutedEventArgs e) => await IdentifyWindowAsync(verifySelected: true);

  internal async Task IdentifyWindowAsync(bool verifySelected = false)
  {
    if (_disposed || _identificationInFlight) return;
    var selected = verifySelected ? _lstPrograms.SelectedItem as SettingsProgramCandidate : null;
    if (verifySelected && selected is null) return;
    _identificationInFlight = true;
    UpdateRemoveEnabled();
    var token = _identificationCancellation.Token;
    var previousState = _window?.WindowState;
    var minimizedByUs = _window?.IsVisible == true && previousState != WindowState.Minimized;
    try
    {
      SetVerificationHint("请在5秒内切换到要识别的实际窗口；识别后返回设置确认");
      if (minimizedByUs) _window!.WindowState = WindowState.Minimized;
      await _waitForWindow(token);
      token.ThrowIfCancellationRequested();
      // Only one query and file check are in flight; blocking platform calls never run on Dispatcher.
      var captured = await Task.Run(() =>
      {
        var candidate = _readWindow();
        var available = candidate is not null && IsLocalProgramAvailable(candidate.ExecutablePath);
        var oldAvailable = selected is null || IsLocalProgramAvailable(selected.ExecutablePath);
        return (Candidate: available ? candidate : null, OldAvailable: oldAvailable);
      }, token);
      if (_disposed || token.IsCancellationRequested) return;
      RestoreWindow();
      var actual = captured.Candidate;
      if (actual is null || !GuardConfigValidator.TryNormalizeProgramPath(actual.ExecutablePath, out var normalized))
      {
        SetVerificationHint("暂时无法识别该窗口，请打开实际文档窗口后重试；也可能没有读取权限", true);
        return;
      }
      actual = new SettingsProgramCandidate(actual.Name, normalized, actual.Icon, ProgramCandidateSource.RunningWindow);
      if (selected is not null && !ContainsProgram(selected))
      {
        SetVerificationHint("所选条目已更改，请重新选择后验证", true);
        return;
      }
      if (selected is not null && string.Equals(selected.ExecutablePath, normalized, StringComparison.OrdinalIgnoreCase))
      {
        selected.VerificationText = "已确认前台匹配（本次设置）";
        SetVerificationHint($"已确认 {Path.GetFileName(normalized)} 与所选条目匹配；守护是否启用请看守护状态");
        return;
      }
      _mismatchedSelection = selected?.ExecutablePath;
      if (selected is not null)
      {
        selected.VerificationText = captured.OldAvailable ? "本次窗口未匹配 · 可添加实际组件" : "原路径不可用 · 可重新识别修正";
        SetVerificationHint($"当前窗口是 {Path.GetFileName(normalized)}，所选条目是 {Path.GetFileName(selected.ExecutablePath)}");
      }
      var replacePath = selected is not null && !captured.OldAvailable ? selected.ExecutablePath : null;
      if (!_confirmWindow(actual, replacePath) || _disposed) return;
      // Modal confirmation pumps Dispatcher: recheck identity before replacing a potentially changed list.
      if (replacePath is not null && (selected is null || !ContainsProgram(selected))) return;
      if (replacePath is not null)
      {
        var existing = FindProgram(normalized);
        if (existing is null)
        {
          var index = _lstPrograms.Items.IndexOf(selected);
          _lstPrograms.Items[index] = actual;
        }
        else _lstPrograms.Items.Remove(selected);
        _queueSave();
      }
      else AddProgramPath(normalized, actual.Name, actual.Icon, actual.Source);
      var added = FindProgram(normalized);
      if (added is not null)
      {
        added.VerificationText = "已确认前台匹配（本次设置）";
        SetVerificationHint($"已确认 {Path.GetFileName(normalized)} 的窗口程序；其他组件请分别打开并添加");
      }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    catch (Exception)
    {
      if (!_disposed) SetVerificationHint("暂时无法识别该窗口，请重试或浏览选择实际 exe 文件", true);
    }
    finally
    {
      if (!_disposed)
      {
        RestoreWindow();
        _identificationInFlight = false;
        UpdateRemoveEnabled();
      }
    }

    void RestoreWindow()
    {
      if (!minimizedByUs || _disposed) return;
      minimizedByUs = false;
      if (_window!.WindowState == WindowState.Minimized) _window.WindowState = previousState!.Value;
      _window.Activate();
    }
  }

  private SettingsProgramCandidate? FindProgram(string path) => _lstPrograms.Items.OfType<SettingsProgramCandidate>()
    .FirstOrDefault(item => string.Equals(item.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));

  private bool ContainsProgram(SettingsProgramCandidate program)
    => _lstPrograms.Items.OfType<SettingsProgramCandidate>().Any(item => ReferenceEquals(item, program));

  private IReadOnlyList<(SettingsProgramCandidate Program, bool Available)> CheckProgramAvailability(SettingsProgramCandidate[] programs)
  {
    var result = new List<(SettingsProgramCandidate, bool)>();
    var budget = System.Diagnostics.Stopwatch.StartNew();
    foreach (var program in programs.Take(GuardConfigValidator.MaxForegroundProgramPaths))
    {
      // Cooperative budget bounds the batch; a single OS file lookup cannot be forcibly interrupted.
      if (budget.Elapsed >= TimeSpan.FromSeconds(2)) break;
      result.Add((program, IsLocalProgramAvailable(program.ExecutablePath)));
    }
    return result;
  }

  private bool IsLocalProgramAvailable(string path)
    => GuardConfigValidator.TryNormalizeProgramPath(path, out var normalized)
      && !normalized.StartsWith(@"\\", StringComparison.Ordinal) && _programExists(normalized);

  private void SetVerificationHint(string message, bool isError = false)
  {
    if (_verificationHint is not null) LogStatusLiveRegion.Set(_verificationHint, message);
    if (isError) _showFeedback(message, true);
  }

  private bool ConfirmWindow(SettingsProgramCandidate actual, string? replacePath)
  {
    var message = $"程序：{actual.Name}\nEXE：{Path.GetFileName(actual.ExecutablePath)}\n位置：{actual.ExecutablePath}\n\n";
    if (replacePath is not null)
      message += $"原路径已不存在或无法访问：\n{replacePath}\n\n确认用当前窗口程序替换这一项？其他组件保持原样。";
    else
    {
      if (_mismatchedSelection is not null) message += $"所选条目：{_mismatchedSelection}\n两者不一致，将保留原条目。\n\n";
      message += "确认将这个具体程序添加到名单？软件的其他组件需要分别添加。";
    }
    return AppDialog.ShowConfirm(_window, replacePath is null ? "添加实际窗口程序" : "修正程序路径", message, AppDialogSeverity.Information);
  }
}
