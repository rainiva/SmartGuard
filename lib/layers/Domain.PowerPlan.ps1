# Domain: 电源计划决策与展示

function Normalize-PlanGuid {
    param([string]$PlanGuid)
    if ($PlanGuid -match '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') {
        return $Matches[1].ToLower()
    }
    return $PlanGuid.Trim().ToLower()
}

function Get-ExpectedPlanGuid {
    param(
        [int]$IdleSeconds,
        [bool]$IsOnAC,
        [int]$BatteryPercent,
        [hashtable]$Config
    )

    if ($Config.Paused) { return $null }

    if ($IdleSeconds -ge $Config.PowerSaverThresholdSec) {
        return $Config.PowerSaverPlanGUID
    }
    if ($IdleSeconds -ge $Config.BalancedThresholdSec) {
        return $Config.BalancedPlanGUID
    }
    if ($IsOnAC -or $BatteryPercent -ge $Config.LowBatteryPercent) {
        return $Config.ActivePlanGUID
    }
    return $Config.BalancedPlanGUID
}

function Test-ExternalPlanChange {
    param([string]$PreviousGuid, [string]$CurrentGuid, [bool]$ScriptJustSwitched)
    if ($ScriptJustSwitched) { return $false }
    if ([string]::IsNullOrWhiteSpace($PreviousGuid)) { return $false }
    return ($PreviousGuid.ToLower() -ne $CurrentGuid.ToLower())
}

function Get-PowerPlanCatalog {
    $output = powercfg /list 2>&1 | Out-String
    $catalog = @{}
    foreach ($match in [regex]::Matches($output, 'GUID:\s+([0-9a-fA-F-]{36})\s+\(([^)]+)\)', 'IgnoreCase')) {
        $catalog[$match.Groups[1].Value.ToLower()] = $match.Groups[2].Value.Trim()
    }
    return $catalog
}

function Get-PlanDisplayName {
    param(
        [string]$PlanGuid,
        [hashtable]$Config,
        [hashtable]$Catalog = $null,
        [string]$PreferredName = $null
    )
    $g = $PlanGuid.ToLower()
    switch ($g) {
        { $_ -eq $Config.ActivePlanGUID.ToLower() } { return '高性能' }
        { $_ -eq $Config.BalancedPlanGUID.ToLower() } { return '平衡' }
        { $_ -eq $Config.PowerSaverPlanGUID.ToLower() } { return '节能' }
        default {
            if ($Catalog -and $Catalog.ContainsKey($g)) { return $Catalog[$g] }
            if (-not [string]::IsNullOrWhiteSpace($PreferredName)) { return $PreferredName }
            return $PlanGuid
        }
    }
}

function Get-CurrentPlanGuidFromOutput {
    param([string]$PowerCfgListOutput)
    if ($PowerCfgListOutput -match 'GUID:\s+([0-9a-fA-F-]{36})') { return $Matches[1].ToLower() }
    return $null
}

function Get-CurrentPlanInfo {
    $output = powercfg /getactivescheme 2>&1 | Out-String
    $guid = Get-CurrentPlanGuidFromOutput -PowerCfgListOutput $output
    $name = $null
    if ($output -match 'GUID:\s+([0-9a-fA-F-]{36})\s+\(([^)]+)\)') {
        if ($guid -and $Matches[1].ToLower() -eq $guid) {
            $name = $Matches[2].Trim()
        }
    }
    return @{ Guid = $guid; Name = $name }
}

function Get-CurrentPlanGuid {
    return (Get-CurrentPlanInfo).Guid
}

function Resolve-PlanDisplayName {
    param(
        [string]$PlanGuid,
        [hashtable]$Config,
        [hashtable]$Catalog,
        $ActivePlanInfo
    )
    $preferredName = $null
    if ($ActivePlanInfo -and $PlanGuid -and $ActivePlanInfo.Guid -eq $PlanGuid.ToLower()) {
        $preferredName = $ActivePlanInfo.Name
    }
    return Get-PlanDisplayName -PlanGuid $PlanGuid -Config $Config -Catalog $Catalog -PreferredName $preferredName
}

function Test-PlanChangedForNotification {
    param([string]$PreviousPlan, [string]$CurrentPlan)
    if ([string]::IsNullOrWhiteSpace($CurrentPlan)) { return $false }
    if ([string]::IsNullOrWhiteSpace($PreviousPlan)) { return $false }
    return ($PreviousPlan -ne $CurrentPlan)
}
