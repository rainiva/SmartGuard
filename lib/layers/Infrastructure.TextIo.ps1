# Infrastructure: 文本文件读写（编码探测）

function Read-TextFileAutoEncoding {
    param([string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        return [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
    }
    $nullCount = ($bytes | Where-Object { $_ -eq 0 }).Count
    if ($nullCount -gt $bytes.Length / 4) {
        return [Text.Encoding]::Unicode.GetString($bytes)
    }
    return [Text.Encoding]::UTF8.GetString($bytes)
}

function Write-TextFileUtf8Bom {
    param([string]$Path, [string]$Content)
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [IO.File]::WriteAllText($Path, $Content, $utf8Bom)
}
