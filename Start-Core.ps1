# Start-Core.ps1 - 智能电源计划核心服务启动器
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { 'C:\Tools' }
$coreScript = Join-Path $root 'lib\SmartPowerPlan.Core.ps1'
$logPath = Join-Path $root 'SmartPowerPlan.startup.log'

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
    Read-Host '按 Enter 键关闭此窗口'
}

Write-StartupLog ("启动器开始，管理员={0}" -f (Test-IsAdministrator))

if (-not (Test-IsAdministrator)) {
    Write-Host '需要管理员权限，请在 UAC 提示中点击「是」…' -ForegroundColor Cyan
    Write-StartupLog '请求 UAC 提权'
    try {
        $argList = '-NoProfile -ExecutionPolicy Bypass -NoExit -File "{0}"' -f $MyInvocation.MyCommand.Path
        $proc = Start-Process -FilePath 'powershell.exe' -Verb RunAs -PassThru -Wait -ArgumentList $argList
        if ($null -eq $proc) {
            Wait-DismissConsole 'UAC 已取消或提权失败。'
            exit 1
        }
        Write-StartupLog ("提权进程退出码：{0}" -f $proc.ExitCode)
        if ($proc.ExitCode -ne 0) {
            Wait-DismissConsole ("提权启动器退出，代码 {0}。详见 SmartPowerPlan.startup.log" -f $proc.ExitCode)
        }
        exit $proc.ExitCode
    }
    catch {
        Wait-DismissConsole ("提权失败：{0}" -f $_.Exception.Message)
        exit 1
    }
}

Write-StartupLog '正在运行核心脚本'
Write-Host '智能电源计划核心服务启动中（管理员）…' -ForegroundColor Green
Write-Host '请保持此窗口打开；关闭窗口将停止服务。'
Write-Host ''

try {
    & $coreScript
    Write-StartupLog '核心脚本正常返回'
    Wait-DismissConsole '智能电源计划核心服务已停止。'
}
catch {
    Write-StartupLog ("核心服务失败：{0}" -f $_.Exception.Message)
    Wait-DismissConsole ("智能电源计划核心服务失败：{0}" -f $_.Exception.Message)
    exit 1
}
