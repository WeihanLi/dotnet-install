[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter()]
    [string]$InstallDir,

    [Parameter()]
    [string]$ActionRepository,

    [Parameter()]
    [string]$ActionRef,

    [Parameter(Mandatory = $true)]
    [string]$RunnerOs,

    [Parameter(Mandatory = $true)]
    [string]$RunnerArch,

    [Parameter(Mandatory = $true)]
    [string]$TempDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$defaultActionRepository = 'WeihanLi/dotnet-install'

function Write-ActionOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        throw 'GITHUB_OUTPUT is not set.'
    }

    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "$Name=$Value"
}

function Resolve-RuntimeIdentifier {
    switch ($RunnerOs) {
        'Windows' {
            switch ($RunnerArch) {
                'X64' { return 'win-x64' }
                'ARM64' { return 'win-arm64' }
            }
        }
        'Linux' {
            switch ($RunnerArch) {
                'X64' { return 'linux-x64' }
                'ARM64' { return 'linux-arm64' }
            }
        }
        'macOS' {
            switch ($RunnerArch) {
                'ARM64' { return 'osx-arm64' }
            }
        }
    }

    throw "Unsupported runner combination '$RunnerOs/$RunnerArch'."
}

function Normalize-Tag {
    param([string]$Tag)

    if ([string]::IsNullOrWhiteSpace($Tag)) {
        return $null
    }

    $trimmed = $Tag.Trim()
    if ($trimmed.StartsWith('refs/tags/', [System.StringComparison]::OrdinalIgnoreCase)) {
        $trimmed = $trimmed.Substring('refs/tags/'.Length)
    }

    return $trimmed
}

function Is-VersionTag {
    param([string]$Tag)

    if ([string]::IsNullOrWhiteSpace($Tag)) {
        return $false
    }

    return $Tag -match '^[vV]?\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$'
}

function Get-LatestReleaseTag {
    param([string]$Repository)

    if ([string]::IsNullOrWhiteSpace($Repository)) {
        throw 'Unable to resolve the action repository for release lookup.'
    }

    $headers = @{
        'User-Agent' = 'dotnet-install-action'
        'Accept' = 'application/vnd.github+json'
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)"
    }

    try {
        $release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repository/releases/latest"
    }
    catch {
        throw "Failed to resolve the latest release for '$Repository'. Use the action from a version tag or publish a GitHub release first. $($_.Exception.Message)"
    }

    if ([string]::IsNullOrWhiteSpace($release.tag_name)) {
        throw "The latest release for '$Repository' does not contain a tag name."
    }

    return $release.tag_name
}

function Get-LatestRelease {
    param([string]$Repository)

    if ([string]::IsNullOrWhiteSpace($Repository)) {
        throw 'Unable to resolve the action repository for release lookup.'
    }

    $headers = @{
        'User-Agent' = 'dotnet-install-action'
        'Accept' = 'application/vnd.github+json'
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)"
    }

    try {
        return Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repository/releases/latest"
    }
    catch {
        throw "Failed to resolve the latest release for '$Repository'. Use the action from a version tag or publish a GitHub release first. $($_.Exception.Message)"
    }
}

function Get-ReleaseAssetName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier
    )

    if ($RuntimeIdentifier.StartsWith('win-', [System.StringComparison]::OrdinalIgnoreCase)) {
        return "dotnet-install-$Version-$RuntimeIdentifier.exe"
    }

    return "dotnet-install-$Version-$RuntimeIdentifier"
}

function Get-ReleaseAssetInfo {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Release,

        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier
    )

    if ([string]::IsNullOrWhiteSpace($Release.tag_name)) {
        throw 'The latest release does not contain a tag name.'
    }

    $normalizedVersion = $Release.tag_name.TrimStart('v', 'V')
    $assetName = Get-ReleaseAssetName -Version $normalizedVersion -RuntimeIdentifier $RuntimeIdentifier
    $sha256Name = "$assetName.sha256"

    $assets = @($Release.assets)
    $binaryAsset = $assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    $sha256Asset = $assets | Where-Object { $_.name -eq $sha256Name } | Select-Object -First 1

    if ($null -eq $binaryAsset) {
        throw "The latest release does not contain asset '$assetName'."
    }

    if ($null -eq $sha256Asset) {
        throw "The latest release does not contain asset '$sha256Name'."
    }

    return [pscustomobject]@{
        Tag               = $Release.tag_name
        NormalizedVersion = $normalizedVersion
        DownloadUrl       = $binaryAsset.browser_download_url
        Sha256Url         = $sha256Asset.browser_download_url
    }
}

$runtimeIdentifier = Resolve-RuntimeIdentifier
$isLocalAction = [string]::IsNullOrWhiteSpace($ActionRepository)
$resolvedActionRepository = if ($isLocalAction) { $defaultActionRepository } else { $ActionRepository }

if ($isLocalAction) {
    $latestRelease = Get-LatestRelease -Repository $defaultActionRepository
    $assetInfo = Get-ReleaseAssetInfo -Release $latestRelease -RuntimeIdentifier $runtimeIdentifier
    $tag = $assetInfo.Tag
    $normalizedToolVersion = $assetInfo.NormalizedVersion
    $downloadUrl = $assetInfo.DownloadUrl
    $sha256Url = $assetInfo.Sha256Url
}
else {
    $tag = Normalize-Tag -Tag $ActionRef
    if (-not (Is-VersionTag -Tag $tag)) {
        $latestRelease = Get-LatestRelease -Repository $resolvedActionRepository
        $assetInfo = Get-ReleaseAssetInfo -Release $latestRelease -RuntimeIdentifier $runtimeIdentifier
        $tag = $assetInfo.Tag
        $normalizedToolVersion = $assetInfo.NormalizedVersion
        $downloadUrl = $assetInfo.DownloadUrl
        $sha256Url = $assetInfo.Sha256Url
    }
    else {
        $normalizedToolVersion = $tag.TrimStart('v', 'V')
        $assetName = Get-ReleaseAssetName -Version $normalizedToolVersion -RuntimeIdentifier $runtimeIdentifier
        $downloadBase = "https://github.com/$resolvedActionRepository/releases/download/$tag"
        $downloadUrl = "$downloadBase/$assetName"
        $sha256Url = "$downloadBase/$assetName.sha256"
    }
}

$isWindowsRid = $runtimeIdentifier.StartsWith('win-', [System.StringComparison]::OrdinalIgnoreCase)

$resolvedInstallDir = if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    Join-Path -Path $TempDirectory -ChildPath 'dotnet-sdk'
}
else {
    [System.IO.Path]::GetFullPath($InstallDir)
}

$toolDirectory = Join-Path -Path $TempDirectory -ChildPath "dotnet-install-action/$normalizedToolVersion/$runtimeIdentifier"
New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $resolvedInstallDir -Force | Out-Null

$toolExecutableName = if ($isWindowsRid) { 'dotnet-install.exe' } else { 'dotnet-install' }
$toolPath = Join-Path -Path $toolDirectory -ChildPath $toolExecutableName
$cacheKey = "dotnet-install-action|$resolvedActionRepository|$normalizedToolVersion|$runtimeIdentifier|$Version"

Write-ActionOutput -Name 'install-dir' -Value $resolvedInstallDir
Write-ActionOutput -Name 'tool-path' -Value $toolPath
Write-ActionOutput -Name 'download-url' -Value $downloadUrl
Write-ActionOutput -Name 'sha256-url' -Value $sha256Url
