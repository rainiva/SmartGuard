#Requires -Version 5.1
<#
  真实用户流程集成测试：模拟「安装目录 + 计划任务启动引擎 + 托盘读状态」。
  依赖已发布的 bin\SmartGuard.Engine.exe（Run-Tests 前需 Publish-All 或已有 bin 输出）。
#>
Describe 'Tray core user flow (integration)' {
    BeforeAll {
        . (Join-Path $PSScriptRoot 'TrayCoreUserFlow.Helpers.ps1')
        Stop-SmartGuardForTrayCoreTest
        $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
        Initialize-TrayCoreUserFlowContext -RepoRoot $repoRoot
    }

    AfterAll {
        Stop-SmartGuardForTrayCoreTest
    }

    It 'scheduled-task style start (cwd=install root, no --root) writes status.json beside tray' {
        $installRoot = Join-Path $env:TEMP ('sg-user-flow-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
        Copy-EnginePayload -InstallRoot $installRoot
        $engine = Join-Path $installRoot 'bin\SmartGuard.Engine.exe'
        $statusPath = Join-Path $installRoot 'SmartGuard.status.json'
        Test-Path -LiteralPath $engine | Should -Be $true

        $proc = Start-Process -FilePath $engine -WorkingDirectory $installRoot -WindowStyle Hidden -PassThru
        try {
            $payload = Wait-StatusFile -StatusPath $statusPath
            $payload | Should -Not -BeNullOrEmpty
            $payload.currentPlan | Should -Not -BeNullOrEmpty
            Test-Path -LiteralPath (Join-Path $installRoot 'SmartGuard.config.json') | Should -Be $true
        }
        finally {
            Stop-EngineTree -ProcessId $proc.Id -InstallRoot $installRoot
            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }


    It 'tray menu would leave waiting state once status.json exists in same root' {
        $installRoot = Join-Path $env:TEMP ('sg-user-flow-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
        Copy-EnginePayload -InstallRoot $installRoot
        $engine = Join-Path $installRoot 'bin\SmartGuard.Engine.exe'
        $statusPath = Join-Path $installRoot 'SmartGuard.status.json'

        $proc = Start-Process -FilePath $engine -ArgumentList @('--root', $installRoot) -WorkingDirectory $installRoot -WindowStyle Hidden -PassThru
        try {
            $payload = Wait-StatusFile -StatusPath $statusPath
            $payload | Should -Not -BeNullOrEmpty
            $payload.currentPlan | Should -Not -BeNullOrEmpty

            dotnet test (Join-Path $global:SG_TestRepoRoot 'Tests\SmartGuard.Tray.Tests\SmartGuard.Tray.Tests.csproj') --filter "TrayReadinessTests" --nologo -v q | Out-Null
            $LASTEXITCODE | Should -Be 0
        }
        finally {
            Stop-EngineTree -ProcessId $proc.Id -InstallRoot $installRoot
            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
