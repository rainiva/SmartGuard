# Presentation: 实时刷新日志查看器

function Show-SmartPowerPlanLogViewer {
    param(
        [string]$LogPath,
        [string]$FallbackLogPath = $null
    )
    if (-not (Get-Command Add-Type -ErrorAction SilentlyContinue)) { return }
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    if ($script:LogViewerForm -and -not $script:LogViewerForm.IsDisposed) {
        $script:LogViewerForm.BringToFront()
        $script:LogViewerForm.Focus()
        return
    }

    $form = New-Object System.Windows.Forms.Form
    $form.Text = '智能电源计划 - 日志（实时）'
    $form.Size = New-Object System.Drawing.Size(780, 520)
    $form.StartPosition = 'CenterScreen'
    $form.MinimumSize = New-Object System.Drawing.Size(480, 320)

    $rtb = New-Object System.Windows.Forms.RichTextBox
    $rtb.Dock = 'Fill'
    $rtb.ReadOnly = $true
    $rtb.BackColor = [System.Drawing.Color]::FromArgb(252, 252, 252)
    $rtb.Font = New-Object System.Drawing.Font('Consolas', 10)
    $rtb.WordWrap = $false
    $form.Controls.Add($rtb)

    $status = New-Object System.Windows.Forms.StatusStrip
    $statusLabel = New-Object System.Windows.Forms.ToolStripStatusLabel
    $statusLabel.Spring = $true
    $statusLabel.TextAlign = 'MiddleLeft'
    [void]$status.Items.Add($statusLabel)
    $form.Controls.Add($status)

    $paths = @($LogPath)
    if ($FallbackLogPath) { $paths += $FallbackLogPath }

    $reload = {
        $textParts = [System.Collections.Generic.List[string]]::new()
        foreach ($p in $paths) {
            if (-not $p -or -not (Test-Path -LiteralPath $p)) { continue }
            try {
                $fs = [System.IO.File]::Open($p, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
                try {
                    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8, $true)
                    $content = $sr.ReadToEnd()
                    $sr.Dispose()
                }
                finally { $fs.Dispose() }
                if ($content) { $textParts.Add($content.TrimEnd()) | Out-Null }
            }
            catch {}
        }
        $merged = ($textParts -join [Environment]::NewLine + '--- fallback ---' + [Environment]::NewLine)
        if ($rtb.Text -ne $merged) {
            $rtb.Text = $merged
            $rtb.SelectionStart = $rtb.TextLength
            $rtb.ScrollToCaret()
        }
        $statusLabel.Text = "刷新: $(Get-Date -Format 'HH:mm:ss') | $($LogPath)"
    }

    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = 1000
    $timer.Add_Tick({ & $reload })
    $form.Add_Shown({ & $reload; $timer.Start() })
    $form.Add_FormClosed({
        $timer.Stop()
        $timer.Dispose()
        $script:LogViewerForm = $null
    })

    $script:LogViewerForm = $form
    $form.Show()
}
