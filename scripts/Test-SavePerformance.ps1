[CmdletBinding()]
param(
    [string]$DotNet = "dotnet",
    [string]$OutputDirectory = "artifacts/save-performance",
    [ValidateRange(1, 100)][int]$Count = 12,
    [ValidateRange(1, 5)][int]$Rounds = 3,
    [ValidateRange(0, 500)][int]$DelayMilliseconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    & $DotNet run --project JavMetaLite.RegressionTests --configuration Release --no-restore `
        -p:OutputPath=bin/save-performance/ -- --save-performance $OutputDirectory $Count $Rounds $DelayMilliseconds
    if ($LASTEXITCODE -ne 0) { throw "Save performance validation failed: $LASTEXITCODE" }
}
finally { Pop-Location }
