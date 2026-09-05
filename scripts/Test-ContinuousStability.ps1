[CmdletBinding()]
param(
    [string]$DotNet = "dotnet",
    [string]$OutputDirectory = "artifacts/stability50",
    [ValidateRange(10, 2000)][int]$Count = 1000,
    [ValidateRange(1, 10)][int]$Rounds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    & $DotNet run --project JavMetaLite.UiSmokeTests --configuration Release --no-restore `
        -p:OutputPath=bin/stability-measure/ -- --stability $OutputDirectory "$Count" "$Rounds"
    if ($LASTEXITCODE -ne 0) { throw "Continuous stability verification failed: $LASTEXITCODE" }
}
finally { Pop-Location }
