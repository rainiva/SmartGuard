[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RequestPath
)

# This is the reserved parameter adapter for the trusted A4 broker.
# Do not execute dotnet run, build the reviewed checkout, or launch an arbitrary bundle.
# A1 has no trusted launch closure, so the formal entry must remain blocked.
Write-Warning 'Blocked: trusted adversary broker is not implemented; no request was executed.'
exit 40
