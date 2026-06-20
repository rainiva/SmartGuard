param(
    [Parameter(Mandatory)]
    [string]$Path,
    [Parameter(Mandatory)]
    [string]$Pattern
)
$bytes = [System.IO.File]::ReadAllBytes($Path)

$encodings = @(
    [System.Text.Encoding]::UTF8,
    [System.Text.Encoding]::Unicode,
    [System.Text.Encoding]::BigEndianUnicode
)

$found = $false
foreach ($enc in $encodings) {
    $text = $enc.GetString($bytes)
    if ($text -match $Pattern) {
        Write-Host "FOUND in $($enc.EncodingName)"
        $found = $true
        break
    }
}

if (-not $found) {
    Write-Host "NOT FOUND"
}
