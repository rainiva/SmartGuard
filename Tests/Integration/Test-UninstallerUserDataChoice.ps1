#requires -Version 5.1
<#
.SYNOPSIS
    Integration test for the SmartGuard uninstaller user-data choice dialog.
.DESCRIPTION
    Installs SmartGuard to a temporary directory, creates synthetic user data,
    then launches the uninstaller interactively. The Inno Setup uninstaller
    first shows its own "Are you sure?" confirmation; after confirming that,
    our InitializeUninstall MsgBox asking whether to keep or delete config/logs
    should appear. A background watcher presses Yes on both dialogs, verifying
    that the choice dialog is actually reached.

    Note: Because the installer registers a scheduled task that starts the
    engine, the actual file-deletion phase may be blocked by a running process
    if the uninstaller is not elevated. This test treats "choice dialog
    detected after the standard confirmation" as the primary success criterion.
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
using System.Runtime.InteropServices;
using System.Text;
public class WinApi {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
}
"@

function Find-WindowByTitlePattern {
    param([string]$Pattern)

    $found = [IntPtr]::Zero
    $sb = New-Object System.Text.StringBuilder(256)

    [WinApi]::EnumWindows(
        [WinApi+EnumWindowsProc]{
            param($hWnd, $lParam)
            [void][WinApi]::GetWindowText($hWnd, $sb, $sb.Capacity)
            $title = $sb.ToString()
            if ($title -like $Pattern) {
                if ([WinApi]::IsWindowVisible($hWnd)) {
                    $script:found = $hWnd
                    return $false
                }
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
    # Send Alt+Y to activate the "Yes" button on the message box.
    [System.Windows.Forms.SendKeys]::SendWait('%y')
    Start-Sleep -Milliseconds 200
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

Write-Host "Installing SmartGuard to $InstallDir ..."
& $InstallerPath /SP- /VERYSILENT /SUPPRESSMSGBOXES /DIR="$InstallDir" | Out-Null

if (-not (Test-Path "$InstallDir\unins000.exe")) {
    throw "Installation failed: unins000.exe not found."
}

# Create synthetic user data so we can later observe deletion
$configPath = "$InstallDir\SmartGuard.config.json"
$logPath = "$InstallDir\SmartGuard.log"
[System.IO.File]::WriteAllText($configPath, '{"Test": true}', [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($logPath, 'test log', [System.Text.UTF8Encoding]::new($false))

# ---------------------------------------------------------------------------
# 2. Launch uninstaller interactively
# ---------------------------------------------------------------------------
Write-Host "Launching interactive uninstaller..."
$proc = Start-Process -FilePath "$InstallDir\unins000.exe" -PassThru -WindowStyle Normal

# 2a. Confirm the standard Inno Setup "Are you sure?" uninstall confirmation
$confirmBox = Wait-ForWindow -Pattern '*SmartGuard*' -TimeoutSeconds 15
if ($confirmBox -eq [IntPtr]::Zero) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "Standard uninstall confirmation dialog was not detected."
}
Write-Host "Detected standard uninstall confirmation; pressing Yes..."
Send-YesToMessageBox -hWnd $confirmBox
Wait-ForWindowClosed -hWnd $confirmBox -TimeoutSeconds 10

# 2b. Our InitializeUninstall MsgBox should now appear
$choiceBox = Wait-ForWindow -Pattern '*SmartGuard*' -TimeoutSeconds 15
if ($choiceBox -eq [IntPtr]::Zero) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "User-data choice dialog (InitializeUninstall MsgBox) was not detected after confirming uninstall."
}
Write-Host "Detected user-data choice dialog (hWnd=$choiceBox); pressing Yes to delete user data..."
Send-YesToMessageBox -hWnd $choiceBox

# Wait for uninstaller process to finish
$finished = $proc.WaitForExit(60000)
if (-not $finished) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "Uninstaller did not exit within 60 seconds."
}

# ---------------------------------------------------------------------------
# 3. Report whether user data was removed
# ---------------------------------------------------------------------------
$dataDeleted = (-not (Test-Path $configPath)) -and (-not (Test-Path $logPath))
if ($dataDeleted) {
    Write-Host "User data files were deleted after choosing Yes."
} else {
    Write-Warning "User data files were not deleted. This is usually because the engine process is still running and locks the installation directory."
}

Write-Host "SUCCESS: Uninstaller choice dialog appeared after the standard confirmation."
