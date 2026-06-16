# Infrastructure: 安装路径解析

function Get-SmartGuardRoot {
    param([string]$ScriptRoot = $null)
    if (-not [string]::IsNullOrWhiteSpace($ScriptRoot)) {
        $normalized = $ScriptRoot.TrimEnd('\', '/')
        $leaf = Split-Path -Leaf $normalized
        if ($leaf -eq 'lib' -or $leaf -eq 'layers') {
            if ($leaf -eq 'layers') {
                return Split-Path -Parent (Split-Path -Parent $normalized)
            }
            return Split-Path -Parent $normalized
        }
        return $normalized
    }
    if ($PSScriptRoot) {
        $leaf = Split-Path -Leaf $PSScriptRoot
        if ($leaf -eq 'layers') {
            return Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        }
        if ($leaf -eq 'lib') {
            return Split-Path -Parent $PSScriptRoot
        }
        return $PSScriptRoot
    }
    return 'D:\Project\SmartGuard'
}

function Get-TrayIconPath {
    param([string]$ScriptRoot = $null)
    $root = Get-SmartGuardRoot -ScriptRoot $ScriptRoot
    return Join-Path $root 'lib\SmartGuard.ico'
}

function Get-SmartGuardFallbackLogPath {
    param([string]$ScriptRoot = $null)
    $root = Get-SmartGuardRoot -ScriptRoot $ScriptRoot
    return Join-Path $root 'SmartGuard.startup.log'
}
