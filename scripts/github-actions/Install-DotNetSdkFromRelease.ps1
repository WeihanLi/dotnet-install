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

$dryRunOutput = Invoke-Tool -Arguments @(
    '--dry-run',
    '--version', $RequestedVersion,
    '--install-dir', $InstallDir,
    '--no-path'
)

$resolvedVersionMatch = [regex]::Match($dryRunOutput, 'Product:\s*Sdk\s+(?<version>\S+)\s+\|')
if (-not $resolvedVersionMatch.Success) {
    throw "Failed to resolve the SDK version from dotnet-install dry-run output.`n$dryRunOutput"
}

$resolvedVersion = $resolvedVersionMatch.Groups['version'].Value

[void](Invoke-Tool -Arguments @(
    '--version', $RequestedVersion,
    '--install-dir', $InstallDir,
    '--no-path',
    '--yes'
))

$dotnetExecutableName = if ($RunnerOs -eq 'Windows') { 'dotnet.exe' } else { 'dotnet' }
$dotnetExecutable = Join-Path -Path $InstallDir -ChildPath $dotnetExecutableName
if (-not (Test-Path -LiteralPath $dotnetExecutable)) {
    throw "The installed dotnet executable was not found at '$dotnetExecutable'."
}

$env:DOTNET_ROOT = $InstallDir
$env:DOTNET_INSTALL_DIR = $InstallDir
$env:PATH = "$InstallDir$([System.IO.Path]::PathSeparator)$env:PATH"

$installedSdks = (& $dotnetExecutable --list-sdks 2>&1 | ForEach-Object { "$_" }) -join [Environment]::NewLine
if ($installedSdks -notmatch "(?m)^$([regex]::Escape($resolvedVersion))\s+\[") {
    throw "The installed dotnet host does not report SDK version '$resolvedVersion'.`n$installedSdks"
}

Add-EnvironmentVariable -Name 'DOTNET_ROOT' -Value $InstallDir
Add-EnvironmentVariable -Name 'DOTNET_INSTALL_DIR' -Value $InstallDir
Add-PathEntry -PathEntry $InstallDir

Write-ActionOutput -Name 'resolved-version' -Value $resolvedVersion
Write-ActionOutput -Name 'install-dir' -Value $InstallDir
Write-ActionOutput -Name 'dotnet-root' -Value $InstallDir
Write-ActionOutput -Name 'dotnet-path' -Value $dotnetExecutable
