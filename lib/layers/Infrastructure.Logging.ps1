# Infrastructure: 日志写入与心跳

function Get-RotatedLogArchivePath {
    param([string]$LogPath)
    return "$LogPath.old"
}

function Add-SmartGuardLogLine {
    param([string]$LogPath, [string]$Line)
    if ([string]::IsNullOrWhiteSpace($LogPath)) { return $false }
    $dir = Split-Path -Parent $LogPath
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $fs = [System.IO.File]::Open($LogPath, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
    try {
        $sw = New-Object System.IO.StreamWriter($fs, (New-Object System.Text.UTF8Encoding $false))
        $sw.WriteLine($Line)
        $sw.Flush()
        $sw.Dispose()
        return $true
    }
    finally {
        $fs.Dispose()
    }
}

function Invoke-LogRotationIfNeeded {
    param([string]$LogPath, [long]$MaxBytes = 1048576)
    if ([string]::IsNullOrWhiteSpace($LogPath)) { return }
    if (-not (Test-Path $LogPath)) { return }
    if ((Get-Item -LiteralPath $LogPath).Length -le $MaxBytes) { return }
    $archive = Get-RotatedLogArchivePath -LogPath $LogPath
    if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
    Move-Item -LiteralPath $LogPath -Destination $archive -Force
}

function Write-SmartGuardLog {
    param(
        [string]$Message,
        [hashtable]$Config,
        [string]$FallbackLogPath = $null
    )
    if ([string]::IsNullOrEmpty($Config.LogFile)) { return $false }
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $Message"
    try {
        $maxBytes = if ($Config.LogMaxBytes) { [long]$Config.LogMaxBytes } else { 1048576 }
        Invoke-LogRotationIfNeeded -LogPath $Config.LogFile -MaxBytes $maxBytes
        if (Add-SmartGuardLogLine -LogPath $Config.LogFile -Line $line) {
            return $true
        }
    }
    catch {}
    if (-not [string]::IsNullOrWhiteSpace($FallbackLogPath)) {
        try {
            $fallbackLine = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - [LOG-FALLBACK] $Message"
            if (Add-SmartGuardLogLine -LogPath $FallbackLogPath -Line $fallbackLine) {
                return $false
            }
        }
        catch {}
    }
    return $false
}

function Format-HeartbeatLogMessage {
    param(
        [string]$Label,
        [string]$CurrentPlanName,
        [int]$BatteryPercent,
        [bool]$IsOnAC,
        [bool]$Paused
    )
    $pwr = if ($IsOnAC) { '插电' } else { '电池' }
    $pause = if ($Paused) { ' | 已暂停' } else { '' }
    return "[监控中] $Label | 计划正常 | $CurrentPlanName | 电量${BatteryPercent}% $pwr$pause"
}

function Test-HeartbeatDue {
    param(
        $LastHeartbeat,
        [int]$IntervalMinutes,
        [datetime]$Now
    )
    if ($IntervalMinutes -le 0) { return $false }
    if ($null -eq $LastHeartbeat) { return $false }
    return (($Now - $LastHeartbeat).TotalMinutes -ge $IntervalMinutes)
}

function Format-PlanChangeBalloon {
    param([string]$PlanName, [int]$Brightness)
    return "已切换至 $PlanName（亮度 ${Brightness}%）"
}

function Get-PauseGuardLogMessage {
    param(
        $PreviousPaused,
        [bool]$CurrentPaused
    )
    if ($null -eq $PreviousPaused) { return $null }
    if ([bool]$PreviousPaused -eq $CurrentPaused) { return $null }
    if ($CurrentPaused) { return '守护已暂停（仅监控，不切换计划）' }
    return '守护已恢复'
}
