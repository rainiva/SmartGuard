# Presentation: 实时刷新日志查看器（增量追加，避免整页闪烁）

function Read-LogFileTextFromOffset {
    param(
        [string]$Path,
        [long]$StartOffset = 0
    )
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) {
        return @{ Length = 0; Text = '' }
    }
    try {
        $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $length = $fs.Length
            if ($StartOffset -lt 0 -or $StartOffset -gt $length) { $StartOffset = 0 }
            $fs.Seek($StartOffset, [System.IO.SeekOrigin]::Begin) | Out-Null
            $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8, $true)
            $text = $sr.ReadToEnd()
            $sr.Dispose()
            return @{ Length = $length; Text = $text }
        }
        finally { $fs.Dispose() }
    }
    catch {
        return @{ Length = 0; Text = '' }
    }
}

function Read-SmartGuardLogText {
    param(
        [string]$LogPath,
        [string]$FallbackLogPath = $null
    )
    $snapshot = Read-LogFileTextFromOffset -Path $LogPath -StartOffset 0
    $textParts = [System.Collections.Generic.List[string]]::new()
    if ($snapshot.Text) { $textParts.Add($snapshot.Text.TrimEnd()) | Out-Null }

    if ($FallbackLogPath -and (Test-Path -LiteralPath $FallbackLogPath)) {
        $fallback = Read-LogFileTextFromOffset -Path $FallbackLogPath -StartOffset 0
        if ($fallback.Text) { $textParts.Add($fallback.Text.TrimEnd()) | Out-Null }
    }

    if ($textParts.Count -eq 0) { return $null }
    if ($textParts.Count -eq 1) { return $textParts[0] }
    return ($textParts -join ([Environment]::NewLine + '--- fallback ---' + [Environment]::NewLine))
}

