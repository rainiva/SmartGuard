namespace SmartGuard.Engine.Domain;

/// <summary>
/// 前台窗口事实值（纯 Domain）：识别是否成功、可空完整可执行路径、失败原因。
/// 不依赖 Win32/WPF，不携带窗口标题；真实采集由 Infrastructure 层负责（后续任务接入）。
/// </summary>
public sealed class ForegroundObservation
{
  /// <summary>未采集或识别失败的兜底事实：限制开启时按“识别不可用”处理，不旁路回退。</summary>
  public static ForegroundObservation Unknown { get; } = new(success: false, executablePath: null, failureReason: null);

  private ForegroundObservation(bool success, string? executablePath, string? failureReason)
  {
    IdentificationSucceeded = success;
    ExecutablePath = executablePath;
    FailureReason = failureReason;
  }

  public bool IdentificationSucceeded { get; }

  /// <summary>识别成功时的原始完整可执行路径；未识别为 null。</summary>
  public string? ExecutablePath { get; }

  /// <summary>识别失败原因（采集层填写）；可为 null 表示尚未尝试采集。</summary>
  public string? FailureReason { get; }

  public static ForegroundObservation Identified(string executablePath)
  {
    if (string.IsNullOrWhiteSpace(executablePath))
      throw new ArgumentException("前台可执行路径不能为空", nameof(executablePath));

    return new ForegroundObservation(success: true, executablePath, failureReason: null);
  }

  public static ForegroundObservation Unidentified(string failureReason)
  {
    if (string.IsNullOrWhiteSpace(failureReason))
      throw new ArgumentException("前台识别失败原因不能为空", nameof(failureReason));

    return new ForegroundObservation(success: false, executablePath: null, failureReason);
  }
}
