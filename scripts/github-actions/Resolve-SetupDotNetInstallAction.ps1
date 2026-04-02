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

$resolvedActionRepository = if ([string]::IsNullOrWhiteSpace($ActionRepository)) {
    $env:GITHUB_REPOSITORY
}
else {
    $ActionRepository
}

$tag = Normalize-Tag -Tag $ActionRef
if (-not (Is-VersionTag -Tag $tag)) {
    $tag = Get-LatestReleaseTag -Repository $resolvedActionRepository
}

$normalizedToolVersion = $tag.TrimStart('v', 'V')
$runtimeIdentifier = Resolve-RuntimeIdentifier
$isWindowsRid = $runtimeIdentifier.StartsWith('win-', [System.StringComparison]::OrdinalIgnoreCase)
$assetName = if ($isWindowsRid) {
    "dotnet-install-$normalizedToolVersion-$runtimeIdentifier.exe"
}
else {
    "dotnet-install-$normalizedToolVersion-$runtimeIdentifier"
}

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
$downloadBase = "https://github.com/$resolvedActionRepository/releases/download/$tag"
$cacheKey = "dotnet-install-action|$resolvedActionRepository|$normalizedToolVersion|$runtimeIdentifier|$Version"

Write-ActionOutput -Name 'install-dir' -Value $resolvedInstallDir
Write-ActionOutput -Name 'tool-path' -Value $toolPath
Write-ActionOutput -Name 'download-url' -Value "$downloadBase/$assetName"
Write-ActionOutput -Name 'sha256-url' -Value "$downloadBase/$assetName.sha256"
Write-ActionOutput -Name 'cache-key' -Value $cacheKey
