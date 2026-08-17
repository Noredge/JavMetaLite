[CmdletBinding()]
param(
    [string]$DotNet = "dotnet",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projects = @(
    @{ Name = "Core smoke tests"; Path = "JavMetaLite.SmokeTests\JavMetaLite.SmokeTests.csproj" },
    @{ Name = "File transaction regression tests"; Path = "JavMetaLite.RegressionTests\JavMetaLite.RegressionTests.csproj" },
    @{ Name = "WPF UI smoke tests"; Path = "JavMetaLite.UiSmokeTests\JavMetaLite.UiSmokeTests.csproj" }
)

Push-Location $repositoryRoot
try {
    foreach ($project in $projects) {
        Write-Host "`n==> $($project.Name)" -ForegroundColor Cyan
        $arguments = @(
            "run",
            "--project", $project.Path,
            "--configuration", $Configuration
        )
        if ($NoRestore) {
            $arguments += "--no-restore"
        }

        & $DotNet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$($project.Name) failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    Pop-Location
}

Write-Host "`nAUTOMATED TEST GATE PASSED" -ForegroundColor Green