function Initialize-LogViewerRedrawHelper {
    if ('LogViewerRedraw' -as [type]) { return }
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class LogViewerRedraw {
    private const int WM_SETREDRAW = 0x000B;
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    public static void SetRedraw(IntPtr handle, bool enable) {
        if (handle == IntPtr.Zero) { return; }
        SendMessage(handle, WM_SETREDRAW, enable ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
    }
}
"@
}

function Test-LogViewerIsAtTail {
    param($RichTextBox)
    if ($null -eq $RichTextBox -or $RichTextBox.IsDisposed) { return $true }
    if ($RichTextBox.TextLength -le 0) { return $true }
    $point = New-Object System.Drawing.Point(0, [Math]::Max(0, $RichTextBox.ClientSize.Height - 2))
    $index = $RichTextBox.GetCharIndexFromPosition($point)
    if ($index -lt 0) { return $true }
    return (($RichTextBox.TextLength - $index) -le 96)
}

function Set-LogViewerRichText {
    param(
        $RichTextBox,
        [string]$Text,
        [bool]$ScrollToTail
    )
    Initialize-LogViewerRedrawHelper
    $useRedraw = $RichTextBox.IsHandleCreated
    if (-not $useRedraw) { $RichTextBox.CreateControl() }
    $handle = $RichTextBox.Handle
    if ($handle -ne [IntPtr]::Zero) {
        [LogViewerRedraw]::SetRedraw($handle, $false)
    }
    try {
        $RichTextBox.Text = $Text
        if ($ScrollToTail) {
            $RichTextBox.SelectionStart = $RichTextBox.TextLength
            $RichTextBox.ScrollToCaret()
        }
    }
    finally {
        if ($handle -ne [IntPtr]::Zero) {
            [LogViewerRedraw]::SetRedraw($handle, $true)
            $RichTextBox.Refresh()
        }
    }
}

function Add-LogViewerRichText {
    param(
        $RichTextBox,
        [string]$Text,
        [bool]$ScrollToTail
    )
    if ([string]::IsNullOrEmpty($Text)) { return }
    Initialize-LogViewerRedrawHelper
    if (-not $RichTextBox.IsHandleCreated) { $RichTextBox.CreateControl() }
    $handle = $RichTextBox.Handle
    if ($handle -ne [IntPtr]::Zero) {
        [LogViewerRedraw]::SetRedraw($handle, $false)
    }
    try {
        $RichTextBox.AppendText($Text)
        if ($ScrollToTail) {
            $RichTextBox.SelectionStart = $RichTextBox.TextLength
            $RichTextBox.ScrollToCaret()
        }
    }
    finally {
        if ($handle -ne [IntPtr]::Zero) {
            [LogViewerRedraw]::SetRedraw($handle, $true)
            $RichTextBox.Refresh()
        }
    }
}

function Update-SmartGuardLogViewerForm {
    param($Form)
    if (-not $Form -or $Form.IsDisposed) { return }
    $state = $Form.Tag
    if (-not $state) { return }

    $rtb = $state.RichTextBox
    if (-not $state.ContainsKey('FollowTail')) { $state.FollowTail = $true }
    if (-not $state.ContainsKey('PrimaryFileLength')) { $state.PrimaryFileLength = -1 }

    if (-not $state.FollowTail -and (Test-LogViewerIsAtTail -RichTextBox $rtb)) {
        $state.FollowTail = $true
    }

    $snapshot = Read-LogFileTextFromOffset -Path $state.LogPath -StartOffset 0
    if ($snapshot.Length -le 0 -and [string]::IsNullOrEmpty($snapshot.Text)) {
        if ($rtb.TextLength -gt 0) {
            Set-LogViewerRichText -RichTextBox $rtb -Text '' -ScrollToTail:$false
        }
        $state.PrimaryFileLength = 0
        $state.LineCount = 0
        $state.StatusLabel.Text = "暂无日志 | $($state.LogPath)"
        return
    }

    $changed = $false
    $scrollTail = [bool]$state.FollowTail

    if ($state.PrimaryFileLength -lt 0 -or $snapshot.Length -lt $state.PrimaryFileLength) {
        $fullText = Read-SmartGuardLogText -LogPath $state.LogPath -FallbackLogPath $state.FallbackLogPath
        if ([string]::IsNullOrEmpty($fullText)) { $fullText = $snapshot.Text }
        Set-LogViewerRichText -RichTextBox $rtb -Text $fullText -ScrollToTail:$scrollTail
        $state.PrimaryFileLength = $snapshot.Length
        $changed = $true
    }
    elseif ($snapshot.Length -gt $state.PrimaryFileLength) {
        $delta = Read-LogFileTextFromOffset -Path $state.LogPath -StartOffset $state.PrimaryFileLength
        Add-LogViewerRichText -RichTextBox $rtb -Text $delta.Text -ScrollToTail:$scrollTail
        $state.PrimaryFileLength = $snapshot.Length
        $changed = $true
    }

    if ($changed) {
        $state.LineCount = ($rtb.Text -split "`r?`n").Count
    }

    $now = Get-Date
    if ($changed -or -not $state.LastStatusRefresh -or (($now - $state.LastStatusRefresh).TotalSeconds -ge 5)) {
        $state.LastStatusRefresh = $now
        $state.StatusLabel.Text = "刷新: $($now.ToString('HH:mm:ss')) | $($state.LineCount) 行 | $($state.LogPath)"
    }
}

function New-SmartGuardLogViewerForm {
    param(
        [string]$LogPath,
        [string]$FallbackLogPath = $null
    )
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    $form = New-Object System.Windows.Forms.Form
    $form.Text = '智能电源守护 - 日志（实时）'
    $form.Size = New-Object System.Drawing.Size(780, 520)
    $form.StartPosition = 'CenterScreen'
    $form.MinimumSize = New-Object System.Drawing.Size(480, 320)
    $form.ShowInTaskbar = $true

    $status = New-Object System.Windows.Forms.StatusStrip
    $status.Dock = 'Bottom'
    $statusLabel = New-Object System.Windows.Forms.ToolStripStatusLabel
    $statusLabel.Spring = $true
    $statusLabel.TextAlign = 'MiddleLeft'
    [void]$status.Items.Add($statusLabel)
    $form.Controls.Add($status)

    $rtb = New-Object System.Windows.Forms.RichTextBox
    $rtb.Dock = 'Fill'
    $rtb.ReadOnly = $true
    $rtb.BackColor = [System.Drawing.Color]::FromArgb(252, 252, 252)
    $rtb.Font = New-Object System.Drawing.Font('Consolas', 10)
    $rtb.WordWrap = $false
    $rtb.HideSelection = $false
    $form.Controls.Add($rtb)

    $viewerState = @{
        LogPath            = $LogPath
        FallbackLogPath    = $FallbackLogPath
        RichTextBox        = $rtb
        StatusLabel        = $statusLabel
        RefreshTimer       = $null
        PrimaryFileLength  = -1
        LineCount          = 0
        FollowTail         = $true
        LastStatusRefresh  = $null
    }
    $form.Tag = $viewerState

    $disableFollowTail = {
        $formRef = $this.FindForm()
        if ($formRef -and $formRef.Tag) {
            $formRef.Tag.FollowTail = $false
        }
    }
    $rtb.Add_MouseWheel($disableFollowTail)
    $rtb.Add_KeyDown({
        param($sender, $eventArgs)
        if ($eventArgs.KeyCode -in @(
            [System.Windows.Forms.Keys]::PageUp,
            [System.Windows.Forms.Keys]::PageDown,
            [System.Windows.Forms.Keys]::Up,
            [System.Windows.Forms.Keys]::Down,
            [System.Windows.Forms.Keys]::Home
        )) {
            $formRef = $sender.FindForm()
            if ($formRef -and $formRef.Tag) {
                $formRef.Tag.FollowTail = $false
            }
        }
    })

    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = 2000
    $timer.Tag = $form
    $timer.Add_Tick({
        Update-SmartGuardLogViewerForm -Form $this.Tag
    })
    $viewerState.RefreshTimer = $timer

    $form.Add_FormClosed({
        $state = $this.Tag
        if ($state -and $state.RefreshTimer) {
            $state.RefreshTimer.Stop()
            $state.RefreshTimer.Dispose()
            $state.RefreshTimer = $null
        }
    })

    return $form
}

function Initialize-SmartGuardLogViewerSession {
    param($Form)
    if (-not $Form) { return }
    Update-SmartGuardLogViewerForm -Form $Form
    $state = $Form.Tag
    if ($state -and $state.RefreshTimer -and -not $state.RefreshTimer.Enabled) {
        $state.RefreshTimer.Start()
    }
}

function Get-SmartGuardLogViewerScriptPath {
    param([string]$ScriptRoot = $null)
    $root = Get-SmartGuardRoot -ScriptRoot $ScriptRoot
    return Join-Path $root 'lib\Show-LogViewer.ps1'
}

function Start-SmartGuardLogViewerProcess {
    param([string]$ScriptRoot = $null)
    $root = Get-SmartGuardRoot -ScriptRoot $ScriptRoot
    $viewerScript = Get-SmartGuardLogViewerScriptPath -ScriptRoot $root
    if (-not (Test-Path -LiteralPath $viewerScript)) {
        throw ('Missing log viewer script: ' + $viewerScript)
    }
    Start-Process -FilePath 'powershell.exe' -WorkingDirectory $root -WindowStyle Hidden -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Sta', '-WindowStyle', 'Hidden', '-File', $viewerScript
    ) | Out-Null
}

function Start-SmartGuardLogViewerApp {
    param([string]$ScriptRoot = $null)
    $root = Get-SmartGuardRoot -ScriptRoot $ScriptRoot
    $configPath = Join-Path $root 'SmartGuard.config.json'
    $cfg = Read-SmartGuardConfig -ConfigPath $configPath
    $log = if ($cfg -and $cfg.LogFile) { $cfg.LogFile } else { Join-Path $root 'SmartGuard.log' }
    $fallback = Get-SmartGuardFallbackLogPath -ScriptRoot $root

    [System.Windows.Forms.Application]::EnableVisualStyles()
    $form = New-SmartGuardLogViewerForm -LogPath $log -FallbackLogPath $fallback
    $form.Add_FormClosed({
        [System.Windows.Forms.Application]::Exit()
    })
    Initialize-SmartGuardLogViewerSession -Form $form
    $null = $form.Show()
    [System.Windows.Forms.Application]::Run($form)
}

function Show-SmartGuardLogViewer {
    param(
        [string]$LogPath,
        [string]$FallbackLogPath = $null
    )
    Start-SmartGuardLogViewerProcess -ScriptRoot (Get-SmartGuardRoot)
}
