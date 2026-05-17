[CmdletBinding()]
param(
    [Parameter()]
    [AllowEmptyString()]
    [string]$RequestedVersion = '',

    [Parameter()]
    [AllowEmptyString()]
    [string]$GlobalJsonFile = '',

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

function Write-Diagnostic {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host "[dotnet-install-action] $Message"
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
        [Parameter()]
        [AllowEmptyString()]
        [string]$VersionText = ''
    )

    # GitHub Actions preserves multiline inputs as a single string; split on lines and ignore blanks.
    return @(
        $VersionText -split "\r?\n" |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

function Resolve-GlobalJsonPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ''
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    $basePath = if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_WORKSPACE)) {
        $env:GITHUB_WORKSPACE
    }
    else {
        (Get-Location).Path
    }

    return [System.IO.Path]::GetFullPath((Join-Path -Path $basePath -ChildPath $Path))
}

function New-InstallArguments {
    param(
        [string]$Version,
        [string]$ResolvedGlobalJsonFile
    )

    $arguments = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($ResolvedGlobalJsonFile)) {
        $arguments.Add('--jsonfile')
        $arguments.Add($ResolvedGlobalJsonFile)
    }
    else {
        $arguments.Add('--version')
        $arguments.Add($Version)
    }

    return $arguments.ToArray()
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
        Write-Diagnostic "Downloading '$Url' to '$DestinationPath'."
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

    Write-Diagnostic "Invoking tool: $ToolPath $($Arguments -join ' ')"
    $output = & $ToolPath @Arguments 2>&1
    $rendered = ($output | ForEach-Object { "$_" }) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-install failed with exit code $LASTEXITCODE.`n$rendered"
    }

    if (-not [string]::IsNullOrWhiteSpace($rendered)) {
        Write-Diagnostic "Tool output:`n$rendered"
    }

    return $rendered
}

function Resolve-RequestedSdkVersion {
    param(
        [string]$Version,
        [string]$ResolvedGlobalJsonFile
    )

    # Resolve selectors such as `10.0.x` to the concrete SDK version before installation.
    $installArguments = @(New-InstallArguments -Version $Version -ResolvedGlobalJsonFile $ResolvedGlobalJsonFile)
    $dryRunArguments = @('--dry-run') + $installArguments + @('--install-dir', $InstallDir, '--no-path')
    $dryRunOutput = Invoke-Tool -Arguments $dryRunArguments

    $resolvedVersionMatch = [regex]::Match($dryRunOutput, 'Product:\s*Sdk\s+(?<version>\S+)\s+\|')
    if (-not $resolvedVersionMatch.Success) {
        $source = if (-not [string]::IsNullOrWhiteSpace($ResolvedGlobalJsonFile)) { $ResolvedGlobalJsonFile } else { $Version }
        throw "Failed to resolve the SDK version from dotnet-install dry-run output for '$source'.`n$dryRunOutput"
    }

    return $resolvedVersionMatch.Groups['version'].Value
}

$toolDirectory = Split-Path -Path $ToolPath -Parent
$toolFileName = Split-Path -Path $ToolPath -Leaf
$shaPath = "$ToolPath.sha256"

Write-Diagnostic "RequestedVersionRaw='${RequestedVersion}'"
Write-Diagnostic "GlobalJsonFileRaw='${GlobalJsonFile}'"
Write-Diagnostic "InstallDir='${InstallDir}'"
Write-Diagnostic "ToolPath='${ToolPath}'"
Write-Diagnostic "DownloadUrl='${DownloadUrl}'"
Write-Diagnostic "Sha256Url='${Sha256Url}'"
Write-Diagnostic "RunnerOs='${RunnerOs}'"

New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

Invoke-Download -Url $DownloadUrl -DestinationPath $ToolPath
Invoke-Download -Url $Sha256Url -DestinationPath $shaPath

if ($RunnerOs -ne 'Windows') {
    & chmod +x $ToolPath
    Write-Diagnostic "Marked tool as executable."
}

$expectedHashLine = Get-Content -LiteralPath $shaPath -Raw
$match = [regex]::Match($expectedHashLine, '^(?<hash>[0-9a-fA-F]{64})\s+')
if (-not $match.Success) {
    throw "Unable to parse SHA-256 content from '$Sha256Url'."
}

$expectedHash = $match.Groups['hash'].Value.ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath $ToolPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Diagnostic "Expected tool SHA256='$expectedHash'"
Write-Diagnostic "Actual tool SHA256='$actualHash'"
if ($actualHash -ne $expectedHash) {
    throw "SHA-256 verification failed for '$toolFileName'. Expected '$expectedHash' but got '$actualHash'."
}

