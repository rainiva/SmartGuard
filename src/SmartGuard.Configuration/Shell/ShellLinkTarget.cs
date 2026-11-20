using System.Runtime.InteropServices;
using System.Text;

namespace SmartGuard.Configuration.Shell;

/// <summary>
/// Reads a shortcut's target through the Unicode shell link API. WScript.Shell converts the path through
/// the ANSI code page, so a shortcut whose name that code page cannot spell (Chinese on an English system,
/// any emoji) silently fails to load. Never resolves the link: tracking may search disks or prompt.
/// </summary>
internal static class ShellLinkTarget
{
  private const uint StgmRead = 0x00000000;
  private const int TargetCapacity = 1024;

  internal static string? TryRead(string shortcutPath)
  {
    object? link = null;
    try
    {
      link = new ShellLink();
      ((IPersistFile)link).Load(shortcutPath, StgmRead);
      var target = new StringBuilder(TargetCapacity);
      ((IShellLinkW)link).GetPath(target, target.Capacity, IntPtr.Zero, 0);
      var path = target.ToString();
      return string.IsNullOrWhiteSpace(path) ? null : path;
    }
    catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException
      or ArgumentException or InvalidCastException)
    {
      return null;
    }
    finally
    {
      if (link is not null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
    }
  }

  [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
  private class ShellLink;

  [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IShellLinkW
  {
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
  }

  [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IPersistFile
  {
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
  }
}
