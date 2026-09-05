Set-StrictMode -Version Latest

# Reviewed upstream texts for the dependencies shipped by this portable build.
# Hashes cover UTF-8 text after CRLF-to-LF normalization, without trailing newlines.
# Updating a dependency requires reviewing its license/notices, not bypassing this check.
$script:ReleaseNoticeComponents = @(
    @{ Id = 'AngleSharp'; Version = '1.5.2'; Library = 'AngleSharp' },
    @{ Id = 'Microsoft.Web.WebView2'; Version = '1.0.4078.44'; Library = 'Microsoft.Web.WebView2' },
    @{ Id = 'Microsoft.NETCore.App.Runtime.win-x64'; Version = '10.0.11'; Library = 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64' },
    @{ Id = 'Microsoft.WindowsDesktop.App.Runtime.win-x64'; Version = '10.0.11'; Library = 'runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64' }
)
$script:ReleaseNoticeBlocks = @{
    'anglesharp-license' = '4D9E121D12AE55BB017C5AA16B6A10CB74B0023F9464210582982033940F3801'
    'webview2-license' = '2B39E78C5EA2AC66E1351236372B7D676CECA22B432FD9275F10B77F64ABC3EF'
    'webview2-notices' = 'EE9973A1C8AC4F0A7946197EBC458ED5FE3218F8B16707994599CA33BE770DE7'
    'dotnet-license' = 'AE48DF11A335DC1A615F4F938B69CBA73BCF4485C4F97AF49B38EFB0F216353B'
    'dotnet-notices' = 'B7D4569659507A00EFEEA90F15A0A95BB2F8B6CE33737D86090B8E22C74A8C98'
}

function Assert-ReleaseNoticeText {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    $normalized = $Text.Replace("`r`n", "`n")
    foreach ($block in $script:ReleaseNoticeBlocks.GetEnumerator()) {
        $id = [regex]::Escape($block.Key)
        $pattern = '(?s)<!-- BEGIN NOTICE: ' + $id + ' -->\n```text\n(?<body>.*?)\n```\n<!-- END NOTICE: ' + $id + ' -->'
        $matches = [regex]::Matches($normalized, $pattern)
        if ($matches.Count -ne 1) {
            throw "Missing or duplicate full third-party notice: $($block.Key)."
        }
        $body = $matches[0].Groups['body'].Value.TrimEnd([char]10)
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($body))).Replace('-', '')
        }
        finally { $sha.Dispose() }
        if ($hash -cne $block.Value) {
            throw "Third-party notice text changed or truncated: $($block.Key). Review the upstream text before updating its fingerprint."
        }
    }
    foreach ($component in $script:ReleaseNoticeComponents) {
        # The visible inventory must agree with the reviewed versions, too.
        $name = if ($component.Id -eq 'Microsoft.Web.WebView2') { 'Microsoft.Web.WebView2 SDK assemblies and native loader' } else { $component.Id }
        if (-not $normalized.Contains("| $name | $($component.Version) |")) {
            throw "Third-party inventory version missing or mismatched: $($component.Id)."
        }
    }
}

function Assert-ReleaseProjectNoticeCoverage {
    param([Parameter(Mandatory)][xml[]]$Projects)

    foreach ($project in $Projects) {
        foreach ($reference in $project.SelectNodes('/Project/ItemGroup/PackageReference')) {
            $name = $reference.GetAttribute('Include')
            $version = $reference.GetAttribute('Version')
            $component = @($script:ReleaseNoticeComponents | Where-Object { $_.Id -ceq $name })
            if ($component.Count -ne 1 -or $component[0].Version -cne $version) {
                throw "Unreviewed package dependency or version in third-party notices: $name $version."
            }
        }
    }
}

function Assert-ReleaseDependencyNoticeCoverage {
    param(
        [Parameter(Mandatory)][string]$DependencyJson,
        [Parameter(Mandatory)][string]$AppVersion
    )

    $dependencies = $DependencyJson | ConvertFrom-Json
    $libraryNames = @($dependencies.libraries.PSObject.Properties.Name)
    if ($libraryNames -cnotcontains "JavMetaLite/$AppVersion") {
        throw "Build dependency manifest does not match app version $AppVersion."
    }
    foreach ($component in $script:ReleaseNoticeComponents) {
        $expected = "$($component.Library)/$($component.Version)"
        if ($libraryNames -cnotcontains $expected) {
            throw "Build dependency has no reviewed third-party notices: $expected."
        }
    }
    foreach ($library in $dependencies.libraries.PSObject.Properties) {
        if ($library.Value.type -in @('package', 'runtimepack')) {
            $known = @($script:ReleaseNoticeComponents | Where-Object { "$($_.Library)/$($_.Version)" -ceq $library.Name })
            if ($known.Count -ne 1) {
                throw "Build dependency has no reviewed third-party notices: $($library.Name)."
            }
        }
    }
}
