Get-Process SmartGuard.Engine -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Output ("MemoryMB={0} Id={1}" -f [math]::Round($_.WorkingSet64 / 1MB, 1), $_.Id)
}
