# Infrastructure: 电量读取（与 C# BatteryStatusInterpreter 对齐）

function Test-SmartGuardNoSystemBattery {
    param([byte]$BatteryFlag)
    return (($BatteryFlag -band 128) -ne 0)
}

function ConvertFrom-SmartGuardAcLineStatus {
    param([byte]$AcLineStatus)
    switch ($AcLineStatus) {
        1 { return $true }
        0 { return $false }
        default { return $null }
    }
}

function ConvertFrom-SmartGuardBatteryLifePercent {
    param([byte]$BatteryLifePercent)
    if ($BatteryLifePercent -eq 255) { return $null }
    return [int]$BatteryLifePercent
}

function ConvertFrom-SmartGuardWmiBatteryStatus {
    param([int]$BatteryStatus)
    if ($BatteryStatus -in 6, 7, 8, 9) { return $true }
    if ($BatteryStatus -in 4, 5, 11) { return $false }
    return $null
}

function Get-SmartGuardAggregatedBatteryPercent {
    param([array]$Batteries)
    if (-not $Batteries -or $Batteries.Count -eq 0) { return 100 }
    $weighted = 0.0
    $totalWeight = 0.0
    foreach ($bat in $Batteries) {
        $weight = [uint32]$bat.DesignCapacity
        if ($weight -eq 0) { $weight = 1 }
        $weighted += [double]$bat.EstimatedChargeRemaining * $weight
        $totalWeight += $weight
    }
    if ($totalWeight -le 0) { return 100 }
    return [int][Math]::Round($weighted / $totalWeight)
}

function Resolve-SmartGuardBatteryInfo {
    param(
        [byte]$AcLineStatus,
        [byte]$BatteryLifePercent,
        [byte]$BatteryFlag,
        [int]$WmiPercent = -1,
        $WmiOnAc = $null
    )
    $hasWmiPercent = $WmiPercent -ge 0
    if ((Test-SmartGuardNoSystemBattery -BatteryFlag $BatteryFlag) -and -not $hasWmiPercent) {
        return @{ Percent = 100; IsOnAC = $true }
    }

    $onAc = ConvertFrom-SmartGuardAcLineStatus -AcLineStatus $AcLineStatus
    if ($null -eq $onAc) { $onAc = if ($null -ne $WmiOnAc) { [bool]$WmiOnAc } else { $true } }

    $percent = ConvertFrom-SmartGuardBatteryLifePercent -BatteryLifePercent $BatteryLifePercent
    if ($null -eq $percent) {
        $percent = if ($hasWmiPercent) { $WmiPercent } else { 100 }
    }
    if ($percent -lt 0) { $percent = 0 }
    if ($percent -gt 100) { $percent = 100 }

    return @{ Percent = [int]$percent; IsOnAC = [bool]$onAc }
}

function Get-SmartGuardSystemPowerStatus {
    if (-not ('SmartGuard.SystemPowerStatus' -as [type])) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Sequential)]
public struct SmartGuardSystemPowerStatus {
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte Reserved1;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}
public static class SmartGuardSystemPowerStatusReader {
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(ref SmartGuardSystemPowerStatus status);
    public static SmartGuardSystemPowerStatus Read() {
        var status = new SmartGuardSystemPowerStatus();
        if (!GetSystemPowerStatus(ref status)) {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        return status;
    }
}
"@
    }
    return [SmartGuardSystemPowerStatusReader]::Read()
}

function Get-BatteryInfo {
    try {
        $wmiPercent = -1
        $wmiOnAc = $null
        $batteries = @(Get-CimInstance Win32_Battery -EA Stop)
        if ($batteries.Count -gt 0) {
            $wmiPercent = Get-SmartGuardAggregatedBatteryPercent -Batteries $batteries
            foreach ($bat in $batteries) {
                $hint = ConvertFrom-SmartGuardWmiBatteryStatus -BatteryStatus ([int]$bat.BatteryStatus)
                if ($hint -eq $true) { $wmiOnAc = $true }
                elseif ($hint -eq $false -and $wmiOnAc -ne $true) { $wmiOnAc = $false }
            }
        }

        $status = Get-SmartGuardSystemPowerStatus
        return Resolve-SmartGuardBatteryInfo `
            -AcLineStatus $status.ACLineStatus `
            -BatteryLifePercent $status.BatteryLifePercent `
            -BatteryFlag $status.BatteryFlag `
            -WmiPercent $wmiPercent `
            -WmiOnAc $wmiOnAc
    }
    catch {
        return @{ Percent = 100; IsOnAC = $true }
    }
}
