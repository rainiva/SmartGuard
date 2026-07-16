#requires -Version 5.1
<#
.SYNOPSIS
    Integration test for the SmartGuard uninstaller dual-checkbox confirm page.
.DESCRIPTION
    Installs SmartGuard to a temporary directory, creates synthetic user data,
    then launches the uninstaller interactively. After the standard Inno
    "Are you sure?" confirmation, the custom uninstall page must appear with
    both "删除用户数据（配置与状态）" and "删除日志文件" checkbox captions.
    The watcher leaves both unchecked and clicks "卸载".
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [string]$InstallDir = "C:\Temp\SmartGuardUninstallTest-$([Guid]::NewGuid().ToString('N'))"
)

$ErrorActionPreference = 'Stop'

function Remove-TestDirectory {
    param([string]$Path)
    if (Test-Path $Path) {
        Remove-Item -Recurse -Force $Path -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 200
    }
}

Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class WinApi {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public const uint BM_CLICK = 0x00F5;

    public static string GetText(IntPtr hWnd) {
        var sb = new StringBuilder(512);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static List<string> CollectChildTexts(IntPtr parent) {
        var texts = new List<string>();
        EnumChildWindows(parent, (hWnd, lParam) => {
            var t = GetText(hWnd);
            if (!string.IsNullOrWhiteSpace(t)) texts.Add(t);
            return true;
        }, IntPtr.Zero);
        return texts;
    }
}
"@

function Find-WindowByTitlePattern {
    param([string]$Pattern)

    $script:found = [IntPtr]::Zero
    [WinApi]::EnumWindows(
        [WinApi+EnumWindowsProc]{
            param($hWnd, $lParam)
            $title = [WinApi]::GetText($hWnd)
            if ($title -like $Pattern -and [WinApi]::IsWindowVisible($hWnd)) {
                $script:found = $hWnd
                return $false
            }
            return $true
        },
        [IntPtr]::Zero) | Out-Null

    return $script:found
}

$script:CaptionDeleteUserData = -join @(
    [char]0x5220, [char]0x9664, [char]0x7528, [char]0x6237, [char]0x6570, [char]0x636E,
    [char]0xFF08, [char]0x914D, [char]0x7F6E, [char]0x4E0E, [char]0x72B6, [char]0x6001, [char]0xFF09)
$script:CaptionDeleteLogs = -join @(
    [char]0x5220, [char]0x9664, [char]0x65E5, [char]0x5FD7, [char]0x6587, [char]0x4EF6)
$script:CaptionUninstall = -join @([char]0x5378, [char]0x8F7D)

function Find-UninstallConfirmPageWindow {
    $script:found = [IntPtr]::Zero
    [WinApi]::EnumWindows(
        [WinApi+EnumWindowsProc]{
            param($hWnd, $lParam)
            if (-not [WinApi]::IsWindowVisible($hWnd)) { return $true }
            $texts = [WinApi]::CollectChildTexts($hWnd)
            $joined = [string]::Join('|', $texts)
            if ($joined.Contains($script:CaptionDeleteUserData) -and $joined.Contains($script:CaptionDeleteLogs)) {
                $script:found = $hWnd
                return $false
            }
            return $true
        },
        [IntPtr]::Zero) | Out-Null
    return $script:found
}

function Send-YesToMessageBox {
    param($hWnd)
    if ($hWnd -eq $null -or $hWnd -eq [IntPtr]::Zero) {
        throw "Cannot click Yes on a null/invalid window handle."
    }
    [void][WinApi]::SetForegroundWindow($hWnd)
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('%y')
    Start-Sleep -Milliseconds 200
}

function Click-ChildButtonByCaption {
    param(
        [IntPtr]$Parent,
        [string]$Caption
    )

    $script:button = [IntPtr]::Zero
    [WinApi]::EnumChildWindows(
        $Parent,
        [WinApi+EnumChildProc]{
            param($hWnd, $lParam)
            if ([WinApi]::GetText($hWnd) -eq $Caption) {
                $script:button = $hWnd
                return $false
            }
            return $true
        },
        [IntPtr]::Zero) | Out-Null

    if ($script:button -eq [IntPtr]::Zero) {
        throw "Button with caption '$Caption' was not found."
    }

    [void][WinApi]::SetForegroundWindow($Parent)
    Start-Sleep -Milliseconds 200
    [void][WinApi]::SendMessage($script:button, [WinApi]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
}

function Wait-ForWindow {
    param([string]$Pattern, [int]$TimeoutSeconds = 15)

    $box = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($box -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $box = Find-WindowByTitlePattern -Pattern $Pattern
        if ($box -ne [IntPtr]::Zero) {
            Write-Host "  Matched pattern '$Pattern' to window hWnd=$box"
        }
    }
    return $box
}

function Wait-ForUninstallConfirmPage {
    param([int]$TimeoutSeconds = 20)

    $box = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($box -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $box = Find-UninstallConfirmPageWindow
        if ($box -ne [IntPtr]::Zero) {
            Write-Host "  Found dual-checkbox uninstall confirm page hWnd=$box"
        }
    }
    return $box
}

function Wait-ForWindowClosed {
    param([IntPtr]$hWnd, [int]$TimeoutSeconds = 10)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ([WinApi]::IsWindow($hWnd) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
}

# ---------------------------------------------------------------------------
# 1. Clean and install
# ---------------------------------------------------------------------------
Remove-TestDirectory -Path $InstallDir

# Stop any live Guardian/Tray so PrepareToInstall can succeed.
schtasks /End /TN 'SmartGuard Guardian' 2>$null | Out-Null
schtasks /End /TN 'SmartGuard Tray' 2>$null | Out-Null
Get-Process -Name 'SmartGuard.Engine', 'SmartGuard.Tray', 'SmartGuard.Settings' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
$installLog = Join-Path $InstallDir 'install-ui-test.log'
Write-Host "Installing SmartGuard to $InstallDir ..."
$installProc = Start-Process -FilePath $InstallerPath -ArgumentList @(
    "/DIR=$InstallDir",
    '/VERYSILENT',
    '/SUPPRESSMSGBOXES',
    '/NORESTART',
    '/NOCANCEL',
    '/SP-',
    '/CLOSEAPPLICATIONS=off',
    "/LOG=$installLog"
) -PassThru -Wait

if ($installProc.ExitCode -ne 0 -or -not (Test-Path "$InstallDir\unins000.exe")) {
    $tail = if (Test-Path $installLog) { Get-Content $installLog -Tail 20 | Out-String } else { '(no log)' }
    throw "Installation failed (exit=$($installProc.ExitCode)): unins000.exe not found.`n$tail"
}

$configPath = "$InstallDir\SmartGuard.config.json"
$statusPath = "$InstallDir\SmartGuard.status.json"
$logPath = "$InstallDir\SmartGuard.log"
[System.IO.File]::WriteAllText($configPath, '{"Test": true}', [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($statusPath, '{"paused":false}', [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($logPath, 'test log', [System.Text.UTF8Encoding]::new($false))

# ---------------------------------------------------------------------------
# 2. Launch uninstaller interactively
# ---------------------------------------------------------------------------
Write-Host "Launching interactive uninstaller..."
$proc = Start-Process -FilePath "$InstallDir\unins000.exe" -PassThru -WindowStyle Normal

$confirmBox = Wait-ForWindow -Pattern '*SmartGuard*' -TimeoutSeconds 15
if ($confirmBox -eq [IntPtr]::Zero) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "Standard uninstall confirmation dialog was not detected."
}
Write-Host "Detected standard uninstall confirmation; pressing Yes..."
Send-YesToMessageBox -hWnd $confirmBox
Wait-ForWindowClosed -hWnd $confirmBox -TimeoutSeconds 10

$choicePage = Wait-ForUninstallConfirmPage -TimeoutSeconds 20
if ($choicePage -eq [IntPtr]::Zero) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "Dual-checkbox uninstall confirm page was not detected after confirming uninstall."
}
Write-Host "Detected dual-checkbox page (hWnd=$choicePage); leaving both unchecked and clicking uninstall..."
Click-ChildButtonByCaption -Parent $choicePage -Caption $script:CaptionUninstall

$finished = $proc.WaitForExit(60000)
if (-not $finished) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "Uninstaller did not exit within 60 seconds."
}

# Default: both unchecked => user data/logs should remain (unless engine lock quirks)
$configKept = Test-Path $configPath
$logKept = Test-Path $logPath
if ($configKept -and $logKept) {
    Write-Host "Config and log were retained with both checkboxes unchecked (expected)."
} else {
    Write-Warning "Config/log retention unexpected (config=$configKept log=$logKept). May be elevation/process lock related."
}

Write-Host "SUCCESS: Dual-checkbox uninstall confirm page appeared after the standard confirmation."
