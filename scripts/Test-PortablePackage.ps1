[CmdletBinding()]
param(
    [string]$ArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "ReleasePackage.Common.ps1")
. (Join-Path $PSScriptRoot "ReleaseNotices.Common.ps1")

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectXml = [xml][IO.File]::ReadAllText((Join-Path $repositoryRoot "JavMetaLite.App/JavMetaLite.App.csproj"))
$identity = Get-ReleaseIdentity -ProjectXml $projectXml
$packageName = "JavMetaLite-v$($identity.Version)-win-x64-portable"
if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $ArchivePath = Join-Path $repositoryRoot "release/$packageName.zip"
}
$ArchivePath = [IO.Path]::GetFullPath($ArchivePath)
$archiveHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
$checksumLine = "$archiveHash  $([IO.Path]::GetFileName($ArchivePath))"
if ([IO.File]::ReadAllText("$ArchivePath.sha256").Trim() -cne $checksumLine) {
    throw "Portable archive checksum mismatch."
}
$aggregateChecksumPath = Join-Path (Split-Path -Parent $ArchivePath) "SHA256SUMS.txt"
if (-not [IO.File]::ReadAllLines($aggregateChecksumPath).Contains($checksumLine)) {
    throw "Portable archive checksum is missing from SHA256SUMS.txt."
}

# Inspect the ZIP inventory before extraction; reject extra entries and paths.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$expectedNames = @("JavMetaLite.exe", "LICENSE.txt", "README.txt", "THIRD_PARTY_NOTICES.txt")
$expectedEntries = @($expectedNames | ForEach-Object { "$packageName/$_" })
$archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
try {
    $actualEntries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    if ($actualEntries.Count -ne $expectedEntries.Count -or
        @(Compare-Object ($actualEntries | Sort-Object) ($expectedEntries | Sort-Object) -CaseSensitive).Count -ne 0) {
        throw "Portable ZIP must contain exactly the four expected files in $packageName."
    }
}
finally { $archive.Dispose() }

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$extractionPath = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ("JavMetaLite-package-check-" + [Guid]::NewGuid().ToString("N"))))
if (-not $extractionPath.StartsWith($temporaryRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing a package extraction path outside the temporary directory."
}
New-Item -ItemType Directory -Path $extractionPath | Out-Null
try {
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $extractionPath
    $packagePath = Join-Path $extractionPath $packageName
    $executable = Join-Path $packagePath "JavMetaLite.exe"
    $executableVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
    Assert-ReleaseExecutableIdentity -Identity $identity -ProductVersion $executableVersion.ProductVersion -FileVersion $executableVersion.FileVersion

    $readme = [IO.File]::ReadAllText((Join-Path $packagePath "README.txt"))
    $expectedReadme = [IO.File]::ReadAllText((Join-Path $repositoryRoot "packaging/README.txt")).Replace("{VERSION}", $identity.Version)
    if ($readme -cne $expectedReadme) { throw "Portable README does not match the versioned template." }
    if ([IO.File]::ReadAllText((Join-Path $packagePath "LICENSE.txt")) -cne [IO.File]::ReadAllText((Join-Path $repositoryRoot "LICENSE"))) {
        throw "Portable project license does not match the source."
    }
    $notices = [IO.File]::ReadAllText((Join-Path $packagePath "THIRD_PARTY_NOTICES.txt"))
    Assert-ReleaseNoticeText -Text $notices
    if ($notices -cne [IO.File]::ReadAllText((Join-Path $repositoryRoot "THIRD_PARTY_NOTICES.md"))) {
        throw "Portable third-party notices do not match the reviewed source."
    }

    Write-Host "PORTABLE PACKAGE PASS: $($identity.Version), four-file ZIP, EXE identity, README, licenses and SHA-256 $archiveHash."
}
finally {
    # Only this run's validated, uniquely named extraction directory is removed.
    Remove-Item -LiteralPath $extractionPath -Recurse -Force
}
