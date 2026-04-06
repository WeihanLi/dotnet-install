[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RequestedVersion,

    [Parameter(Mandatory = $true)]
    [string]$InstallDir,

    [Parameter(Mandatory = $true)]
    [string]$ToolPath,

    [Parameter(Mandatory = $true)]
    [string]$DownloadUrl,

    [Parameter(Mandatory = $true)]
    [string]$Sha256Url,

    [Parameter(Mandatory = $true)]
    [string]$RunnerOs
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

function Write-MultilineActionOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        throw 'GITHUB_OUTPUT is not set.'
    }

    $marker = [Guid]::NewGuid().ToString('N')
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "$Name<<$marker"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value $Value.TrimEnd()
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value $marker
}

function Split-RequestedVersions {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VersionText
    )

    # GitHub Actions preserves multiline inputs as a single string; split on lines and ignore blanks.
    return @(
        $VersionText -split "\r?\n" |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

function Add-EnvironmentVariable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) {
        throw 'GITHUB_ENV is not set.'
    }

    Add-Content -LiteralPath $env:GITHUB_ENV -Value "$Name=$Value"
}

function Add-PathEntry {
    param([string]$PathEntry)

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_PATH)) {
        throw 'GITHUB_PATH is not set.'
    }

    Add-Content -LiteralPath $env:GITHUB_PATH -Value $PathEntry
}

function Invoke-Download {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    # GitHub-hosted release downloads accept the workflow token when one is available.
    $headers = @{
        'User-Agent' = 'dotnet-install-action'
        'Accept' = 'application/octet-stream'
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)"
    }

    try {
        Invoke-WebRequest -Headers $headers -Uri $Url -OutFile $DestinationPath
    }
    catch {
        throw "Failed to download '$Url'. $($_.Exception.Message)"
    }
}

function Invoke-Tool {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & $ToolPath @Arguments 2>&1
    $rendered = ($output | ForEach-Object { "$_" }) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-install failed with exit code $LASTEXITCODE.`n$rendered"
    }

    return $rendered
}

function Resolve-RequestedSdkVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    # Resolve selectors such as `10.0.x` to the concrete SDK version before installation.
    $dryRunOutput = Invoke-Tool -Arguments @(
        '--dry-run',
        '--version', $Version,
        '--install-dir', $InstallDir,
        '--no-path'
    )

    $resolvedVersionMatch = [regex]::Match($dryRunOutput, 'Product:\s*Sdk\s+(?<version>\S+)\s+\|')
    if (-not $resolvedVersionMatch.Success) {
        throw "Failed to resolve the SDK version from dotnet-install dry-run output for '$Version'.`n$dryRunOutput"
    }

    return $resolvedVersionMatch.Groups['version'].Value
}

$toolDirectory = Split-Path -Path $ToolPath -Parent
$toolFileName = Split-Path -Path $ToolPath -Leaf
$shaPath = "$ToolPath.sha256"

New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

Invoke-Download -Url $DownloadUrl -DestinationPath $ToolPath
Invoke-Download -Url $Sha256Url -DestinationPath $shaPath

if ($RunnerOs -ne 'Windows') {
    & chmod +x $ToolPath
}

$expectedHashLine = Get-Content -LiteralPath $shaPath -Raw
$match = [regex]::Match($expectedHashLine, '^(?<hash>[0-9a-fA-F]{64})\s+')
if (-not $match.Success) {
    throw "Unable to parse SHA-256 content from '$Sha256Url'."
}

$expectedHash = $match.Groups['hash'].Value.ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath $ToolPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "SHA-256 verification failed for '$toolFileName'. Expected '$expectedHash' but got '$actualHash'."
}

$requestedVersions = Split-RequestedVersions -VersionText $RequestedVersion
if ($requestedVersions.Count -eq 0) {
    throw 'At least one SDK version must be provided.'
}

$resolvedVersions = [System.Collections.Generic.List[string]]::new()
$installedResolvedVersions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($version in $requestedVersions) {
    $resolvedVersion = Resolve-RequestedSdkVersion -Version $version
    $resolvedVersions.Add($resolvedVersion)

    # Different selectors can converge on the same concrete SDK; install it only once.
    if (-not $installedResolvedVersions.Add($resolvedVersion)) {
        Write-Host "SDK version '$resolvedVersion' was already requested earlier in this action run. Skipping duplicate install."
        continue
    }

    [void](Invoke-Tool -Arguments @(
        '--version', $version,
        '--install-dir', $InstallDir,
        '--no-path',
        '--yes'
    ))
}

$dotnetExecutableName = if ($RunnerOs -eq 'Windows') { 'dotnet.exe' } else { 'dotnet' }
$dotnetExecutable = Join-Path -Path $InstallDir -ChildPath $dotnetExecutableName
if (-not (Test-Path -LiteralPath $dotnetExecutable)) {
    throw "The installed dotnet executable was not found at '$dotnetExecutable'."
}

$env:DOTNET_ROOT = $InstallDir
$env:DOTNET_INSTALL_DIR = $InstallDir
$env:PATH = "$InstallDir$([System.IO.Path]::PathSeparator)$env:PATH"

$installedSdks = (& $dotnetExecutable --list-sdks 2>&1 | ForEach-Object { "$_" }) -join [Environment]::NewLine
# Verify every resolved SDK against the installed host, not just the last one.
foreach ($resolvedVersion in $resolvedVersions) {
    if ($installedSdks -notmatch "(?m)^$([regex]::Escape($resolvedVersion))\s+\[") {
        throw "The installed dotnet host does not report SDK version '$resolvedVersion'.`n$installedSdks"
    }
}

Add-EnvironmentVariable -Name 'DOTNET_ROOT' -Value $InstallDir
Add-EnvironmentVariable -Name 'DOTNET_INSTALL_DIR' -Value $InstallDir
Add-PathEntry -PathEntry $InstallDir

Write-MultilineActionOutput -Name 'resolved-version' -Value (($resolvedVersions | Select-Object -Unique) -join [Environment]::NewLine)
Write-ActionOutput -Name 'install-dir' -Value $InstallDir
Write-ActionOutput -Name 'dotnet-root' -Value $InstallDir
Write-ActionOutput -Name 'dotnet-path' -Value $dotnetExecutable
