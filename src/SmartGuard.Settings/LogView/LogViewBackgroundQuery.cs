namespace SmartGuard.Settings.LogView;

/// <summary>
/// 后台日志查询：在 Task 线程执行 LogViewController.GetFilteredLines，
/// 支持 CancellationToken 取消（D4 裁决）。
/// </summary>
internal sealed class LogViewBackgroundQuery
{
  public async Task<IReadOnlyList<string>> QueryAsync(
    LogViewController controller,
    CancellationToken cancellationToken)
  {
    if (cancellationToken.IsCancellationRequested)
      return Array.Empty<string>();

    return await Task.Run(() =>
    {
      cancellationToken.ThrowIfCancellationRequested();
      return controller.GetFilteredLines();
    }, cancellationToken);
  }
}
