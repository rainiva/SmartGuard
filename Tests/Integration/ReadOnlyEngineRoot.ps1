# Integration tests start a real Engine against this machine. Seed its root first so that Engine only reads
# power settings: the init marker skips the one-time brightness alignment (the capability probe is a read),
# and a paused config keeps the policy from switching the active plan. ASCII only.
. (Join-Path $PSScriptRoot '..\..\scripts\SmartGuardPathConstants.ps1')

function Initialize-SmartGuardReadOnlyEngineRoot {
    param([Parameter(Mandatory = $true)][string]$Root)

    New-Item -ItemType Directory -Path $Root -Force | Out-Null
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText(
        (Join-Path $Root $SmartGuardInitMarkerFileName),
        'seeded-by-integration-test',
        $utf8)
    [System.IO.File]::WriteAllText(
        (Join-Path $Root $SmartGuardConfigFileName),
        '{"ActivePlanGUID":"8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c","BalancedPlanGUID":"381b4222-f694-41f0-9685-ff5bb260df2e","PowerSaverPlanGUID":"a1841308-3541-4fab-bc81-f71556f20b4a","Paused":true,"LogFile":""}',
        $utf8)
}
