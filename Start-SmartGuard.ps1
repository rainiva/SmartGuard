# Double-click entry -> elevated launcher
$cmd = Join-Path $PSScriptRoot 'Start-Core.cmd'
Start-Process -FilePath $cmd -WorkingDirectory $PSScriptRoot