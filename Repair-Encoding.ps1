# Repair-Encoding.ps1 - convert UTF-16 ps1 files to UTF-8 BOM
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$utf8Bom = New-Object System.Text.UTF8Encoding $true

function Fix-Ps1Encoding {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        Write-Host "SKIP (missing): $Path"
        return
    }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $content = $null
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        $content = [System.Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
    }
    elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
        $content = [System.Text.Encoding]::BigEndianUnicode.GetString($bytes, 2, $bytes.Length - 2)
    }
    elseif ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        Write-Host "OK (already UTF-8 BOM): $Path"
        return
    }
    else {
        # UTF-16 LE without BOM (common bad save): even length, null bytes
        $nullCount = ($bytes | Where-Object { $_ -eq 0 }).Count
        if ($nullCount -gt ($bytes.Length / 4)) {
            $content = [System.Text.Encoding]::Unicode.GetString($bytes)
        }
        else {
            $content = [System.Text.Encoding]::UTF8.GetString($bytes)
        }
    }
    [System.IO.File]::WriteAllText($Path, $content, $utf8Bom)
    Write-Host "FIXED: $Path"
}

$files = Get-ChildItem -Path $root -Recurse -Filter '*.ps1' -File |
    Where-Object { $_.Name -notlike '_*' } |
    Select-Object -ExpandProperty FullName

foreach ($f in $files) { Fix-Ps1Encoding -Path $f }

function Write-Utf8BomFile {
    param([string]$Path, [string]$Content)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8Bom)
    Write-Host "WROTE: $Path"
}

# Root wrappers must delegate to lib (avoid stale duplicate logic)
Write-Utf8BomFile -Path (Join-Path $root 'SmartGuard.Core.ps1') -Content @'
# Forwarder: run lib implementation
#Requires -RunAsAdministrator
& (Join-Path $PSScriptRoot 'lib\SmartGuard.Core.ps1')
'@

Write-Utf8BomFile -Path (Join-Path $root 'SmartGuard.Functions.ps1') -Content @'
# Forwarder: dot-source lib implementation
. (Join-Path $PSScriptRoot 'lib\SmartGuard.Functions.ps1')
'@

Write-Utf8BomFile -Path (Join-Path $root 'Start-SmartGuard.ps1') -Content @'
# Double-click entry -> elevated launcher
$cmd = Join-Path $PSScriptRoot 'Start-Core.cmd'
Start-Process -FilePath $cmd -WorkingDirectory $PSScriptRoot
'@

Write-Utf8BomFile -Path (Join-Path $root 'Start-Core.ps1') -Content @'
# Start-Core.ps1
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { 'D:\Project\SmartGuard' }
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

Write-StartupLog 'Running Core script'
Write-Host 'SmartGuard Core is starting (admin)...' -ForegroundColor Green
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
'@

Write-Utf8BomFile -Path (Join-Path $root 'Register-AllTasks.ps1') -Content @'
# Register Core + Tray scheduled tasks
Write-Host 'Registering Core (admin required)...'
& (Join-Path $PSScriptRoot 'Register-SmartGuardTask.ps1')
Write-Host 'Registering Tray...'
& (Join-Path $PSScriptRoot 'Register-TrayTask.ps1')
Write-Host 'Done. Log off and log on, or run Tray manually:'
Write-Host '  powershell -Sta -File D:\Project\SmartGuard\lib\SmartGuard.Tray.ps1'
'@

Write-Utf8BomFile -Path (Join-Path $root 'Register-TrayTask.ps1') -Content @'
# Register tray scheduled task (user context, no admin)
$taskName = 'SmartGuard Tray'
$scriptPath = 'D:\Project\SmartGuard\lib\SmartGuard.Tray.ps1'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-WindowStyle Hidden -ExecutionPolicy Bypass -NoProfile -Sta -File `"$scriptPath`"" -WorkingDirectory 'D:\Project\SmartGuard'
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartInterval (New-TimeSpan -Minutes 1) -RestartCount 999
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force
Write-Host "Registered: $taskName"
'@

# Recreate Run-Tests.ps1 if missing
$runTests = Join-Path $root 'Run-Tests.ps1'
if (-not (Test-Path $runTests)) {
    $body = @'
$testPath = Join-Path $PSScriptRoot 'Tests\SmartGuard.Tests.ps1'
$resultPath = Join-Path $PSScriptRoot 'test-result.txt'
if (-not (Get-Module -ListAvailable -Name Pester)) {
    Install-Module -Name Pester -Scope CurrentUser -Force -SkipPublisherCheck
}
Import-Module Pester -MinimumVersion 5.0 -Force
$r = Invoke-Pester -Path $testPath -PassThru
"$(Get-Date -Format s) PASSED=$($r.PassedCount) FAILED=$($r.FailedCount) TOTAL=$($r.TotalCount)" | Out-File $resultPath -Encoding UTF8
Write-Host "PASSED=$($r.PassedCount) FAILED=$($r.FailedCount) TOTAL=$($r.TotalCount)"
if ($r.FailedCount -gt 0) { exit 1 }
'@
    [System.IO.File]::WriteAllText($runTests, $body, $utf8Bom)
    Write-Host "CREATED: $runTests"
}

# Recreate Register if missing
$register = Join-Path $root 'Register-SmartGuardTask.ps1'
if (-not (Test-Path $register)) {
    $body = @'
#Requires -RunAsAdministrator
$taskName = 'SmartGuard Guardian'
$scriptPath = 'D:\Project\SmartGuard\lib\SmartGuard.Core.ps1'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-WindowStyle Hidden -ExecutionPolicy Bypass -NoProfile -File `"$scriptPath`"" -WorkingDirectory 'D:\Project\SmartGuard'
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartInterval (New-TimeSpan -Minutes 1) -RestartCount 999
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force
Write-Host "Registered: $taskName"
'@
    [System.IO.File]::WriteAllText($register, $body, $utf8Bom)
    Write-Host "CREATED: $register"
}

Write-Host 'Done.'
