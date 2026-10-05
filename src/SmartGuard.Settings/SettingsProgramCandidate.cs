using System.Windows.Media;
using System.ComponentModel;

namespace SmartGuard.Settings;

/// <summary>仅供设置展示的本机候选；身份始终是完整可执行路径，名称不参与守护匹配。</summary>
internal enum ProgramCandidateSource { ExistingOrFile, RunningWindow, Shortcut, RelatedComponent }

/// <summary>
/// UI条目使用对象身份：WPF Selector 不得把重载后相同路径的新条目当作旧选择，
/// 具体路径的去重仍由既有名单 Owner 完成；验证状态只属于这一条目的当前会话。
/// </summary>
internal sealed class SettingsProgramCandidate : INotifyPropertyChanged
{
  internal SettingsProgramCandidate(string name, string executablePath, ImageSource? icon = null,
    ProgramCandidateSource source = ProgramCandidateSource.ExistingOrFile)
  {
    Name = name;
    ExecutablePath = executablePath;
    Icon = icon;
    Source = source;
  }

  public string Name { get; }
  public string ExecutablePath { get; }
  public ImageSource? Icon { get; }
  public ProgramCandidateSource Source { get; }

  public string SourceText => Source switch
  {
    ProgramCandidateSource.RunningWindow => "运行中的窗口程序",
    ProgramCandidateSource.Shortcut => "快捷方式入口 · 需打开窗口验证",
    ProgramCandidateSource.RelatedComponent => "同软件组件 · 需打开窗口验证",
    _ => "浏览文件或已有名单 · 尚未验证",
  };

  private string _verificationText = "尚未验证";
  public string VerificationText
  {
    get => _verificationText;
    internal set
    {
      if (_verificationText == value) return;
      _verificationText = value;
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VerificationText)));
    }
  }
  public event PropertyChangedEventHandler? PropertyChanged;

  // Existing configuration snapshots consume the concrete path, including legacy list readers.
  public override string ToString() => ExecutablePath;
}
