param(
    [string]$Root = 'D:\Project\SmartGuard',
    [string]$Version = '1.0.51',
    [string]$Iscc = 'D:\Apps\Inno Setup 6\ISCC.exe'
)

$iss = Join-Path $Root 'installer\SmartGuard.iss'
$staging = Join-Path $Root 'installer\staging'

& $Iscc `
    "/DStagingDir=$staging" `
    "/DMyAppVersion=$Version" `
    "/DRuntimeInstallerFile=windowsdesktop-runtime-8.0.18-win-x64.exe" `
    $iss
