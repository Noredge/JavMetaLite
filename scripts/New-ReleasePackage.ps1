[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$DotNet = "dotnet",
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use the form 1.0.0."
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$releaseRoot = Join-Path $repositoryRoot "release"
$publishDirectory = Join-Path $releaseRoot ".publish-v$Version"
$packageName = "JavMetaLite-v$Version-win-x64-portable"
$packageDirectory = Join-Path $releaseRoot $packageName
$archivePath = Join-Path $releaseRoot "$packageName.zip"
$checksumPath = Join-Path $releaseRoot "SHA256SUMS.txt"

foreach ($path in @($publishDirectory, $packageDirectory, $archivePath, $checksumPath)) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($releaseRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to change a path outside the release directory: $fullPath"
    }
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
foreach ($path in @($publishDirectory, $packageDirectory)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
foreach ($path in @($archivePath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$publishArguments = @(
    "publish",
    (Join-Path $repositoryRoot "JavMetaLite.App\JavMetaLite.App.csproj"),
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--output", $publishDirectory,
    "-p:PublishSingleFile=true"
)
if ($NoRestore) {
    $publishArguments += "--no-restore"
}

& $DotNet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $publishDirectory "JavMetaLite.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published executable was not found: $executable"
}

New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
Copy-Item -LiteralPath $executable -Destination (Join-Path $packageDirectory "JavMetaLite.exe")
$readmeTemplate = [System.IO.File]::ReadAllText((Join-Path $repositoryRoot "packaging\README.txt"))
$portableReadme = $readmeTemplate.Replace("{VERSION}", $Version)
[System.IO.File]::WriteAllText(
    (Join-Path $packageDirectory "README.txt"),
    $portableReadme,
    [System.Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination (Join-Path $packageDirectory "LICENSE.txt")
Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD_PARTY_NOTICES.md") -Destination (Join-Path $packageDirectory "THIRD_PARTY_NOTICES.txt")

Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archivePath -CompressionLevel Optimal
$archiveHash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
$checksumLine = "$($archiveHash.Hash)  $([System.IO.Path]::GetFileName($archivePath))$([Environment]::NewLine)"
[System.IO.File]::WriteAllText($checksumPath, $checksumLine, [System.Text.UTF8Encoding]::new($false))

$packageFiles = Get-ChildItem -LiteralPath $packageDirectory -File | Sort-Object Name
if ($packageFiles.Count -ne 4) {
    throw "Expected exactly four files in the portable directory, found $($packageFiles.Count)."
}

Remove-Item -LiteralPath $publishDirectory -Recurse -Force

Write-Host "`nRelease package created:" -ForegroundColor Green
Write-Host "  $archivePath"
Write-Host "  $checksumPath"
Write-Host "  SHA-256: $($archiveHash.Hash)"
