using System.Runtime.InteropServices;
using System.Text;
using SmartGuard.Engine.Domain;

namespace SmartGuard.Engine.Infrastructure;

internal delegate bool TryGetWindowProcessId(IntPtr hWnd, out uint processId);

internal delegate bool TryOpenProcessLimited(uint processId, out IntPtr processHandle);

internal delegate bool TryQueryProcessImageName(IntPtr processHandle, StringBuilder nameBuffer, ref uint characterCount);

/// <summary>
/// 前台事实采集唯一真源：一次查询内完成
/// GetForegroundWindow → GetWindowThreadProcessId → OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)
/// → QueryFullProcessImageNameW → 复核前台 HWND/PID 仍一致。
/// 无 hook、监听线程、Timer、事件队列或进程身份缓存；每次查询在 finally 中释放进程句柄；
/// 不读取窗口标题。任何失败（空 HWND / 进程退出 / 查询失败 / 查询期间前台变化）收敛为
/// 识别失败的 <see cref="ForegroundObservation"/>，限制开启时按“识别不可用”处理，不旁路回退。
/// Win32 调用点经构造函数委托注入，便于测试注入摇晃的 Win32 时序。
/// </summary>
internal sealed class ForegroundProgramReader
{
  /// <summary>镜像路径查询缓冲区上限（字符数，含终止符），与计划 §3 资源预算一致。</summary>
  internal const int ImageNameBufferCapacityChars = 32768;

  private const uint ProcessQueryLimitedInformation = 0x1000;

  private readonly Func<IntPtr> _getForegroundWindow;
  private readonly TryGetWindowProcessId _getWindowProcessId;
  private readonly TryOpenProcessLimited _openProcessLimited;
  private readonly TryQueryProcessImageName _queryProcessImageName;
  private readonly Action<IntPtr> _closeProcessHandle;

  public ForegroundProgramReader()
    : this(
      GetForegroundWindowViaWin32,
      GetWindowProcessIdViaWin32,
      OpenProcessLimitedViaWin32,
      QueryProcessImageNameViaWin32,
      CloseProcessHandleViaWin32)
  {
  }

  internal ForegroundProgramReader(
    Func<IntPtr> getForegroundWindow,
    TryGetWindowProcessId getWindowProcessId,
    TryOpenProcessLimited openProcessLimited,
    TryQueryProcessImageName queryProcessImageName,
    Action<IntPtr> closeProcessHandle)
  {
    _getForegroundWindow = getForegroundWindow;
    _getWindowProcessId = getWindowProcessId;
    _openProcessLimited = openProcessLimited;
    _queryProcessImageName = queryProcessImageName;
    _closeProcessHandle = closeProcessHandle;
  }

  public ForegroundObservation Read()
  {
    var foregroundHwnd = _getForegroundWindow();
    if (foregroundHwnd == IntPtr.Zero)
      return ForegroundObservation.Unidentified("前台窗口为空");

    if (!_getWindowProcessId(foregroundHwnd, out var foregroundPid) || foregroundPid == 0)
      return ForegroundObservation.Unidentified("前台窗口进程标识不可用");

    if (!_openProcessLimited(foregroundPid, out var processHandle))
      return ForegroundObservation.Unidentified("打开前台进程失败（可能已退出或拒绝访问）");

    try
    {
      var nameBuffer = new StringBuilder(ImageNameBufferCapacityChars);
      var characterCount = (uint)ImageNameBufferCapacityChars;
      if (!_queryProcessImageName(processHandle, nameBuffer, ref characterCount) || characterCount == 0)
        return ForegroundObservation.Unidentified("查询前台进程镜像路径失败");

      var executablePath = nameBuffer.ToString(0, (int)characterCount);
      if (string.IsNullOrWhiteSpace(executablePath))
        return ForegroundObservation.Unidentified("前台进程镜像路径为空");

      // 查询后复核前台身份仍一致：不一致说明采集期间前台已切换，本次事实作废。
      var recheckHwnd = _getForegroundWindow();
      if (recheckHwnd != foregroundHwnd)
        return ForegroundObservation.Unidentified("查询期间前台窗口已切换");

      if (!_getWindowProcessId(recheckHwnd, out var recheckPid) || recheckPid != foregroundPid)
        return ForegroundObservation.Unidentified("查询期间前台进程已变化");

      return ForegroundObservation.Identified(executablePath);
    }
    finally
    {
      _closeProcessHandle(processHandle);
    }
  }

  private static IntPtr GetForegroundWindowViaWin32()
    => GetForegroundWindow();

  private static bool GetWindowProcessIdViaWin32(IntPtr hWnd, out uint processId)
  {
    _ = GetWindowThreadProcessId(hWnd, out processId);
    return processId != 0;
  }

  private static bool OpenProcessLimitedViaWin32(uint processId, out IntPtr processHandle)
  {
    processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
    return processHandle != IntPtr.Zero;
  }

  private static bool QueryProcessImageNameViaWin32(
    IntPtr processHandle,
    StringBuilder nameBuffer,
    ref uint characterCount)
    => QueryFullProcessImageNameW(processHandle, dwFlags: 0, nameBuffer, ref characterCount);

  private static void CloseProcessHandleViaWin32(IntPtr processHandle)
    => _ = CloseHandle(processHandle);

  [DllImport("user32.dll")]
  private static extern IntPtr GetForegroundWindow();

  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern bool QueryFullProcessImageNameW(
    IntPtr hProcess,
    uint dwFlags,
    StringBuilder lpExeName,
    ref uint lpdwSize);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool CloseHandle(IntPtr hObject);
}
