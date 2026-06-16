# Infrastructure: Windows 通知中心 Toast

function Get-SmartPowerPlanToastAppId {
    return 'Tools.SmartPowerPlan.Guardian'
}

function Get-SmartPowerPlanToastDisplayName {
    return -join ([char]0x667A, [char]0x80FD, [char]0x7535, [char]0x6E90, [char]0x8BA1, [char]0x5212)
}

function Register-SmartPowerPlanAppUserModelId {
    param([string]$ScriptRoot = $null)
    try {
        $root = Get-SmartPowerPlanRoot -ScriptRoot $ScriptRoot
        $appId = Get-SmartPowerPlanToastAppId
        $regPath = "HKCU:\Software\Classes\AppUserModelId\$appId"
        if (-not (Test-Path -LiteralPath $regPath)) {
            New-Item -Path $regPath -Force | Out-Null
        }
        New-ItemProperty -LiteralPath $regPath -Name DisplayName -Value (Get-SmartPowerPlanToastDisplayName) -PropertyType String -Force | Out-Null
        $iconPath = Get-TrayIconPath -ScriptRoot $root
        if (Test-Path -LiteralPath $iconPath) {
            $iconUri = (New-Object Uri ((Resolve-Path -LiteralPath $iconPath).ProviderPath)).AbsoluteUri
            New-ItemProperty -LiteralPath $regPath -Name IconUri -Value $iconUri -PropertyType String -Force | Out-Null
        }
        return $true
    }
    catch {
        return $false
    }
}

function Set-ShortcutAppUserModelId {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$AppUserModelId
    )
    if (-not (Test-Path -LiteralPath $ShortcutPath)) { return $false }
    try {
        if (-not ('ShellLinkHelper' -as [type])) {
            Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class ShellLinkHelper
{
    private static readonly Guid PropertyStoreGuid = new Guid("00021401-0000-0000-C000-000000000046");
    private static readonly PropertyKey AppUserModelIdKey = new PropertyKey(new Guid("9F4C2855-9F59-4B38-AC27-8B953B3F3C28"), 5);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(Guid fmtid, uint pid) { this.fmtid = fmtid; this.pid = pid; }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    public class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellLinkW
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
    public interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    public static bool Apply(string shortcutPath, string appUserModelId)
    {
        var link = (IShellLinkW)new ShellLink();
        var persist = (IPersistFile)link;
        persist.Load(shortcutPath, 0);
        var store = (IPropertyStore)link;
        var key = AppUserModelIdKey;
        var pv = new PropVariant();
        pv.vt = 31;
        pv.pointerValue = Marshal.StringToCoTaskMemUni(appUserModelId);
        try
        {
            store.SetValue(ref key, ref pv);
            store.Commit();
            persist.Save(shortcutPath, true);
            return true;
        }
        finally
        {
            if (pv.pointerValue != IntPtr.Zero) { Marshal.FreeCoTaskMem(pv.pointerValue); }
        }
    }
}
'@
        }
        return [ShellLinkHelper]::Apply($ShortcutPath, $AppUserModelId)
    }
    catch {
        return $false
    }
}

function Initialize-SmartPowerPlanToastRegistration {
    param([string]$ScriptRoot = $null)
    try {
        $root = Get-SmartPowerPlanRoot -ScriptRoot $ScriptRoot
        $appId = Get-SmartPowerPlanToastAppId
        Register-SmartPowerPlanAppUserModelId -ScriptRoot $root | Out-Null

        $programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
        if (-not (Test-Path $programs)) { return $false }
        $lnk = Join-Path $programs 'SmartPowerPlan.lnk'
        $target = Join-Path $root 'Start-Tray.cmd'
        if (-not (Test-Path -LiteralPath $target)) { $target = Join-Path $root 'Restart-Tray.cmd' }
        if (-not (Test-Path -LiteralPath $target)) { $target = 'powershell.exe' }

        $wsh = New-Object -ComObject WScript.Shell
        $sc = $wsh.CreateShortcut($lnk)
        $sc.TargetPath = $target
        $sc.WorkingDirectory = $root
        $sc.Description = (Get-SmartPowerPlanToastDisplayName)
        $iconPath = Get-TrayIconPath -ScriptRoot $root
        if (Test-Path -LiteralPath $iconPath) {
            $sc.IconLocation = "$iconPath,0"
        }
        $sc.Save()

        Set-ShortcutAppUserModelId -ShortcutPath $lnk -AppUserModelId $appId | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Show-SmartPowerPlanToast {
    param(
        [string]$Title,
        [string]$Body,
        [string]$Tag = 'default',
        [string]$ScriptRoot = $null
    )
    if ([string]::IsNullOrWhiteSpace($Title)) { return $false }
    try {
        [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
        [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
        Initialize-SmartPowerPlanToastRegistration -ScriptRoot $ScriptRoot | Out-Null
        $appId = Get-SmartPowerPlanToastAppId
        $safeTitle = [System.Security.SecurityElement]::Escape($Title)
        $safeBody = [System.Security.SecurityElement]::Escape($Body)
        $xmlText = @"
<toast activation="protocol">
  <visual>
    <binding template="ToastGeneric">
      <text hint-maxLines="1">$safeTitle</text>
      <text hint-style="subtitle">$safeBody</text>
    </binding>
  </visual>
  <audio src="ms-winsoundevent:Notification.Reminder"/>
</toast>
"@
        $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
        $xml.LoadXml($xmlText)
        $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
        $toast.Tag = $Tag
        $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId)
        $notifier.Show($toast)
        return $true
    }
    catch {
        return $false
    }
}
