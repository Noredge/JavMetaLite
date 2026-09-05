[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "ReleasePackage.Common.ps1")
. (Join-Path $PSScriptRoot "ReleaseNotices.Common.ps1")

function Assert-Rejected([scriptblock]$Action, [string]$ExpectedMessage) {
    try { & $Action | Out-Null }
    catch {
        if (-not $_.Exception.Message.Contains($ExpectedMessage)) { throw }
        return
    }
    throw "Expected rejection: $ExpectedMessage"
}

# Pure validation: no build, download, package deletion or filesystem writes.
$fixture = [xml]'<Project><PropertyGroup><Version>1.2.0-preview.43</Version><InformationalVersion>$(Version)</InformationalVersion><FileVersion>1.2.0.0</FileVersion></PropertyGroup></Project>'
$identity = Get-ReleaseIdentity -ProjectXml $fixture
if ($identity.Version -cne "1.2.0-preview.43") { throw "Default version was not read from the project." }
$explicit = Get-ReleaseIdentity -ProjectXml $fixture -RequestedVersion $identity.Version
if ($explicit.Version -cne $identity.Version) { throw "Explicit matching version changed." }
Assert-Rejected { Get-ReleaseIdentity -ProjectXml $fixture -RequestedVersion "1.0.0" } "does not match"
Assert-Rejected { Get-ReleaseIdentity -ProjectXml ([xml]'<Project/>') } "must declare"
$invalid = [xml]$fixture.OuterXml
$invalid.Project.PropertyGroup.Version = "../bad"
Assert-Rejected { Get-ReleaseIdentity -ProjectXml $invalid } "semantic version"
$invalid = [xml]$fixture.OuterXml
$invalid.Project.PropertyGroup.InformationalVersion = "1.0.0"
Assert-Rejected { Get-ReleaseIdentity -ProjectXml $invalid } "InformationalVersion must match"
$invalid = [xml]$fixture.OuterXml
$invalid.Project.PropertyGroup.FileVersion = "1.0.0.0"
Assert-Rejected { Get-ReleaseIdentity -ProjectXml $invalid } "FileVersion must be"
Assert-ReleaseExecutableIdentity -Identity $identity -ProductVersion $identity.Version -FileVersion $identity.FileVersion
Assert-Rejected { Assert-ReleaseExecutableIdentity -Identity $identity -ProductVersion "1.0.0" -FileVersion $identity.FileVersion } "EXE version mismatch"
Assert-Rejected { Assert-ReleaseExecutableIdentity -Identity $identity -ProductVersion $identity.Version -FileVersion "1.0.0.0" } "EXE version mismatch"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$notices = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md'))
Assert-ReleaseNoticeText -Text $notices
Assert-ReleaseNoticeText -Text $notices.Replace("`r`n", "`n").Replace("`n", "`r`n")
Assert-Rejected { Assert-ReleaseNoticeText -Text '' } 'full third-party notice'
Assert-Rejected { Assert-ReleaseNoticeText -Text 'AngleSharp: MIT https://github.com/AngleSharp/AngleSharp' } 'full third-party notice'
foreach ($block in $script:ReleaseNoticeBlocks.Keys) {
    $begin = "<!-- BEGIN NOTICE: $block -->"
    $end = "<!-- END NOTICE: $block -->"
    $beginIndex = $notices.IndexOf($begin, [StringComparison]::Ordinal)
    $endIndex = $notices.IndexOf($end, [StringComparison]::Ordinal) + $end.Length
    $wholeBlock = $notices.Substring($beginIndex, $endIndex - $beginIndex)
    Assert-Rejected { Assert-ReleaseNoticeText -Text $notices.Replace($wholeBlock, '') } 'full third-party notice'
    Assert-Rejected { Assert-ReleaseNoticeText -Text ($notices + "`n" + $wholeBlock) } 'full third-party notice'
    # Keep headings, markers and most content: removing even one copyright line must fail.
    $bodyStart = $wholeBlock.IndexOf('```text', [StringComparison]::Ordinal) + 7
    $alteredBlock = $wholeBlock.Insert($bodyStart, 'truncated')
    Assert-Rejected { Assert-ReleaseNoticeText -Text $notices.Replace($wholeBlock, $alteredBlock) } 'full third-party notice'
    $copyrightAltered = [regex]::Replace($wholeBlock, '(?m)^.*Copyright.*\r?\n', '')
    if ($copyrightAltered -ceq $wholeBlock) { throw "Notice fixture has no copyright: $block" }
    Assert-Rejected { Assert-ReleaseNoticeText -Text $notices.Replace($wholeBlock, $copyrightAltered) } 'text changed or truncated'
}
Assert-Rejected { Assert-ReleaseNoticeText -Text $notices.Replace('| AngleSharp | 1.5.2 |', '| AngleSharp | 1.5.3 |') } 'inventory version'

