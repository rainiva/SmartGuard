using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

/// <summary>
/// GuardianLoop 的前台采集与受控订阅部分：
/// - <see cref="ForegroundProgramReader"/>：前台事实采集唯一真源，周期完整轮次与事件轻路径共用；
/// - <see cref="ForegroundEventListener"/>：唯一前台事件监听 Owner（一个 hook + 一个消息循环线程），
///   归循环启动/停止/等待退出；限制开启时确保运行、限制关闭时卸载；注册失败或线程退出可观测
///   （一次性降级日志），故障路径不自动重启，降级为同一 Reader 的周期采样；
/// - 电源事件唤醒标记：电源事件强制完整轮次（与周期到期同一执行路径）。
/// 本部分不拥有规则与持久化；准入规则真源为 PolicyEngine，执行真源为 GuardianIterationRunner。
/// </summary>
public sealed partial class GuardianLoop
{
  internal static Func<ForegroundProgramReader>? ForegroundReaderFactoryOverrideForTests;
  internal static Func<Action, ForegroundEventListener>? ForegroundListenerFactoryOverrideForTests;

  private ForegroundProgramReader? _foregroundReader;
  private ForegroundEventListener? _foregroundListener;
  private volatile bool _foregroundCollectionDesired;
  private bool _foregroundDegradationReported;
  private int _powerWakePending;

  internal bool ForegroundListenerRunning => _foregroundListener?.IsRunning ?? false;
  internal bool ForegroundListenerHealthy => _foregroundListener?.IsHealthy ?? false;
  internal string? ForegroundListenerFailure => _foregroundListener?.LastFailure;
  internal bool ForegroundDegradationReported => _foregroundDegradationReported;

  /// <summary>周期完整轮次的前台事实：仅在限制开启时真实采集（限制关闭=跳过采集），失败收敛为 Unknown。</summary>
  internal ForegroundObservation ReadForegroundForRunner()
  {
    if (!_foregroundCollectionDesired || _foregroundReader is null)
      return ForegroundObservation.Unknown;

    try
    {
      return _foregroundReader.Read();
    }
    catch
    {
      // 识别不可用 → 限制开启时按不干预处理，不旁路回退。
      return ForegroundObservation.Unknown;
    }
  }

  /// <summary>
  /// 按最新有效配置同步前台订阅：限制开 → 确保 hook + 消息线程运行；限制关 → 卸载并跳过采集；
  /// 配置不可用 → 保持订阅现状（不启停、不猜测授权；轻路径与完整轮次对无效配置本就不做业务动作，
  /// 此处避免每周期启停抖动）。订阅管理失败不得让守护循环退出，等待下一周期重试。
  /// </summary>
  internal void SyncForegroundSubscription()
  {
    try
    {
      var configRead = GuardConfigReadResult.TryFromRepository(_configRepository);
      if (!configRead.IsValid || configRead.Config is null)
        return;
      var restrictionEnabled = configRead.Config.ForegroundRestrictionEnabled;
      _foregroundCollectionDesired = restrictionEnabled;
      if (restrictionEnabled)
        EnsureForegroundListenerStarted();
      else
        StopForegroundListener();
    }
    catch
    {
      // 订阅同步是尽力而为的管理动作；下一周期重试。
    }
  }

  private void EnsureForegroundListenerStarted()
  {
    if (_foregroundListener is { } existing)
    {
      if (existing.IsRunning)
        return;

      // 线程已退出（注册失败或消息循环异常）：可观测 + 降级为周期采样；不在高频路径重启监听，
      // 重建只发生在限制关闭→开启转换（届时 _foregroundListener 已被 Stop 置空）。
      ReportForegroundDegradation(existing.LastFailure);
      return;
    }

    var listener = CreateForegroundListener();
    try
    {
      listener.Start();
    }
    catch (Exception ex)
    {
      // Start 抛异常与注册失败同样进入降级记忆：登记该（非运行态）实例，
      // 后续周期不再重复创建/重试；重建仅发生在限制关闭→开启转换（届时实例被 Stop 置空）。
      _foregroundListener = listener;
      ReportForegroundDegradation(ex.Message);
      return;
    }

    _foregroundListener = listener;
    _foregroundDegradationReported = false;
  }

  private void StopForegroundListener()
  {
    if (_foregroundListener is null)
      return;

    try
    {
      _foregroundListener.Stop();
      _foregroundListener.Dispose();
    }
    catch
    {
      // 停止失败不向外抛；对象置空后由 GC 回收，不再接收回调。
    }

    _foregroundListener = null;
    _foregroundDegradationReported = false;
  }

  private void ReportForegroundDegradation(string? failure)
  {
    if (_foregroundDegradationReported)
      return;

    _foregroundDegradationReported = true;
    WriteLog(
      LoadConfig(),
      LogLevel.Warn,
      $"前台事件监听不可用（{failure ?? "原因未知"}），已降级为周期采样",
      EventNameRegistry.ForegroundListenerDegraded);
  }

  private ForegroundEventListener CreateForegroundListener()
    => ForegroundListenerFactoryOverrideForTests?.Invoke(OnForegroundEvent)
      ?? new ForegroundEventListener(OnForegroundEvent);

  /// <summary>
  /// 执行一个守护周期并按既有轮次边界收敛异常：协调器周期（完整轮次 / 轻路径）失败时，
  /// 记入异常风暴处理与回退日志，并让协调器按完整间隔冷却（前台节流请求丢弃，
  /// 下一真实事件重新标记），周期复核为兜底，守护循环永不因前台机制静默退出。
  /// </summary>
  private async Task RunGuardedCycleAsync(
    PowerLifecycleDirective directive,
    int intervalSeconds,
    CancellationToken cancellationToken)
  {
    try
    {
      await _cycleCoordinator.RunCycleAsync(directive, intervalSeconds, cancellationToken);
    }
    catch (Exception ex)
    {
      _cycleCoordinator.NoteCycleFailure(intervalSeconds);
      try
      {
        var cfg = LoadConfig();
        _exceptionStormHandler.TrackAndLogException(ex, cfg);
      }
      catch
      {
        SmartGuardLogWriter.TryAppendLine(_fallbackLogPath, $"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex.Message}");
      }
    }
  }

  /// <summary>监听线程回调（零 IO）：转交协调器做 0→1 合并标记并唤醒 Engine。</summary>
  private void OnForegroundEvent() => _cycleCoordinator.MarkForegroundSignal();

  private bool ConsumePowerWake() => Interlocked.Exchange(ref _powerWakePending, 0) != 0;
}
