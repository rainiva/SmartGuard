#Requires -Version 5.1

function Get-BumpedPatchVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version,

        [int]$Increment = 1
    )

    if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
        throw "Invalid installer version '$Version'. Expected major.minor.patch (e.g. 1.0.0)."
    }

    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    $patch = [int]$Matches[3] + $Increment
    return "$major.$minor.$patch"
}

function Update-InstallerVersionFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VersionFile,

        [switch]$SkipBump
    )

    if (-not (Test-Path -LiteralPath $VersionFile)) {
        throw "Missing installer version file: $VersionFile"
    }

    $current = (Get-Content -LiteralPath $VersionFile -Raw).Trim()
    if ($SkipBump) {
        return $current
    }

    $next = Get-BumpedPatchVersion -Version $current
    Set-Content -LiteralPath $VersionFile -Value $next -Encoding ASCII -NoNewline
    return $next
}
