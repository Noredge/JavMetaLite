Set-StrictMode -Version Latest

function Get-ReleaseIdentity {
    param(
        [Parameter(Mandatory)][xml]$ProjectXml,
        [string]$RequestedVersion
    )

    $versionNode = $ProjectXml.SelectSingleNode("/Project/PropertyGroup/Version")
    $infoNode = $ProjectXml.SelectSingleNode("/Project/PropertyGroup/InformationalVersion")
    $fileNode = $ProjectXml.SelectSingleNode("/Project/PropertyGroup/FileVersion")
    if ($null -eq $versionNode -or $null -eq $infoNode -or $null -eq $fileNode) {
        throw "The app project must declare Version, InformationalVersion and FileVersion."
    }
    $projectVersion = $versionNode.InnerText.Trim()
    if ($projectVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
        throw "The app Version must be a semantic version, for example 1.2.0-preview.43."
    }
    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion) -and $RequestedVersion -cne $projectVersion) {
        throw "Requested package version '$RequestedVersion' does not match app version '$projectVersion'."
    }
    $informationalVersion = $infoNode.InnerText.Trim().Replace('$(Version)', $projectVersion)
    if ($informationalVersion -cne $projectVersion) {
        throw "InformationalVersion must match the app Version."
    }
    $fileVersion = $fileNode.InnerText.Trim()
    $expectedFileVersion = ($projectVersion -split '-', 2)[0] + ".0"
    if ($fileVersion -cne $expectedFileVersion) {
        throw "FileVersion must be $expectedFileVersion for app version $projectVersion."
    }
    [pscustomobject]@{ Version = $projectVersion; FileVersion = $fileVersion }
}

function Assert-ReleaseExecutableIdentity {
    param(
        [Parameter(Mandatory)]$Identity,
        [Parameter(Mandatory)][string]$ProductVersion,
        [Parameter(Mandatory)][string]$FileVersion
    )
    if ($ProductVersion -cne $Identity.Version -or $FileVersion -cne $Identity.FileVersion) {
        throw "Published EXE version mismatch: ProductVersion=$ProductVersion; FileVersion=$FileVersion."
    }
}