$resolvedGlobalJsonFile = Resolve-GlobalJsonPath -Path $GlobalJsonFile
if (-not [string]::IsNullOrWhiteSpace($resolvedGlobalJsonFile)) {
    Write-Diagnostic "ResolvedGlobalJsonFile='${resolvedGlobalJsonFile}'"
    if (-not (Test-Path -LiteralPath $resolvedGlobalJsonFile)) {
        throw "The global.json file was not found at '$resolvedGlobalJsonFile'."
    }
}

$requestedVersions = @(Split-RequestedVersions -VersionText $RequestedVersion)
if ($requestedVersions.Length -gt 0 -and -not [string]::IsNullOrWhiteSpace($resolvedGlobalJsonFile)) {
    throw 'The version and global-json-file inputs cannot be combined.'
}

if ($requestedVersions.Length -eq 0 -and [string]::IsNullOrWhiteSpace($resolvedGlobalJsonFile)) {
    throw 'Either a version input or a global-json-file input must be provided.'
}

if (-not [string]::IsNullOrWhiteSpace($resolvedGlobalJsonFile)) {
    $requestedVersions = @($resolvedGlobalJsonFile)
}
Write-Diagnostic "Requested version count=$($requestedVersions.Length)"
Write-Diagnostic "Requested versions='$($requestedVersions -join ', ')'"

$resolvedVersions = [System.Collections.Generic.List[string]]::new()
$installedResolvedVersions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($version in $requestedVersions) {
    $globalJsonForRequest = if (-not [string]::IsNullOrWhiteSpace($resolvedGlobalJsonFile)) { $resolvedGlobalJsonFile } else { '' }
    $resolvedVersion = Resolve-RequestedSdkVersion -Version $version -ResolvedGlobalJsonFile $globalJsonForRequest
    $resolvedVersions.Add($resolvedVersion)
    Write-Diagnostic "Resolved requested version '$version' to SDK '$resolvedVersion'."

    # Different selectors can converge on the same concrete SDK; install it only once.
    if (-not $installedResolvedVersions.Add($resolvedVersion)) {
        Write-Diagnostic "SDK version '$resolvedVersion' was already requested earlier in this action run. Skipping duplicate install."
        continue
    }

    $installArguments = @(New-InstallArguments -Version $version -ResolvedGlobalJsonFile $globalJsonForRequest)
    [void](Invoke-Tool -Arguments ($installArguments + @('--install-dir', $InstallDir, '--no-path', '--yes')))
}

$dotnetExecutableName = if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_INSTALL_ACTION_DOTNET_EXECUTABLE_NAME)) {
    $env:DOTNET_INSTALL_ACTION_DOTNET_EXECUTABLE_NAME
}
elseif ($RunnerOs -eq 'Windows') {
    'dotnet.exe'
}
else {
    'dotnet'
}
$dotnetExecutable = Join-Path -Path $InstallDir -ChildPath $dotnetExecutableName
Write-Diagnostic "Expected dotnet executable path='$dotnetExecutable'"
if (-not (Test-Path -LiteralPath $dotnetExecutable)) {
    throw "The installed dotnet executable was not found at '$dotnetExecutable'."
}

$env:DOTNET_ROOT = $InstallDir
$env:DOTNET_INSTALL_DIR = $InstallDir
$env:PATH = "$InstallDir$([System.IO.Path]::PathSeparator)$env:PATH"
Write-Diagnostic "Exported DOTNET_ROOT and DOTNET_INSTALL_DIR to '$InstallDir'."

$installedSdks = (& $dotnetExecutable --list-sdks 2>&1 | ForEach-Object { "$_" }) -join [Environment]::NewLine
Write-Diagnostic "dotnet --list-sdks output:`n$installedSdks"
# Verify every resolved SDK against the installed host, not just the last one.
foreach ($resolvedVersion in $resolvedVersions) {
    if ($installedSdks -notmatch "(?m)^$([regex]::Escape($resolvedVersion))\s+\[") {
        throw "The installed dotnet host does not report SDK version '$resolvedVersion'.`n$installedSdks"
    }

    Write-Diagnostic "Verified installed SDK '$resolvedVersion'."
}

Add-EnvironmentVariable -Name 'DOTNET_ROOT' -Value $InstallDir
Add-EnvironmentVariable -Name 'DOTNET_INSTALL_DIR' -Value $InstallDir
Add-PathEntry -PathEntry $InstallDir
Write-Diagnostic "Wrote action environment variables and PATH entry."

Write-MultilineActionOutput -Name 'resolved-version' -Value (($resolvedVersions | Select-Object -Unique) -join [Environment]::NewLine)
Write-ActionOutput -Name 'install-dir' -Value $InstallDir
Write-ActionOutput -Name 'dotnet-root' -Value $InstallDir
Write-ActionOutput -Name 'dotnet-path' -Value $dotnetExecutable
Write-Diagnostic "Wrote action outputs successfully."
