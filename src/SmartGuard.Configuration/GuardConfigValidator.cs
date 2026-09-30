namespace SmartGuard.Configuration;

public static class GuardConfigValidator
{
  public const int MaxForegroundProgramPaths = 128;
  public const int MaxForegroundProgramPathLength = 32767;

  private const string ExeSuffix = ".exe";
  private static readonly char[] ForbiddenPathChars = ['*', '?'];

  public static IReadOnlyList<string> Validate(GuardConfig config)
  {
    var errors = new List<string>();
    if (config.BalancedThresholdSec < 60)
      errors.Add("平衡阈值至少 60 秒");
    if (config.PowerSaverThresholdSec <= config.BalancedThresholdSec)
      errors.Add("节能阈值必须大于平衡阈值");
    if (config.LowBatteryPercent is < 0 or > 100)
      errors.Add("低电量百分比须在 0~100");
    if (config.CheckIntervalSec < 5)
      errors.Add("轮询间隔至少 5 秒");
    if (config.BrightnessRestoreMs < 0)
      errors.Add("亮度恢复延迟不能为负");
    if (config.HeartbeatIntervalMin < 0)
      errors.Add("心跳间隔不能为负");
    ValidateForegroundProgramPaths(config.ForegroundProgramPaths, errors);
    return errors.Distinct().ToList();
  }

  /// <summary>
  /// 前台程序路径规则单源：规范化（<see cref="Path.GetFullPath"/>）+ OrdinalIgnoreCase 去重（保留首次出现）。
  /// 无法规范化的条目原样保留，由 <see cref="Validate"/> 拒绝；本助手不得静默丢弃条目。
  /// </summary>
  public static IReadOnlyList<string> NormalizeProgramPaths(IEnumerable<string?>? paths)
  {
    var result = new List<string>();
    if (paths is null)
      return result;

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var raw in paths)
    {
      var candidate = TryNormalizeProgramPath(raw, out var normalized)
        ? normalized
        : raw?.Trim() ?? string.Empty;
      if (seen.Add(candidate))
        result.Add(candidate);
    }

    return result;
  }

  public static bool TryNormalizeProgramPath(string? path, out string normalized)
  {
    normalized = string.Empty;
    if (string.IsNullOrWhiteSpace(path))
      return false;

    var candidate = path.Trim();
    if (candidate.Length > MaxForegroundProgramPathLength)
      return false;
    if (candidate.IndexOfAny(ForbiddenPathChars) >= 0)
      return false;
    if (candidate.StartsWith(@"\\.\", StringComparison.Ordinal)
      || candidate.StartsWith(@"\\?\", StringComparison.Ordinal))
      return false;
    // IsPathFullyQualified 而非 IsPathRooted：后者接受盘符相对（"C:app.exe"）与根相对（"\app.exe"），
    // GetFullPath 会按进程当前目录把它们解析成环境相关的绝对路径后静默保存；这两类是计划禁止的相对路径。
    if (!Path.IsPathFullyQualified(candidate))
      return false;
    if (!candidate.EndsWith(ExeSuffix, StringComparison.OrdinalIgnoreCase))
      return false;

    try
    {
      normalized = Path.GetFullPath(candidate);
    }
    catch (Exception)
    {
      return false;
    }

    return normalized.Length <= MaxForegroundProgramPathLength;
  }

  private static void ValidateForegroundProgramPaths(List<string>? paths, List<string> errors)
  {
    if (paths is null || paths.Count == 0)
      return;

    var distinctValid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var raw in paths)
    {
      if (string.IsNullOrWhiteSpace(raw))
        errors.Add("前台程序名单不能包含空路径");
      else if (raw.Trim().Length > MaxForegroundProgramPathLength)
        errors.Add($"前台程序路径过长（最多 {MaxForegroundProgramPathLength} 个字符）");
      else if (raw.IndexOfAny(ForbiddenPathChars) >= 0
        || raw.StartsWith(@"\\.\", StringComparison.Ordinal)
        || raw.StartsWith(@"\\?\", StringComparison.Ordinal))
        errors.Add("前台程序路径不能包含通配符或设备路径前缀");
      else if (TryNormalizeProgramPath(raw, out var normalized))
        distinctValid.Add(normalized);
      else
        errors.Add("前台程序路径必须为以 .exe 结尾的绝对 Windows 路径");
    }

    if (distinctValid.Count > MaxForegroundProgramPaths)
      errors.Add($"前台程序名单最多 {MaxForegroundProgramPaths} 项");
  }
}
