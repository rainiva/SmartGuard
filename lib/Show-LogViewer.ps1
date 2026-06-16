#Requires -Version 5.1
$scriptRoot = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { 'D:\Project\SmartGuard' }
. (Join-Path $scriptRoot 'lib\SmartGuard.Functions.ps1')

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not (Enter-SingleInstanceMutex -Name 'LogViewer')) {
    $title = -join ([char]0x667A, [char]0x80FD, [char]0x7535, [char]0x6E90, [char]0x8BA1, [char]0x5212)
    $msg = -join ([char]0x65E5, [char]0x5FD7, [char]0x7A97, [char]0x53E3, [char]0x5DF2, [char]0x5728, [char]0x8FD0, [char]0x884C, [char]0x3002)
    [System.Windows.Forms.MessageBox]::Show(
        $msg,
        $title,
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
    exit 0
}

Start-SmartGuardLogViewerApp -ScriptRoot $scriptRoot
