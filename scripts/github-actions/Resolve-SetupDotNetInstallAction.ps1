[CmdletBinding()]
param(
    [Parameter()]
    [AllowEmptyString()]
    [string]$Version = '',

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
# Local-path usage does not carry a stable action repository identity, so fall back to the
# canonical published release source for the binary while keeping the local script logic.
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

function Write-Diagnostic {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host "[dotnet-install-action] $Message"
}

function Test-IsMuslBasedLinux {
    $osReleasePath = if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_INSTALL_ACTION_OS_RELEASE_PATH)) {
        $env:DOTNET_INSTALL_ACTION_OS_RELEASE_PATH
    }
    else {
        '/etc/os-release'
    }

    if (Test-Path -LiteralPath $osReleasePath) {
        $osReleaseContent = Get-Content -LiteralPath $osReleasePath -Raw
        if ($osReleaseContent -match '(?m)^ID="?alpine"?$') {
            return $true
        }
    }

    try {
        $lddOutput = (& ldd --version 2>&1 | Out-String)
        if ($lddOutput -match 'musl') {
            return $true
        }
    }
    catch {
        Write-Diagnostic "Unable to execute 'ldd --version' while checking for musl: $($_.Exception.Message)"
    }

    return $false
}

function Resolve-RuntimeIdentifier {
    # Match the release artifact RID names produced by the publish workflow.
    switch ($RunnerOs) {
        'Windows' {
            switch ($RunnerArch) {
                'X64' { return 'win-x64' }
                'ARM64' { return 'win-arm64' }
            }
        }
        'Linux' {
            $isMusl = Test-IsMuslBasedLinux
            switch ($RunnerArch) {
                'X64' { return $(if ($isMusl) { 'linux-musl-x64' } else { 'linux-x64' }) }
                'ARM64' { return $(if ($isMusl) { 'linux-musl-arm64' } else { 'linux-arm64' }) }
            }
        }
        'macOS' {
            switch ($RunnerArch) {
                'X64' { return 'osx-x64' }
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

function New-GitHubApiHeaders {
    $headers = @{
        'User-Agent' = 'dotnet-install-action'
        'Accept' = 'application/vnd.github+json'
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)"
    }

    return $headers
}

function Get-GitHubApiBaseUrl {
    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_INSTALL_ACTION_GITHUB_API_BASE_URL)) {
        return $env:DOTNET_INSTALL_ACTION_GITHUB_API_BASE_URL.TrimEnd('/')
    }

    return 'https://api.github.com'
}

function Get-LatestRelease {
    param([string]$Repository)

    if ([string]::IsNullOrWhiteSpace($Repository)) {
        throw 'Unable to resolve the action repository for release lookup.'
    }

    $headers = New-GitHubApiHeaders
    $apiBaseUrl = Get-GitHubApiBaseUrl

    try {
        return Invoke-RestMethod -Headers $headers -Uri "$apiBaseUrl/repos/$Repository/releases/latest"
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

function Find-ReleaseWithAssets {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Releases,

        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier,

        [Parameter(Mandatory = $true)]
        [bool]$Prerelease
    )

    foreach ($release in $Releases) {
        if ([bool]$release.prerelease -ne $Prerelease) {
            continue
        }

        $normalizedVersion = $release.tag_name.TrimStart('v', 'V')
        $assetName = Get-ReleaseAssetName -Version $normalizedVersion -RuntimeIdentifier $RuntimeIdentifier
        $sha256Name = "$assetName.sha256"
        $assetNames = @($release.assets | ForEach-Object { $_.name })

        if ($assetNames -contains $assetName -and $assetNames -contains $sha256Name) {
            return $release
        }

        Write-Diagnostic "Skipping release '$($release.tag_name)' because required assets '$assetName' and '$sha256Name' were not both present."
    }

    return $null
}

function Get-LatestPublishedRelease {
    param(
        [string]$Repository,
        [string]$RuntimeIdentifier
    )

    $headers = New-GitHubApiHeaders
    $apiBaseUrl = Get-GitHubApiBaseUrl

    try {
        $stableRelease = Get-LatestRelease -Repository $Repository
        if ($null -ne (Find-ReleaseWithAssets -Releases @($stableRelease) -RuntimeIdentifier $RuntimeIdentifier -Prerelease $false)) {
            Write-Diagnostic "Resolved latest stable release '$($stableRelease.tag_name)'."
            return $stableRelease
        }

        Write-Diagnostic "Latest stable release '$($stableRelease.tag_name)' did not contain the required assets for '$RuntimeIdentifier'."
    }
    catch {
        Write-Diagnostic "Latest stable release lookup failed: $($_.Exception.Message)"
    }

    try {
        $response = Invoke-RestMethod -Headers $headers -Uri "$apiBaseUrl/repos/$Repository/releases?per_page=20"
    }
    catch {
        throw "Failed to enumerate releases for '$Repository'. $($_.Exception.Message)"
    }

    $releases = @($response)
    Write-Diagnostic "Enumerated $($releases.Count) release entries from the releases API."
    foreach ($release in $releases) {
        if ($null -ne $release) {
            Write-Diagnostic "Release candidate tag='$($release.tag_name)' draft='$($release.draft)' prerelease='$($release.prerelease)'"
        }
    }

    $publishedReleases = @($releases | Where-Object {
        $null -ne $_ -and
        $_.PSObject.Properties.Name -contains 'tag_name' -and
        -not [bool]$_.draft
    })
    Write-Diagnostic "Published release candidates after draft filtering: $($publishedReleases.Count)"
    if ($publishedReleases.Count -eq 0) {
        throw "No published releases were found for '$Repository'. Publish a stable or prerelease first."
    }

    $stableRelease = Find-ReleaseWithAssets -Releases $publishedReleases -RuntimeIdentifier $RuntimeIdentifier -Prerelease $false
    if ($null -ne $stableRelease) {
        Write-Diagnostic "Resolved latest stable release '$($stableRelease.tag_name)' from release listing fallback."
        return $stableRelease
    }

    $previewRelease = Find-ReleaseWithAssets -Releases $publishedReleases -RuntimeIdentifier $RuntimeIdentifier -Prerelease $true
    if ($null -eq $previewRelease) {
        throw "No stable or prerelease releases with matching assets were found for '$Repository' and RID '$RuntimeIdentifier'."
    }

    Write-Diagnostic "Falling back to latest prerelease '$($previewRelease.tag_name)'."
    return $previewRelease
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

    # Release assets are versioned by the normalized tag without the leading 'v'.
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

Write-Diagnostic "ActionRepository='${ActionRepository}'"
Write-Diagnostic "ActionRef='${ActionRef}'"
Write-Diagnostic "GitHubRepository='${env:GITHUB_REPOSITORY}'"
Write-Diagnostic "RunnerOs='${RunnerOs}' RunnerArch='${RunnerArch}' RuntimeIdentifier='${runtimeIdentifier}'"
Write-Diagnostic "IsLocalAction='${isLocalAction}' ResolvedActionRepository='${resolvedActionRepository}'"

if ($isLocalAction) {
    # For `uses: ./`, always pair the checked-out action scripts with the latest published binary.
    Write-Diagnostic "Using local action scripts with the latest published release binary."
    $latestRelease = Get-LatestPublishedRelease -Repository $defaultActionRepository -RuntimeIdentifier $runtimeIdentifier
    $assetInfo = Get-ReleaseAssetInfo -Release $latestRelease -RuntimeIdentifier $runtimeIdentifier
    $tag = $assetInfo.Tag
    $normalizedToolVersion = $assetInfo.NormalizedVersion
    $downloadUrl = $assetInfo.DownloadUrl
    $sha256Url = $assetInfo.Sha256Url
    Write-Diagnostic "Resolved latest release tag '${tag}' for local action mode."
}
else {
    $tag = Normalize-Tag -Tag $ActionRef
    Write-Diagnostic "Normalized action ref to '${tag}'."
    if (-not (Is-VersionTag -Tag $tag)) {
        # Branch refs and mutable refs do not map to a unique release asset, so use the latest release.
        Write-Diagnostic "Action ref is not a version tag. Falling back to the latest published release binary."
        $latestRelease = Get-LatestPublishedRelease -Repository $resolvedActionRepository -RuntimeIdentifier $runtimeIdentifier
        $assetInfo = Get-ReleaseAssetInfo -Release $latestRelease -RuntimeIdentifier $runtimeIdentifier
        $tag = $assetInfo.Tag
        $normalizedToolVersion = $assetInfo.NormalizedVersion
        $downloadUrl = $assetInfo.DownloadUrl
        $sha256Url = $assetInfo.Sha256Url
        Write-Diagnostic "Resolved latest release tag '${tag}' for non-tag action ref."
    }
    else {
        # Tagged action usage is deterministic and can point directly at the matching release asset.
        $normalizedToolVersion = $tag.TrimStart('v', 'V')
        $assetName = Get-ReleaseAssetName -Version $normalizedToolVersion -RuntimeIdentifier $runtimeIdentifier
        $downloadBase = "https://github.com/$resolvedActionRepository/releases/download/$tag"
        $downloadUrl = "$downloadBase/$assetName"
        $sha256Url = "$downloadBase/$assetName.sha256"
        Write-Diagnostic "Using release-tag mode with asset '${assetName}'."
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

Write-Diagnostic "RequestedVersionInput='${Version}'"
Write-Diagnostic "ResolvedInstallDir='${resolvedInstallDir}'"
Write-Diagnostic "ToolPath='${toolPath}'"
Write-Diagnostic "DownloadUrl='${downloadUrl}'"
Write-Diagnostic "Sha256Url='${sha256Url}'"

Write-ActionOutput -Name 'install-dir' -Value $resolvedInstallDir
Write-ActionOutput -Name 'tool-path' -Value $toolPath
Write-ActionOutput -Name 'download-url' -Value $downloadUrl
Write-ActionOutput -Name 'sha256-url' -Value $sha256Url
