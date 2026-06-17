# Start-Core.ps1
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { 'D:\Project\SmartGuard' }
$engineExe = Join-Path $root 'bin\SmartGuard.Engine.exe'
$coreScript = Join-Path $root 'lib\SmartGuard.Core.ps1'
$logPath = Join-Path $root 'SmartGuard.startup.log'

function Write-StartupLog {
    param([string]$Message)
    $line = '{0} {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Wait-DismissConsole {
    param([string]$Message)
    Write-StartupLog $Message
    Write-Host ''
    Write-Host $Message -ForegroundColor Yellow
    Read-Host 'Press Enter to close this window'
}

Write-StartupLog ("Launcher started. Admin={0}" -f (Test-IsAdministrator))

if (-not (Test-IsAdministrator)) {
    Write-Host 'Need administrator rights. Please click Yes on the UAC prompt...' -ForegroundColor Cyan
    Write-StartupLog 'Requesting UAC elevation'
    try {
        $argList = '-NoProfile -ExecutionPolicy Bypass -NoExit -File "{0}"' -f $MyInvocation.MyCommand.Path
        $proc = Start-Process -FilePath 'powershell.exe' -Verb RunAs -PassThru -Wait -ArgumentList $argList
        if ($null -eq $proc) {
            Wait-DismissConsole 'UAC was cancelled or elevation failed.'
            exit 1
        }
        Write-StartupLog ("Elevated process exit code: {0}" -f $proc.ExitCode)
        if ($proc.ExitCode -ne 0) {
            Wait-DismissConsole ("Elevated launcher exited with code {0}. See SmartGuard.startup.log" -f $proc.ExitCode)
        }
        exit $proc.ExitCode
    }
    catch {
        Wait-DismissConsole ("Elevation error: {0}" -f $_.Exception.Message)
        exit 1
    }
}

if (Test-Path -LiteralPath $engineExe) {
    Write-StartupLog "Running C# engine: $engineExe"
    Write-Host 'SmartGuard Engine is starting (admin)...' -ForegroundColor Green
    Write-Host 'Engine runs in background. Check SmartGuard.log for activity.'
    Write-Host 'To stop: Stop-Process -Name SmartGuard.Engine'
    Write-Host ''
    try {
        & $engineExe --root $root
        Write-StartupLog 'Engine exited normally'
        Wait-DismissConsole 'SmartGuard Engine stopped.'
    }
    catch {
        Write-StartupLog ("Engine failed: {0}" -f $_.Exception.Message)
        Wait-DismissConsole ("SmartGuard Engine failed: {0}" -f $_.Exception.Message)
        exit 1
    }
}
else {
    Write-StartupLog "Engine exe not found; running PS fallback: $coreScript"
    Write-Host 'SmartGuard Core is starting (admin, PowerShell fallback)...' -ForegroundColor Green
    Write-Host 'Keep this window open. Closing it stops the service.'
    Write-Host ''
    try {
        & $coreScript
        Write-StartupLog 'Core script returned normally'
        Wait-DismissConsole 'SmartGuard Core stopped.'
    }
    catch {
        Write-StartupLog ("Core failed: {0}" -f $_.Exception.Message)
        Wait-DismissConsole ("SmartGuard Core failed: {0}" -f $_.Exception.Message)
        exit 1
    }
}
