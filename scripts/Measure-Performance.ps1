[CmdletBinding()]
param(
    [string]$DotNet = "dotnet",
    [string]$Output = "artifacts/performance/current.json",
    [string]$Sizes = "100,500,1000",
    [ValidateRange(1, 10)][int]$Repetitions = 3,
    [switch]$SearchOnly,
    [switch]$SearchHotspots
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    if ($SearchOnly -and $SearchHotspots) { throw 'Choose SearchOnly or SearchHotspots, not both.' }
    $benchmarkArguments = if ($SearchHotspots) { @("--search-hotspots", $Output) }
        else { @("--performance", $Output, $Sizes, "$Repetitions") }
    if ($SearchOnly) { $benchmarkArguments += "search" }
    & $DotNet run --project JavMetaLite.UiSmokeTests --configuration Release --no-restore `
        -p:OutputPath=bin/performance-measure/ -- @benchmarkArguments
    if ($LASTEXITCODE -ne 0) { throw "Performance measurement failed: $LASTEXITCODE" }
}
finally { Pop-Location }
