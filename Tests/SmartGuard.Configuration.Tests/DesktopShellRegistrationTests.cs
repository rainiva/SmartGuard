using SmartGuard.Configuration.Shell;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace SmartGuard.Configuration.Tests;

public class DesktopShellRegistrationTests
{
  [Fact]
  public void Start_menu_shortcuts_collapse_to_one_primary_entrypoint()
  {
    var root = @"D:\Apps\SmartGuard";

    var shortcuts = DesktopShellRegistration.GetStartMenuShortcuts(root);

    shortcuts.Should().HaveCount(1);
    shortcuts[0].ShortcutPath.Should().EndWith("SmartGuard.lnk");
    shortcuts[0].TargetPath.Should().Be(SmartGuardPaths.TrayExe(root));
    shortcuts[0].Arguments.Should().Be($"--root \"{root}\"");
    shortcuts[0].AppUserModelId.Should().Be(SmartGuardToastAppId.AppId);
  }

  [Fact]
  public void Desktop_shortcut_points_to_tray_entrypoint()
  {
    var root = @"D:\Apps\SmartGuard";

    var shortcut = DesktopShellRegistration.GetDesktopShortcut(root);

    shortcut.TargetPath.Should().Be(SmartGuardPaths.TrayExe(root));
    shortcut.Arguments.Should().Be($"--root \"{root}\"");
    shortcut.AppUserModelId.Should().Be(SmartGuardToastAppId.AppId);
  }

  [Fact]
  public void Shell_shortcuts_continue_to_consume_tray_exe_icon_not_loose_ico_path()
  {
    var root = @"D:\Apps\SmartGuard";
    var start = DesktopShellRegistration.GetStartMenuShortcuts(root).Single();
    var desktop = DesktopShellRegistration.GetDesktopShortcut(root);

    start.IconLocation.Should().Be($"{SmartGuardPaths.TrayExe(root)},0");
    desktop.IconLocation.Should().Be($"{SmartGuardPaths.TrayExe(root)},0");
  }

  [Fact]
  public void Delete_directory_tree_removes_legacy_shortcuts_with_unicode_names()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-shell-delete-" + Guid.NewGuid().ToString("N"));
    var nested = Path.Combine(root, "SmartGuard");
    Directory.CreateDirectory(nested);
    foreach (var name in new[] { "SmartGuard 托盘.lnk", "SmartGuard 设置.lnk", "SmartGuard 日志.lnk", "卸载 SmartGuard.lnk" })
      File.WriteAllBytes(Path.Combine(nested, name), []);

    var method = typeof(DesktopShellRegistration).GetMethod(
      "DeleteDirectoryTree",
      BindingFlags.NonPublic | BindingFlags.Static);

    method.Should().NotBeNull();
    method!.Invoke(null, [nested]);

    Directory.Exists(nested).Should().BeFalse();
    Directory.Exists(root).Should().BeTrue();

