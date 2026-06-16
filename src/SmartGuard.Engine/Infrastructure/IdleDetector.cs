using System.Runtime.InteropServices;

namespace SmartGuard.Engine.Infrastructure;

public static class IdleDetector
{
  [StructLayout(LayoutKind.Sequential)]
  private struct LastInputInfo
  {
    public uint CbSize;
    public uint DwTime;
  }

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool GetLastInputInfo(ref LastInputInfo plii);

  public static uint GetIdleSeconds()
  {
    var lii = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
    if (!GetLastInputInfo(ref lii))
      throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    return ((uint)Environment.TickCount - lii.DwTime) / 1000;
  }
}