$projectFixture = [xml]'<Project><ItemGroup><PackageReference Include="AngleSharp" Version="1.5.2"/><PackageReference Include="Microsoft.Web.WebView2" Version="1.0.4078.44"/></ItemGroup></Project>'
Assert-ReleaseProjectNoticeCoverage -Projects @($projectFixture)
Assert-Rejected { Assert-ReleaseProjectNoticeCoverage -Projects @([xml]$projectFixture.OuterXml.Replace('1.5.2', '1.5.3')) } 'Unreviewed package dependency'
Assert-Rejected { Assert-ReleaseProjectNoticeCoverage -Projects @([xml]'<Project><ItemGroup><PackageReference Include="Unreviewed.Library" Version="1.0.0"/></ItemGroup></Project>') } 'Unreviewed package dependency'

$dependencyFixture = @{
    libraries = @{
        'JavMetaLite/1.2.0-preview.43' = @{ type = 'project' }
        'AngleSharp/1.5.2' = @{ type = 'package' }
        'Microsoft.Web.WebView2/1.0.4078.44' = @{ type = 'package' }
        'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.11' = @{ type = 'runtimepack' }
        'runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.11' = @{ type = 'runtimepack' }
    }
}
$dependencyJson = $dependencyFixture | ConvertTo-Json -Depth 4
Assert-ReleaseDependencyNoticeCoverage -DependencyJson $dependencyJson -AppVersion $identity.Version
Assert-Rejected { Assert-ReleaseDependencyNoticeCoverage -DependencyJson $dependencyJson.Replace('10.0.11', '10.0.12') -AppVersion $identity.Version } 'no reviewed third-party notices'
Assert-Rejected { Assert-ReleaseDependencyNoticeCoverage -DependencyJson $dependencyJson.Replace('AngleSharp/1.5.2', 'AngleSharp/1.5.3') -AppVersion $identity.Version } 'no reviewed third-party notices'
Assert-Rejected { Assert-ReleaseDependencyNoticeCoverage -DependencyJson $dependencyJson -AppVersion '1.2.0-preview.99' } 'does not match app version'
$dependencyFixture.libraries['Unreviewed.Library/1.0.0'] = @{ type = 'package' }
Assert-Rejected { Assert-ReleaseDependencyNoticeCoverage -DependencyJson ($dependencyFixture | ConvertTo-Json -Depth 4) -AppVersion $identity.Version } 'no reviewed third-party notices'

$entryPoint = Join-Path $PSScriptRoot "New-ReleasePackage.ps1"
$actual = & $entryPoint -ValidateOnly
$matched = & $entryPoint -ValidateOnly -Version $actual.Version
if ($matched.Version -cne $actual.Version) { throw "Entry point did not use the project version." }
Assert-Rejected { & $entryPoint -ValidateOnly -Version "0.0.0-mismatch" } "does not match"
Write-Host "PACKAGE VALIDATION PASS: project/default/explicit versions, EXE identity, complete license/notice text, dependency coverage and read-only entry point."
