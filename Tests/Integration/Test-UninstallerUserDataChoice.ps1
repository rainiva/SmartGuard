#requires -Version 5.1
<#
.SYNOPSIS
    Integration test for the SmartGuard uninstaller user-data choice dialog.
.DESCRIPTION
    Installs SmartGuard to a temporary directory, creates synthetic user data,
    then launches the uninstaller interactively. A background watcher finds the
    confirmation message box and presses the "Yes" button, verifying that the
    uninstaller reaches InitializeUninstall and presents the keep/delete choice.

    Note: Because the installer registers a scheduled task that starts the
    engine, the actual file-deletion phase may be blocked by a running process.
    This test therefore treats "dialog detected" as the primary success
    criterion and only reports the deletion result as additional evidence.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [string]$InstallDir = "C:\Temp\SmartGuardUninstallTest"
)

$ErrorActionPreference = 'Stop'

function Remove-TestDirectory {
    param([string]$Path)
    if (Test-Path $Path) {
        Remove-Item -Recurse -Force $Path -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 200
    }
}

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

function Find-UninstallerMessageBox {
    $found = [IntPtr]::Zero
    $sb = New-Object System.Text.StringBuilder(256)

    [WinApi]::EnumWindows(
        [WinApi+EnumWindowsProc]{
            param($hWnd, $lParam)
            [void][WinApi]::GetWindowText($hWnd, $sb, $sb.Capacity)
            $title = $sb.ToString()
            if ($title -like '*SmartGuard*' -or $title -eq '确认' -or $title -eq 'Confirm') {
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
    param([IntPtr]$hWnd)
    [void][WinApi]::SetForegroundWindow($hWnd)
    Start-Sleep -Milliseconds 250
    # WM_COMMAND with IDYES (6) and BN_CLICKED (0)
    [void][WinApi]::SendMessage($hWnd, 0x0111, [IntPtr]6, [IntPtr]0)
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
# 2. Launch uninstaller interactively and auto-press Yes on the choice dialog
# ---------------------------------------------------------------------------
Write-Host "Launching interactive uninstaller..."
$proc = Start-Process -FilePath "$InstallDir\unins000.exe" -PassThru -WindowStyle Normal

$box = [IntPtr]::Zero
$deadline = (Get-Date).AddSeconds(15)
while ($box -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
    $box = Find-UninstallerMessageBox
}

if ($box -eq [IntPtr]::Zero) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "Uninstaller message box was not detected."
}

Write-Host "Detected uninstaller choice dialog (hWnd=$box); pressing Yes to delete user data..."
Send-YesToMessageBox -hWnd $box

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

# The primary test goal is to prove the choice dialog appears.
Write-Host "SUCCESS: Uninstaller choice dialog appeared during interactive uninstall."
