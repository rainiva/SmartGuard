using System.Runtime.InteropServices;

namespace SmartGuard.Engine.Infrastructure;

/// <summary>Win32 WinEvent out-of-context 回调签名（SetWinEventHook）。</summary>
internal delegate void WinEventDelegate(
  IntPtr hWinEventHook,
  uint eventType,
  IntPtr hwnd,
  int idObject,
  int idChild,
  uint dwEventThread,
  uint dwmsEventTime);

/// <summary>
/// 唯一前台事件监听 Owner：一个 EVENT_SYSTEM_FOREGROUND hook + 一个阻塞消息循环线程。
/// 回调只转发为注入的内存动作（合并待处理标记由 GuardianCycleCoordinator 唯一持有），
/// 不查询进程、不读配置、不写日志、不触发电源动作。注册与卸载都在监听线程上完成；
/// Stop 之后回调不再执行任何工作。注册失败或线程异常退出经 IsHealthy/LastFailure 可观测，
/// 故障路径不自动重启（重建只允许发生在限制关闭→开启的配置转换，由 GuardianLoop 决定）。
/// hook 注册/卸载与消息泵经构造函数委托注入，便于确定性测试；等待消息时阻塞，不忙等。
/// </summary>
internal sealed class ForegroundEventListener : IDisposable
{
  internal const uint EventSystemForeground = 0x0003;
  internal const uint WineventOutOfContext = 0x0000;

  private const uint InfiniteTimeoutMs = 0xFFFFFFFF;
  private const int WaitObject0 = 0;
  private const uint QsAllPosted = 0x0400;
  private const uint MwmoInputAvailable = 0x0004;
  private const uint PmRemove = 0x0001;
  private const int WmQuit = 0x0012;
  private const int WaitFailed = -1;
  private static readonly TimeSpan StopJoinTimeout = TimeSpan.FromSeconds(5);

  private readonly object _gate = new();
  private readonly Action _onForegroundSignal;
  private readonly Func<WinEventDelegate, IntPtr> _setHook;
  private readonly Action<IntPtr> _unhook;
  private readonly Func<WaitHandle, bool> _pumpUntilStop;
  private readonly WinEventDelegate _callback;
  private readonly ManualResetEvent _stopEvent = new(false);

  private Thread? _thread;
  private IntPtr _hookHandle;
  private int _stopped;
  private bool _disposed;
  private volatile bool _healthy;
  private volatile string? _lastFailure;

  public ForegroundEventListener(
    Action onForegroundSignal,
    Func<WinEventDelegate, IntPtr>? setHook = null,
    Action<IntPtr>? unhook = null,
    Func<WaitHandle, bool>? pumpUntilStop = null)
  {
    _onForegroundSignal = onForegroundSignal;
    _callback = OnWinEvent;
    _setHook = setHook ?? SetWinEventHookViaWin32;
    _unhook = unhook ?? UnhookWinEventViaWin32;
    _pumpUntilStop = pumpUntilStop ?? PumpUntilStopViaWin32;
  }

  /// <summary>hook 已注册且消息循环线程存活。</summary>
  public bool IsHealthy => _healthy;

  /// <summary>最近一次注册失败 / 消息循环异常退出 / 线程异常的说明；正常为 null。</summary>
  public string? LastFailure => _lastFailure;

  /// <summary>监听线程存活且未停止；线程自行退出（如注册失败）即为 false。</summary>
  public bool IsRunning
  {
    get
    {
      lock (_gate)
      {
        return Volatile.Read(ref _stopped) == 0 && _thread is { IsAlive: true };
      }
    }
  }

