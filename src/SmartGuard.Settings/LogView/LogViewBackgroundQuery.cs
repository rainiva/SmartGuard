namespace SmartGuard.Settings.LogView;

/// <summary>
/// 后台日志查询：在 Task 线程执行 LogViewController.GetFilteredLinesAsync，
/// 支持 CancellationToken 取消（D4 裁决）。
/// </summary>
internal sealed class LogViewBackgroundQuery
{
  public Task<IReadOnlyList<string>> QueryAsync(
    LogViewController controller,
    CancellationToken cancellationToken)
    => controller.GetFilteredLinesAsync(cancellationToken);
}
