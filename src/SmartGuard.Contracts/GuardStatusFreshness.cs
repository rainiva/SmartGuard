namespace SmartGuard.Contracts;

/// <summary>
/// 状态新鲜度判定（Tray / Settings 展示链共享真源）：status.json 的 <c>timestamp</c>
/// 为 UTC（Engine 以 "O" 写入）。距今超过保守上界（Engine 无变化时最长 60 秒刷新一次，
/// 连续 3 个周期未见刷新视为过期）或来自未来超过上界（时钟回拨保护），以及解析失败，
/// 均视为不可信——UI 不得据此显示确定的正在守护。
/// </summary>
public static class GuardStatusFreshness
{
  /// <summary>状态新鲜度保守上界：Engine 无变化时最长 60 秒刷新一次，连续 3 个周期未见刷新视为过期。</summary>
  public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(3);

  public static bool IsFresh(string? timestamp, DateTimeOffset nowUtc)
  {
    if (string.IsNullOrWhiteSpace(timestamp) || !DateTimeOffset.TryParse(timestamp, out var written))
      return false;

    var age = nowUtc - written;
    return age <= MaxAge && age >= -MaxAge;
  }
}