  /// <summary>启动监听线程（幂等；线程已退出后可经 Stop→Start 生命周期重建）。</summary>
  public void Start()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    lock (_gate)
    {
      if (_thread is { IsAlive: true })
        return;

      Volatile.Write(ref _stopped, 0);
      _healthy = false;
      _lastFailure = null;
      Volatile.Write(ref _hookHandle, IntPtr.Zero);
      _stopEvent.Reset();
      _thread = new Thread(ThreadMain)
      {
        Name = "SmartGuard.ForegroundListener",
        IsBackground = true,
      };
      _thread.Start();
    }
  }

  /// <summary>
  /// 停止监听：先置停止标记（此后回调直接返回，不再做任何工作），再解除消息循环阻塞，
  /// 并等待监听线程在注册线程上完成 Unhook 后退出。不在其他线程跨线程卸载 hook。
  /// </summary>
  public void Stop()
  {
    lock (_gate)
    {
      Volatile.Write(ref _stopped, 1);
      _stopEvent.Set();
      if (_thread is { IsAlive: true } && !_thread.Join(StopJoinTimeout))
        _lastFailure = "前台监听线程未在期限内退出";
      _healthy = false;
    }
  }

  public void Dispose()
  {
    lock (_gate)
    {
      if (_disposed)
        return;
      _disposed = true;
    }

    Stop();
    _stopEvent.Dispose();
  }

  private void ThreadMain()
  {
    try
    {
      var hook = _setHook(_callback);
      if (hook == IntPtr.Zero)
      {
        _lastFailure = "SetWinEventHook(EVENT_SYSTEM_FOREGROUND) 注册失败，已降级为周期采样";
        return;
      }

      Volatile.Write(ref _hookHandle, hook);
      _healthy = true;
      if (!_pumpUntilStop(_stopEvent) && _lastFailure is null)
        _lastFailure = "前台监听消息循环异常退出，已降级为周期采样";
    }
    catch (Exception ex)
    {
      _lastFailure = "前台监听线程异常退出，已降级为周期采样：" + ex.Message;
    }
    finally
    {
      _healthy = false;
      var hook = Volatile.Read(ref _hookHandle);
      if (hook != IntPtr.Zero)
      {
        try
        {
          _unhook(hook);
        }
        catch
        {
          // 卸载失败不向外抛；句柄随线程退出交还系统，健康面已可见。
        }
        Volatile.Write(ref _hookHandle, IntPtr.Zero);
      }
    }
  }

  private void OnWinEvent(
    IntPtr hWinEventHook,
    uint eventType,
    IntPtr hwnd,
    int idObject,
    int idChild,
    uint dwEventThread,
    uint dwmsEventTime)
  {
    if (Volatile.Read(ref _stopped) != 0)
      return;

    _onForegroundSignal();
  }

  /// <summary>
  /// 默认阻塞消息泵：等待“停止事件或已投递消息”，消息到达时经 PeekMessage 泵出
  /// （out-of-context hook 回调借此在注册线程上执行），停止事件或 WM_QUIT 时受控返回。
  /// </summary>
  private static bool PumpUntilStopViaWin32(WaitHandle stopHandle)
  {
    var handles = new[] { stopHandle.SafeWaitHandle.DangerousGetHandle() };
    while (true)
    {
      var wait = MsgWaitForMultipleObjectsEx(1, handles, InfiniteTimeoutMs, QsAllPosted, MwmoInputAvailable);
      if (wait == WaitObject0)
        return true;
      if (wait == WaitFailed)
        return false;

      while (PeekMessage(out var message, IntPtr.Zero, 0, 0, PmRemove))
      {
        if (message.Message == WmQuit)
          return true;
        _ = TranslateMessage(ref message);
        _ = DispatchMessage(ref message);
      }

      if (stopHandle.WaitOne(0))
        return true;
    }
  }

  private static IntPtr SetWinEventHookViaWin32(WinEventDelegate callback)
    => SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, callback, 0, 0, WineventOutOfContext);

  private static void UnhookWinEventViaWin32(IntPtr hook)
    => _ = UnhookWinEvent(hook);

  [DllImport("user32.dll", SetLastError = false)]
  private static extern IntPtr SetWinEventHook(
    uint eventMin,
    uint eventMax,
    IntPtr hmodWinEventProc,
    WinEventDelegate pfnWinEventProc,
    uint idProcess,
    uint idThread,
    uint dwFlags);

  [DllImport("user32.dll")]
  private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

  [DllImport("user32.dll", SetLastError = false)]
  private static extern int MsgWaitForMultipleObjectsEx(
    int nCount,
    IntPtr[] pHandles,
    uint dwMilliseconds,
    uint dwWakeMask,
    uint dwFlags);

  [DllImport("user32.dll")]
  private static extern bool PeekMessage(out NativeMessage lpMessage, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

  [DllImport("user32.dll")]
  private static extern bool TranslateMessage(ref NativeMessage lpMessage);

  [DllImport("user32.dll")]
  private static extern IntPtr DispatchMessage(ref NativeMessage lpMessage);

  [StructLayout(LayoutKind.Sequential)]
  private struct NativeMessage
  {
    public IntPtr Handle;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public IntPtr Point;
  }
}
