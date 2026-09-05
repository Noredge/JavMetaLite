[CmdletBinding()]
param(
    [string]$Version,
    [string]$DotNet = "dotnet",
    [switch]$NoRestore,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
. (Join-Path $PSScriptRoot "ReleasePackage.Common.ps1")
. (Join-Path $PSScriptRoot "ReleaseNotices.Common.ps1")
$projectPath = Join-Path $repositoryRoot "JavMetaLite.App\JavMetaLite.App.csproj"
$projectXml = [xml][System.IO.File]::ReadAllText($projectPath)
$identity = Get-ReleaseIdentity -ProjectXml $projectXml -RequestedVersion $Version
$Version = $identity.Version
$noticeText = [System.IO.File]::ReadAllText((Join-Path $repositoryRoot "THIRD_PARTY_NOTICES.md"))
Assert-ReleaseNoticeText -Text $noticeText
$coreProjectXml = [xml][System.IO.File]::ReadAllText((Join-Path $repositoryRoot "JavMetaLite.Core\JavMetaLite.Core.csproj"))
Assert-ReleaseProjectNoticeCoverage -Projects @($projectXml, $coreProjectXml)
if ($ValidateOnly) {
    return $identity
}

$releaseRoot = Join-Path $repositoryRoot "release"
$publishDirectory = Join-Path $releaseRoot ".publish-v$Version"
$packageName = "JavMetaLite-v$Version-win-x64-portable"
$packageDirectory = Join-Path $releaseRoot $packageName
$archivePath = Join-Path $releaseRoot "$packageName.zip"
$checksumPath = Join-Path $releaseRoot "SHA256SUMS.txt"
$versionChecksumPath = $archivePath + ".sha256"

foreach ($path in @($publishDirectory, $packageDirectory, $archivePath, $checksumPath, $versionChecksumPath)) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($releaseRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to change a path outside the release directory: $fullPath"
    }
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
foreach ($path in @($publishDirectory)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
$publishArguments = @(
    "publish",
    $projectPath,
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
$executableVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
Assert-ReleaseExecutableIdentity -Identity $identity -ProductVersion $executableVersion.ProductVersion -FileVersion $executableVersion.FileVersion

# Publishing a single-file EXE consumes this manifest from the build output.
# Check it after publishing so an SDK/runtime upgrade cannot silently ship old notices.
$framework = $projectXml.SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText
$assemblyName = $projectXml.SelectSingleNode('/Project/PropertyGroup/AssemblyName').InnerText
$dependencyPath = Join-Path (Split-Path -Parent $projectPath) "bin\Release\$framework\win-x64\$assemblyName.deps.json"
Assert-ReleaseDependencyNoticeCoverage -DependencyJson ([System.IO.File]::ReadAllText($dependencyPath)) -AppVersion $Version

# Do not replace a previous package until publishing and identity validation succeed.
if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
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
[System.IO.File]::WriteAllText(
    (Join-Path $packageDirectory "THIRD_PARTY_NOTICES.txt"),
    $noticeText,
    [System.Text.UTF8Encoding]::new($false))

Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archivePath -CompressionLevel Optimal
$archiveHash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
$checksumLine = "$($archiveHash.Hash)  $([System.IO.Path]::GetFileName($archivePath))$([Environment]::NewLine)"
[System.IO.File]::WriteAllText($versionChecksumPath, $checksumLine, [System.Text.UTF8Encoding]::new($false))
$previousChecksums = @()
if (Test-Path -LiteralPath $checksumPath) {
    $archiveFileName = [System.IO.Path]::GetFileName($archivePath)
    $previousChecksums = @([System.IO.File]::ReadAllLines($checksumPath) | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and -not $_.EndsWith("  $archiveFileName", [System.StringComparison]::OrdinalIgnoreCase)
    })
}
[System.IO.File]::WriteAllText($checksumPath,
    (($previousChecksums + $checksumLine.TrimEnd()) -join [Environment]::NewLine) + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

$packageFiles = Get-ChildItem -LiteralPath $packageDirectory -File | Sort-Object Name
if ($packageFiles.Count -ne 4) {
    throw "Expected exactly four files in the portable directory, found $($packageFiles.Count)."
}

Remove-Item -LiteralPath $publishDirectory -Recurse -Force

Write-Host "`nRelease package created:" -ForegroundColor Green
Write-Host "  $archivePath"
Write-Host "  $checksumPath"
Write-Host "  $versionChecksumPath"
Write-Host "  SHA-256: $($archiveHash.Hash)"
