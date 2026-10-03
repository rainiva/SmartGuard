using System.Windows.Media;

namespace SmartGuard.Settings;

/// <summary>仅供设置展示的本机候选；身份始终是完整可执行路径，名称不参与守护匹配。</summary>
internal sealed record SettingsProgramCandidate(string Name, string ExecutablePath, ImageSource? Icon = null)
{
  // Existing configuration snapshots consume the concrete path, including legacy list readers.
  public override string ToString() => ExecutablePath;
}
