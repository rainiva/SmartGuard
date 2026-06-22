using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

namespace SmartGuard.Tray.Toast;

public static class ToastAumidRegistrar
{
  internal static Func<string, bool>? StartMenuShortcutWriterForTests;
  internal static int ShortcutWriteCountForTests { get; private set; }

  public static bool EnsureRegistered(string root)
  {
    try
    {
      RegisterAppUserModelId(root);
      EnsureStartMenuShortcut(root);
      return true;
    }
    catch
    {
      return false;
    }
  }

  internal static void ResetForTests()
  {
    StartMenuShortcutWriterForTests = null;
    ShortcutWriteCountForTests = 0;
  }

  private static void RegisterAppUserModelId(string root)
  {
    var regPath = $@"Software\Classes\AppUserModelId\{SmartGuardToastAppId.AppId}";
    using var key = Registry.CurrentUser.CreateSubKey(regPath, true);
    if (key is null) return;
    key.SetValue("DisplayName", SmartGuardToastAppId.DisplayName, RegistryValueKind.String);
    var iconPath = Path.Combine(root, "lib", "SmartGuard.ico");
    if (File.Exists(iconPath))
    {
      var iconUri = new Uri(iconPath).AbsoluteUri;
      key.SetValue("IconUri", iconUri, RegistryValueKind.String);
    }
  }

  private static string GetShortcutMarkerPath(string root)
    => Path.Combine(root, "lib", ".smartguard-toast-shortcut");

  private static void EnsureStartMenuShortcut(string root)
  {
    if (File.Exists(GetShortcutMarkerPath(root)))
      return;

    var programs = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "Microsoft", "Windows", "Start Menu", "Programs");
    if (!Directory.Exists(programs)) return;

    var shortcutPath = Path.Combine(programs, "SmartGuard.lnk");
    if (StartMenuShortcutWriterForTests?.Invoke(shortcutPath) == true)
    {
      MarkShortcutRegistered(root);
      return;
    }

    var shortcutTarget = ToastShortcutResolver.Resolve(root);

    var shellType = Type.GetTypeFromProgID("WScript.Shell");
    if (shellType is null) return;
    dynamic shell = Activator.CreateInstance(shellType)!;
    dynamic shortcut = shell.CreateShortcut(shortcutPath);
    shortcut.TargetPath = shortcutTarget.TargetPath;
    shortcut.Arguments = shortcutTarget.Arguments;
    shortcut.WorkingDirectory = shortcutTarget.WorkingDirectory;
    shortcut.Description = SmartGuardToastAppId.DisplayName;
    var iconPath = Path.Combine(root, "lib", "SmartGuard.ico");
    if (File.Exists(iconPath))
      shortcut.IconLocation = $"{iconPath},0";
    shortcut.Save();

    ShellLinkAppUserModelId.Apply(shortcutPath, SmartGuardToastAppId.AppId);
    MarkShortcutRegistered(root);
  }

  private static void MarkShortcutRegistered(string root)
  {
    ShortcutWriteCountForTests++;
    var markerPath = GetShortcutMarkerPath(root);
    var markerDir = Path.GetDirectoryName(markerPath);
    if (!string.IsNullOrEmpty(markerDir))
      Directory.CreateDirectory(markerDir);
    File.WriteAllText(markerPath, DateTime.UtcNow.ToString("o"));
  }
}

internal static class ShellLinkAppUserModelId
{
  private static readonly Guid PropertyStoreGuid = new("00021401-0000-0000-C000-000000000046");
  private static readonly PropertyKey AppUserModelIdKey = new(
    new Guid("9F4C2855-9F59-4B38-AC27-8B953B3F3C28"), 5);

  public static bool Apply(string shortcutPath, string appUserModelId)
  {
    if (!File.Exists(shortcutPath)) return false;
    var link = (IShellLinkW)new ShellLink();
    var persist = (IPersistFile)link;
    persist.Load(shortcutPath, 0);
    var store = (IPropertyStore)link;
    var key = AppUserModelIdKey;
    var pv = new PropVariant { vt = 31, pointerValue = Marshal.StringToCoTaskMemUni(appUserModelId) };
    try
    {
      store.SetValue(ref key, ref pv);
      store.Commit();
      persist.Save(shortcutPath, true);
      return true;
    }
    finally
    {
      if (pv.pointerValue != IntPtr.Zero)
        Marshal.FreeCoTaskMem(pv.pointerValue);
    }
  }

  [StructLayout(LayoutKind.Sequential, Pack = 4)]
  private struct PropertyKey(Guid fmtid, uint pid)
  {
    public Guid fmtid = fmtid;
    public uint pid = pid;
  }

  [StructLayout(LayoutKind.Explicit)]
  private struct PropVariant
  {
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public IntPtr pointerValue;
  }

  [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IPropertyStore
  {
    void GetCount(out uint cProps);
    void GetAt(uint iProp, out PropertyKey pkey);
    void GetValue(ref PropertyKey key, out PropVariant pv);
    void SetValue(ref PropertyKey key, ref PropVariant pv);
    void Commit();
  }

  [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
  private class ShellLink;

  [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IShellLinkW
  {
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPath, int dwReserved);
    void Resolve(IntPtr hwnd, int fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
  }

  [ComImport, Guid("0000010c-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IPersistFile
  {
    void GetClassID(out Guid pClassID);
    void IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
  }
}
