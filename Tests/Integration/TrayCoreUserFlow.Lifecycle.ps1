#Requires -Version 5.1

. (Join-Path $PSScriptRoot 'TrayCoreUserFlow.Helpers.ps1')
. (Join-Path $PSScriptRoot 'Restore-SmartGuardScheduledTasks.ps1')

$script:TrayCoreUserFlowTaskSnapshot = $null

function Enter-PesterFileLifecycle {
    $snapshot = Capture-SmartGuardScheduledTasks
    try {
        Stop-SmartGuardForTrayCoreTest
        $script:TrayCoreUserFlowTaskSnapshot = $snapshot
    }
    catch {
        $enterError = $_
        try {
            Restore-SmartGuardScheduledTasks -Snapshot $snapshot
        }
        catch {
            throw (
                "Tray lifecycle enter failed: $($enterError.Exception.Message) " +
                "Baseline restore also failed: $($_.Exception.Message)")
        }
        throw $enterError
    }
}

function Exit-PesterFileLifecycle {
    $snapshot = $script:TrayCoreUserFlowTaskSnapshot
    if ($null -eq $snapshot) {
        throw 'Tray lifecycle exit requires a successful enter snapshot.'
    }

    try {
        Stop-SmartGuardForTrayCoreTest
    }
    finally {
        try {
            Restore-SmartGuardScheduledTasks -Snapshot $snapshot
        }
        finally {
            $script:TrayCoreUserFlowTaskSnapshot = $null
        }
    }
}
