[CmdletBinding()]
param(
    [string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pluginRoot = Join-Path $repositoryRoot 'plugins/tpsreader'
$skillRoot = Join-Path $pluginRoot 'skills/read-tps-files'

function Assert-Condition {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Read-JsonObject {
    param([string] $Path)

    Assert-Condition (Test-Path -LiteralPath $Path -PathType Leaf) "Missing JSON file: $Path"
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

$package = Read-JsonObject (Join-Path $pluginRoot 'package.json')
$codexManifest = Read-JsonObject (Join-Path $pluginRoot '.codex-plugin/plugin.json')
$claudeManifest = Read-JsonObject (Join-Path $pluginRoot '.claude-plugin/plugin.json')
$codexMarketplace = Read-JsonObject (Join-Path $repositoryRoot '.agents/plugins/marketplace.json')
$claudeMarketplace = Read-JsonObject (Join-Path $repositoryRoot '.claude-plugin/marketplace.json')

Assert-Condition ($package.name -eq '@carlosgtrz/tpsreader-agent-skill') 'Unexpected npm package name.'
Assert-Condition ($codexManifest.name -eq 'tpsreader') 'Unexpected Codex plugin name.'
Assert-Condition ($claudeManifest.name -eq 'tpsreader') 'Unexpected Claude plugin name.'
Assert-Condition ($package.license -eq 'Apache-2.0') 'The npm license must be Apache-2.0.'
Assert-Condition ($package.publishConfig.access -eq 'public') 'The npm package must publish with public access.'
Assert-Condition ($package.repository.url -eq 'git+https://github.com/CarlosGtrz/TpsReader.git') 'Unexpected npm repository URL.'
Assert-Condition ($package.homepage -eq 'https://github.com/CarlosGtrz/TpsReader#agent-skill') 'Unexpected npm homepage.'
$requiredKeywords = @('pi-package', 'agent-skill', 'clarion', 'topspeed', 'tps')
foreach ($keyword in $requiredKeywords) {
    Assert-Condition ($package.keywords -contains $keyword) "The npm package is missing required keyword '$keyword'."
}
Assert-Condition ($package.version -eq $codexManifest.version) 'npm and Codex versions differ.'
Assert-Condition ($package.version -eq $claudeManifest.version) 'npm and Claude versions differ.'
Assert-Condition ([string]::IsNullOrEmpty($ExpectedVersion) -or $package.version -eq $ExpectedVersion) "Package version '$($package.version)' does not match expected version '$ExpectedVersion'."
Assert-Condition ($codexManifest.skills -eq './skills/') 'Codex skills path must be ./skills/.'
Assert-Condition ($claudeManifest.skills -eq './skills/') 'Claude skills path must be ./skills/.'
Assert-Condition ($package.pi.skills.Count -eq 1 -and $package.pi.skills[0] -eq './skills') 'Pi skills path must contain only ./skills.'
Assert-Condition ($null -eq $package.dependencies) 'The npm package must not declare dependencies.'
Assert-Condition ($null -eq $package.devDependencies) 'The npm package must not declare devDependencies.'
Assert-Condition ($null -eq $package.scripts) 'The npm package must not declare lifecycle or other scripts.'

$codexEntry = @($codexMarketplace.plugins | Where-Object name -eq 'tpsreader')
Assert-Condition ($codexMarketplace.name -eq 'tpsreader') 'Unexpected Codex marketplace name.'
Assert-Condition ($codexEntry.Count -eq 1) 'Codex marketplace must contain exactly one tpsreader entry.'
Assert-Condition ($codexEntry[0].source.source -eq 'local') 'Codex marketplace source must be local.'
Assert-Condition ($codexEntry[0].source.path -eq './plugins/tpsreader') 'Codex marketplace path must be ./plugins/tpsreader.'
Assert-Condition ($codexEntry[0].policy.installation -eq 'AVAILABLE') 'Codex installation policy must be AVAILABLE.'
Assert-Condition ($codexEntry[0].policy.authentication -eq 'ON_INSTALL') 'Codex authentication policy must be ON_INSTALL.'
Assert-Condition ($codexEntry[0].category -eq 'Productivity') 'Codex marketplace category must be Productivity.'
Assert-Condition ($codexMarketplace.interface.displayName -eq 'TpsReader') 'Codex marketplace display name must be TpsReader.'
Assert-Condition ($codexManifest.interface.displayName -eq 'TpsReader') 'Codex plugin display name must be TpsReader.'
Assert-Condition ($codexManifest.interface.developerName -eq 'CarlosGtrz') 'Codex developer must be CarlosGtrz.'
Assert-Condition ($codexManifest.interface.category -eq 'Productivity') 'Codex plugin category must be Productivity.'
Assert-Condition (@($codexManifest.interface.capabilities).Count -eq 2) 'Codex capabilities must contain exactly Read and Export.'
Assert-Condition ($codexManifest.interface.capabilities -contains 'Read' -and $codexManifest.interface.capabilities -contains 'Export') 'Codex capabilities must be Read and Export.'
Assert-Condition ($codexManifest.homepage -eq 'https://github.com/CarlosGtrz/TpsReader') 'Unexpected Codex homepage.'
Assert-Condition ($codexManifest.repository -eq 'https://github.com/CarlosGtrz/TpsReader') 'Unexpected Codex repository.'
Assert-Condition ($codexManifest.license -eq 'Apache-2.0') 'Codex license must be Apache-2.0.'

$claudeEntry = @($claudeMarketplace.plugins | Where-Object name -eq 'tpsreader')
Assert-Condition ($claudeMarketplace.name -eq 'tpsreader') 'Unexpected Claude marketplace name.'
Assert-Condition ($claudeMarketplace.owner.name -eq 'CarlosGtrz') 'Claude marketplace owner must be CarlosGtrz.'
Assert-Condition ($claudeEntry.Count -eq 1) 'Claude marketplace must contain exactly one tpsreader entry.'
Assert-Condition ($claudeEntry[0].source -eq './plugins/tpsreader') 'Claude marketplace path must be ./plugins/tpsreader.'

$skillPath = Join-Path $skillRoot 'SKILL.md'
Assert-Condition (Test-Path -LiteralPath $skillPath -PathType Leaf) "Missing skill: $skillPath"
$skillText = Get-Content -Raw -LiteralPath $skillPath
$frontmatterMatch = [regex]::Match($skillText, '\A---\r?\n(?<frontmatter>.*?)\r?\n---\r?\n', [Text.RegularExpressions.RegexOptions]::Singleline)
Assert-Condition $frontmatterMatch.Success 'SKILL.md must start with closed YAML frontmatter.'
$frontmatterLines = $frontmatterMatch.Groups['frontmatter'].Value -split '\r?\n'
$frontmatterKeys = @(
    $frontmatterLines |
        Where-Object { $_ -match '^\s*([A-Za-z0-9_-]+)\s*:' } |
        ForEach-Object { $Matches[1] }
)
Assert-Condition ($frontmatterKeys.Count -eq 2) 'SKILL.md frontmatter must contain exactly name and description.'
Assert-Condition ($frontmatterKeys[0] -eq 'name' -and $frontmatterKeys[1] -eq 'description') 'SKILL.md frontmatter fields must be name followed by description.'
$skillNameMatch = [regex]::Match($frontmatterMatch.Groups['frontmatter'].Value, '(?m)^name:\s*(?<name>[a-z0-9-]+)\s*$')
$descriptionMatch = [regex]::Match($frontmatterMatch.Groups['frontmatter'].Value, '(?m)^description:\s*(?<description>.+?)\s*$')
Assert-Condition ($skillNameMatch.Success -and $skillNameMatch.Groups['name'].Value -eq 'read-tps-files') 'Skill name must be read-tps-files.'
Assert-Condition ($skillNameMatch.Groups['name'].Value -eq (Split-Path $skillRoot -Leaf)) 'Skill name must match its folder.'
Assert-Condition ($descriptionMatch.Success -and $descriptionMatch.Groups['description'].Value.Length -le 1024) 'Skill description must be present and no longer than 1024 characters.'
Assert-Condition (-not $descriptionMatch.Groups['description'].Value.Contains('Codex needs')) 'Skill description must remain agent-neutral.'

$requiredSkillFiles = @(
    (Join-Path $skillRoot 'agents/openai.yaml'),
    (Join-Path $skillRoot 'references/cli-resolution.md')
)
foreach ($path in $requiredSkillFiles) {
    Assert-Condition (Test-Path -LiteralPath $path -PathType Leaf) "Missing required skill resource: $path"
}

$rootLicense = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'Apache-2.0.txt')
$packageLicense = Get-Content -Raw -LiteralPath (Join-Path $pluginRoot 'LICENSE')
Assert-Condition ($rootLicense -eq $packageLicense) 'The package LICENSE must exactly match Apache-2.0.txt.'

$todoMatches = Get-ChildItem -LiteralPath $pluginRoot -Recurse -File |
    Select-String -SimpleMatch '[TODO:' -List
Assert-Condition ($null -eq $todoMatches) 'The package contains a [TODO: ...] placeholder.'

$blockedFiles = @(
    Get-ChildItem -LiteralPath $pluginRoot -Recurse -File |
        Where-Object {
            $_.Extension -in @('.exe', '.dll', '.so', '.dylib', '.tps', '.blob', '.nupkg', '.snupkg')
        }
)
Assert-Condition ($blockedFiles.Count -eq 0) "The package contains blocked binary, fixture, or build files: $($blockedFiles.FullName -join ', ')"

Push-Location $pluginRoot
try {
    $packJson = (& npm pack --dry-run --json 2>&1) -join [Environment]::NewLine
    Assert-Condition ($LASTEXITCODE -eq 0) "npm pack --dry-run failed: $packJson"
    $packResult = $packJson | ConvertFrom-Json
}
finally {
    Pop-Location
}

$actualFiles = @($packResult[0].files.path | Sort-Object)
$expectedFiles = @(
    '.claude-plugin/plugin.json',
    '.codex-plugin/plugin.json',
    'LICENSE',
    'README.md',
    'package.json',
    'skills/read-tps-files/SKILL.md',
    'skills/read-tps-files/agents/openai.yaml',
    'skills/read-tps-files/references/cli-resolution.md'
) | Sort-Object

Assert-Condition ($actualFiles.Count -eq $expectedFiles.Count) "npm package contains an unexpected number of files: $($actualFiles -join ', ')"
for ($index = 0; $index -lt $expectedFiles.Count; $index++) {
    Assert-Condition ($actualFiles[$index] -eq $expectedFiles[$index]) "npm package files differ. Expected '$($expectedFiles[$index])', found '$($actualFiles[$index])'."
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$packStagingRoot = Join-Path $temporaryRoot "tpsreader-agent-package-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $packStagingRoot | Out-Null

try {
    Push-Location $pluginRoot
    try {
        $packedJson = (& npm pack --json --pack-destination $packStagingRoot 2>&1) -join [Environment]::NewLine
        Assert-Condition ($LASTEXITCODE -eq 0) "npm pack failed: $packedJson"
        $packedResult = $packedJson | ConvertFrom-Json
    }
    finally {
        Pop-Location
    }

    $tarballPath = Join-Path $packStagingRoot $packedResult[0].filename
    Assert-Condition (Test-Path -LiteralPath $tarballPath -PathType Leaf) "npm pack did not create the expected tarball: $tarballPath"

    & tar -xf $tarballPath -C $packStagingRoot
    Assert-Condition ($LASTEXITCODE -eq 0) 'Could not unpack the npm tarball.'

    $unpackedRoot = Join-Path $packStagingRoot 'package'
    Assert-Condition (Test-Path -LiteralPath $unpackedRoot -PathType Container) 'The npm tarball does not contain the expected package directory.'

    $unpackedFiles = @(
        Get-ChildItem -LiteralPath $unpackedRoot -Recurse -File -Force |
            ForEach-Object {
                [IO.Path]::GetRelativePath($unpackedRoot, $_.FullName).Replace('\', '/')
            } |
            Sort-Object
    )
    Assert-Condition ($unpackedFiles.Count -eq $expectedFiles.Count) "Unpacked npm package contains an unexpected number of files: $($unpackedFiles -join ', ')"
    for ($index = 0; $index -lt $expectedFiles.Count; $index++) {
        Assert-Condition ($unpackedFiles[$index] -eq $expectedFiles[$index]) "Unpacked npm package files differ. Expected '$($expectedFiles[$index])', found '$($unpackedFiles[$index])'."
    }

    $unpackedPackage = Read-JsonObject (Join-Path $unpackedRoot 'package.json')
    Assert-Condition ($unpackedPackage.version -eq $package.version) 'The packed npm version differs from the source package version.'
    $unpackedLicense = Get-Content -Raw -LiteralPath (Join-Path $unpackedRoot 'LICENSE')
    Assert-Condition ($unpackedLicense -eq $rootLicense) 'The packed LICENSE differs from Apache-2.0.txt.'

    $credentialPatterns = @(
        '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
        '\bgithub_pat_[A-Za-z0-9_]{20,}\b',
        '\bgh[pousr]_[A-Za-z0-9]{20,}\b',
        '\bnpm_[A-Za-z0-9]{20,}\b',
        '\bAKIA[0-9A-Z]{16}\b'
    )
    foreach ($file in Get-ChildItem -LiteralPath $unpackedRoot -Recurse -File -Force) {
        $content = Get-Content -Raw -LiteralPath $file.FullName
        foreach ($pattern in $credentialPatterns) {
            Assert-Condition (-not [regex]::IsMatch($content, $pattern)) "Packed file appears to contain a credential: $($file.FullName)"
        }
    }
}
finally {
    $resolvedStagingRoot = [IO.Path]::GetFullPath($packStagingRoot)
    $expectedPrefix = $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $isSafeTemporaryPath =
        $resolvedStagingRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path $resolvedStagingRoot -Leaf).StartsWith('tpsreader-agent-package-', [StringComparison]::Ordinal)
    Assert-Condition $isSafeTemporaryPath "Refusing to remove unexpected staging directory: $resolvedStagingRoot"
    Remove-Item -LiteralPath $resolvedStagingRoot -Recurse -Force
}

Write-Host "Agent package validation passed for version $($package.version)."
