# Infrastructure: 单实例互斥

function Get-SingleInstanceMutexName {
    param([string]$Component)
    return "Global\SmartGuard.$Component"
}

function Enter-SingleInstanceMutex {
    param([string]$Name)
    try {
        $mutexName = if ($Name -match '^Global\\') { $Name } else { Get-SingleInstanceMutexName -Component $Name }
        $script:SmartGuardInstanceMutex = New-Object System.Threading.Mutex($false, $mutexName)
        return $script:SmartGuardInstanceMutex.WaitOne(0, $false)
    }
    catch { return $false }
}
