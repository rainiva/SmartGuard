namespace SmartGuard.Configuration;

/// <summary>
/// 重复日志聚合：对入队行按指纹聚合，连续相同指纹超过阈值后
/// 折叠为 [DEDUP x{count}] 前缀行，释放后重置计数。
/// 替代 GuardianLoop 内 _tickLogFingerprints 的简单 HashSet 去重。
/// </summary>
public sealed class LogDeduplicationFilter
{
  private readonly int _threshold;
  private string? _lastFingerprint;
  private string? _lastLine;
  private int _repeatCount;

  public LogDeduplicationFilter(int threshold = 5)
  {
    _threshold = Math.Max(2, threshold);
  }

  public bool TryAccept(string fingerprint, string line, out string? outputLine)
  {
    if (fingerprint != _lastFingerprint)
    {
      // 不同指纹：如有积压则先释放（但此处简化为直接重置）
      _lastFingerprint = fingerprint;
      _lastLine = line;
      _repeatCount = 1;
      outputLine = line;
      return true;
    }

    _repeatCount++;
    if (_repeatCount <= _threshold)
    {
      outputLine = null;
      return false; // 抑制重复
    }

    // 达到阈值：输出折叠摘要并重置
    outputLine = $"[DEDUP x{_repeatCount - 1}] {_lastLine}";
    _repeatCount = 0;
    _lastFingerprint = null;
    _lastLine = null;
    return true;
  }

  public void Reset()
  {
    _lastFingerprint = null;
    _lastLine = null;
    _repeatCount = 0;
  }
}