    Directory.Delete(root, false);
  }

  [Fact]
  public void Shell_link_app_user_model_id_loads_shortcuts_in_writeable_mode()
  {
    var repoRoot = Path.GetFullPath(Path.Combine(
      Path.GetDirectoryName(typeof(DesktopShellRegistrationTests).Assembly.Location)!,
      "..", "..", "..", "..", ".."));
    var sourcePath = Path.Combine(repoRoot, "src", "SmartGuard.Configuration", "Shell", "ShellLinkAppUserModelId.cs");

    File.ReadAllText(sourcePath).Should().Contain("persist.Load(shortcutPath, StgmReadWrite");
    File.ReadAllText(sourcePath).Should().NotContain("persist.Load(shortcutPath, 0);");
  }

  [Fact]
  public void Desktop_shell_registration_releases_wscript_shortcut_com_objects_before_app_id_rewrite()
  {
    var repoRoot = Path.GetFullPath(Path.Combine(
      Path.GetDirectoryName(typeof(DesktopShellRegistrationTests).Assembly.Location)!,
      "..", "..", "..", "..", ".."));
    var sourcePath = Path.Combine(repoRoot, "src", "SmartGuard.Configuration", "Shell", "DesktopShellRegistration.cs");
    var source = File.ReadAllText(sourcePath);

    source.Should().Contain("Marshal.FinalReleaseComObject(link);");
    source.Should().Contain("Marshal.FinalReleaseComObject(shell);");
    source.Should().Contain("ShellLinkAppUserModelId.Apply(shortcut.ShortcutPath, shortcut.AppUserModelId);");
  }

  [Fact]
  public void Shell_link_app_user_model_id_round_trips_shortcut_property()
  {
    RunOnSta(() =>
    {
      var directory = Path.Combine(Path.GetTempPath(), "sg-shell-link-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var shortcutPath = Path.Combine(directory, "SmartGuard.lnk");

      try
      {
        CreateShortcut(shortcutPath, Environment.ProcessPath ?? throw new InvalidOperationException("Current process path is unavailable."));

        ShellLinkAppUserModelId.Apply(shortcutPath, SmartGuardToastAppId.AppId).Should().BeTrue();
        ReadShortcutAppUserModelIdViaShell(shortcutPath).Should().Be(SmartGuardToastAppId.AppId);
      }
      finally
      {
        if (File.Exists(shortcutPath))
          File.Delete(shortcutPath);

        if (Directory.Exists(directory))
          Directory.Delete(directory, false);
      }
    });
  }

  [Fact]
  public void Shell_application_observes_app_user_model_id_when_shortcut_is_created_via_shell_link_com()
  {
    RunOnSta(() =>
    {
      var directory = Path.Combine(Path.GetTempPath(), "sg-shell-link-direct-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var shortcutPath = Path.Combine(directory, "SmartGuard.lnk");

      try
      {
        CreateShortcutWithAppUserModelId(
          shortcutPath,
          Environment.ProcessPath ?? throw new InvalidOperationException("Current process path is unavailable."),
          SmartGuardToastAppId.AppId);

        var viaShell = ReadShortcutAppUserModelIdViaShell(shortcutPath);

        viaShell.Should().Be(SmartGuardToastAppId.AppId);
      }
      finally
      {
        if (File.Exists(shortcutPath))
          File.Delete(shortcutPath);

        if (Directory.Exists(directory))
          Directory.Delete(directory, false);
      }
    });
  }

  private static void RunOnSta(Action action)
  {
    Exception? error = null;
    var thread = new Thread(() =>
    {
      try
      {
        action();
      }
      catch (Exception ex)
      {
        error = ex;
      }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (error is not null)
      throw error;
  }

  private static void CreateShortcut(string shortcutPath, string targetPath)
  {
    var shellType = Type.GetTypeFromProgID("WScript.Shell")
      ?? throw new InvalidOperationException("WScript.Shell is not available.");

    dynamic? shell = null;
    dynamic? link = null;
    try
    {
      shell = Activator.CreateInstance(shellType)!;
      link = shell.CreateShortcut(shortcutPath);
      link.TargetPath = targetPath;
      link.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
      link.Save();
    }
    finally
    {
      if (link is not null && Marshal.IsComObject(link))
        Marshal.FinalReleaseComObject(link);

      if (shell is not null && Marshal.IsComObject(shell))
        Marshal.FinalReleaseComObject(shell);
    }
  }

  private static void CreateShortcutWithAppUserModelId(string shortcutPath, string targetPath, string appUserModelId)
  {
    var link = (ShortcutAppUserModelIdReader.IShellLinkW)new ShortcutAppUserModelIdReader.ShellLink();
    link.SetPath(targetPath);
    link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);

    var store = (ShortcutAppUserModelIdReader.IPropertyStore)link;
    var key = ShortcutAppUserModelIdReader.AppUserModelIdKey;
    var pv = ShortcutAppUserModelIdReader.PropVariant.CreateString(appUserModelId);
    try
    {
      store.SetValue(ref key, ref pv);
      store.Commit();

      var persist = (ShortcutAppUserModelIdReader.IPersistFile)link;
      persist.Save(shortcutPath, true);
    }
    finally
    {
      pv.Dispose();
    }
  }

  private static string? ReadShortcutAppUserModelIdViaShell(string shortcutPath)
  {
    var shellType = Type.GetTypeFromProgID("Shell.Application")
      ?? throw new InvalidOperationException("Shell.Application is not available.");

    dynamic? shell = null;
    dynamic? folder = null;
    dynamic? item = null;
    try
    {
      shell = Activator.CreateInstance(shellType)!;
      folder = shell.NameSpace(Path.GetDirectoryName(shortcutPath));
      item = folder?.ParseName(Path.GetFileName(shortcutPath));
      return item?.ExtendedProperty("System.AppUserModel.ID");
    }
    finally
    {
      if (item is not null && Marshal.IsComObject(item))
        Marshal.FinalReleaseComObject(item);

      if (folder is not null && Marshal.IsComObject(folder))
        Marshal.FinalReleaseComObject(folder);

      if (shell is not null && Marshal.IsComObject(shell))
        Marshal.FinalReleaseComObject(shell);
    }
  }

  private static class ShortcutAppUserModelIdReader
  {
    internal static readonly PropertyKey AppUserModelIdKey = new(
      new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PropertyKey(Guid fmtid, uint pid)
    {
      public Guid fmtid = fmtid;
      public uint pid = pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
      [FieldOffset(0)] public ushort vt;
      [FieldOffset(8)] public IntPtr pointerValue;

      public static PropVariant CreateString(string value)
        => new()
        {
          vt = 31,
          pointerValue = Marshal.StringToCoTaskMemUni(value),
        };

      public string? GetValue()
        => vt == 31 && pointerValue != IntPtr.Zero ? Marshal.PtrToStringUni(pointerValue) : null;

      public void Dispose()
      {
        if (pointerValue != IntPtr.Zero)
          Marshal.FreeCoTaskMem(pointerValue);
      }
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
      void GetCount(out uint cProps);
      void GetAt(uint iProp, out PropertyKey pkey);
      void GetValue(ref PropertyKey key, out PropVariant pv);
      void SetValue(ref PropertyKey key, ref PropVariant pv);
      void Commit();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLink;

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
      void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
      void GetIDList(out IntPtr ppidl);
      void SetIDList(IntPtr pidl);
      void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
      void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
      void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
      void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
      void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
      void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
      void GetHotkey(out short pwHotkey);
      void SetHotkey(short wHotkey);
      void GetShowCmd(out int piShowCmd);
      void SetShowCmd(int iShowCmd);
      void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
      void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
      void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPath, int dwReserved);
      void Resolve(IntPtr hwnd, int fFlags);
      void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010c-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPersistFile
    {
      void GetClassID(out Guid pClassID);
      void IsDirty();
      void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
      void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
      void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
      void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
  }
}
