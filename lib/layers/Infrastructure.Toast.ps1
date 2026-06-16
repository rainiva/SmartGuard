# Infrastructure: Windows 通知中心 Toast

function Get-SmartPowerPlanToastAppId {
    return 'Tools.SmartPowerPlan.Guardian'
}

function Initialize-SmartPowerPlanToastRegistration {
    param([string]$ScriptRoot = 'C:\Tools')
    try {
        $appId = Get-SmartPowerPlanToastAppId
        $programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
        if (-not (Test-Path $programs)) { return $false }
        $lnk = Join-Path $programs 'SmartPowerPlan.lnk'
        if (Test-Path $lnk) { return $true }
        $target = Join-Path $ScriptRoot 'Start-Tray.cmd'
        if (-not (Test-Path $target)) { $target = 'powershell.exe' }
        $wsh = New-Object -ComObject WScript.Shell
        $sc = $wsh.CreateShortcut($lnk)
        $sc.TargetPath = $target
        $sc.WorkingDirectory = $ScriptRoot
        $sc.Description = '智能电源计划'
        $sc.Save()
        $bytes = [System.IO.File]::ReadAllBytes($lnk)
        $shellLinkGuid = [guid]('00021401-0000-0000-C000-000000000046')
        $dataOffset = [BitConverter]::ToUInt32($bytes, 76)
        $appIdBytes = [System.Text.Encoding]::Unicode.GetBytes($appId + [char]0)
        $newBytes = New-Object byte[] ($bytes.Length + $appIdBytes.Length + 8)
        [Array]::Copy($bytes, $newBytes, $bytes.Length)
        [BitConverter]::GetBytes($appIdBytes.Length).CopyTo($newBytes, $bytes.Length)
        [Array]::Copy($appIdBytes, 0, $newBytes, $bytes.Length + 4, $appIdBytes.Length)
        [System.IO.File]::WriteAllBytes($lnk, $newBytes)
        return $true
    }
    catch {
        return $false
    }
}

function Show-SmartPowerPlanToast {
    param(
        [string]$Title,
        [string]$Body,
        [string]$Tag = 'default',
        [string]$ScriptRoot = 'C:\Tools'
    )
    if ([string]::IsNullOrWhiteSpace($Title)) { return $false }
    try {
        [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
        [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
        Initialize-SmartPowerPlanToastRegistration -ScriptRoot $ScriptRoot | Out-Null
        $appId = Get-SmartPowerPlanToastAppId
        $safeTitle = [System.Security.SecurityElement]::Escape($Title)
        $safeBody = [System.Security.SecurityElement]::Escape($Body)
        $xmlText = @"
<toast activation="protocol">
  <visual>
    <binding template="ToastGeneric">
      <text hint-maxLines="1">$safeTitle</text>
      <text hint-style="subtitle">$safeBody</text>
    </binding>
  </visual>
  <audio src="ms-winsoundevent:Notification.Reminder"/>
</toast>
"@
        $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
        $xml.LoadXml($xmlText)
        $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
        $toast.Tag = $Tag
        $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId)
        $notifier.Show($toast)
        return $true
    }
    catch {
        return $false
    }
}
