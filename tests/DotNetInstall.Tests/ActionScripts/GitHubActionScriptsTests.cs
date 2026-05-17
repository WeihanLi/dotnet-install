using System.Text;

namespace DotNetInstall.Tests.ActionScripts;

public sealed class GitHubActionScriptsTests
{
    [Fact]
    public async Task ResolveScript_LocalMode_FallsBackToLatestPrereleaseRelease()
    {
        using var tempDir = new TemporaryDirectory();
        using var server = new TestHttpServer();
        var outputPath = tempDir.GetPath("github-output.txt");
        File.WriteAllText(outputPath, string.Empty);

        server.AddJson(
            "/repos/WeihanLi/dotnet-install/releases/latest",
            """{"message":"Not Found","status":"404"}""",
            statusCode: 404);
        server.AddJson(
            "/repos/WeihanLi/dotnet-install/releases?per_page=20",
            $$"""
            [
              {
                "tag_name": "0.1.0-rc-2",
                "draft": false,
                "prerelease": true,
                "assets": [
                  {
                    "name": "dotnet-install-0.1.0-rc-2-win-x64.exe",
                    "browser_download_url": "{{server.BaseUri}}downloads/dotnet-install-0.1.0-rc-2-win-x64.exe"
                  },
                  {
                    "name": "dotnet-install-0.1.0-rc-2-win-x64.exe.sha256",
                    "browser_download_url": "{{server.BaseUri}}downloads/dotnet-install-0.1.0-rc-2-win-x64.exe.sha256"
                  }
                ]
              }
            ]
            """);

        var result = await ActionScriptTestHost.RunPowerShellFileAsync(
            ActionScriptTestHost.ResolveScriptPath(@"scripts\github-actions\Resolve-SetupDotNetInstallAction.ps1"),
            [
                "-Version", "10.0.x",
                "-RunnerOs", "Windows",
                "-RunnerArch", "X64",
                "-TempDirectory", tempDir.Path
            ],
            new Dictionary<string, string?>
            {
                ["GITHUB_OUTPUT"] = outputPath,
                ["DOTNET_INSTALL_ACTION_GITHUB_API_BASE_URL"] = server.BaseUri.ToString().TrimEnd('/'),
                ["GITHUB_TOKEN"] = string.Empty
            });

        Assert.True(
            result.ExitCode == 0,
            $"ExitCode={result.ExitCode}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}");
        var outputs = ActionScriptTestHost.ParseGitHubOutputFile(outputPath);
        Assert.Equal($"{server.BaseUri}downloads/dotnet-install-0.1.0-rc-2-win-x64.exe", outputs["download-url"]);
        Assert.Equal($"{server.BaseUri}downloads/dotnet-install-0.1.0-rc-2-win-x64.exe.sha256", outputs["sha256-url"]);
        Assert.Contains("Falling back to latest prerelease '0.1.0-rc-2'.", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveScript_TagMode_UsesDeterministicReleaseUrls()
    {
        using var tempDir = new TemporaryDirectory();
        var outputPath = tempDir.GetPath("github-output.txt");
        File.WriteAllText(outputPath, string.Empty);

        var result = await ActionScriptTestHost.RunPowerShellFileAsync(
            ActionScriptTestHost.ResolveScriptPath(@"scripts\github-actions\Resolve-SetupDotNetInstallAction.ps1"),
            [
                "-Version", "10.0.x",
                "-ActionRepository", "ExampleOrg/dotnet-install",
                "-ActionRef", "refs/tags/v1.2.3",
                "-RunnerOs", "Windows",
                "-RunnerArch", "X64",
                "-TempDirectory", tempDir.Path
            ],
            new Dictionary<string, string?>
            {
                ["GITHUB_OUTPUT"] = outputPath
            });

        Assert.True(
            result.ExitCode == 0,
            $"ExitCode={result.ExitCode}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}");
        var outputs = ActionScriptTestHost.ParseGitHubOutputFile(outputPath);
        Assert.Equal(
            "https://github.com/ExampleOrg/dotnet-install/releases/download/v1.2.3/dotnet-install-1.2.3-win-x64.exe",
            outputs["download-url"]);
        Assert.Equal(
            "https://github.com/ExampleOrg/dotnet-install/releases/download/v1.2.3/dotnet-install-1.2.3-win-x64.exe.sha256",
            outputs["sha256-url"]);
    }

    [Fact]
    public async Task ResolveScript_LocalMode_SkipsStableReleaseWithoutAssets_AndUsesPrereleaseWithAssets()
    {
        using var tempDir = new TemporaryDirectory();
        using var server = new TestHttpServer();
        var outputPath = tempDir.GetPath("github-output.txt");
        File.WriteAllText(outputPath, string.Empty);

        server.AddJson(
            "/repos/WeihanLi/dotnet-install/releases/latest",
            """
            {
              "tag_name": "0.1.0",
              "draft": false,
              "prerelease": false,
              "assets": []
            }
            """);
        server.AddJson(
            "/repos/WeihanLi/dotnet-install/releases?per_page=20",
            $$"""
            [
              {
                "tag_name": "0.1.0",
                "draft": false,
                "prerelease": false,
                "assets": []
              },
              {
                "tag_name": "0.1.0-rc-2",
                "draft": false,
                "prerelease": true,
                "assets": [
                  {
                    "name": "dotnet-install-0.1.0-rc-2-linux-x64",
                    "browser_download_url": "{{server.BaseUri}}downloads/dotnet-install-0.1.0-rc-2-linux-x64"
                  },
                  {
                    "name": "dotnet-install-0.1.0-rc-2-linux-x64.sha256",
                    "browser_download_url": "{{server.BaseUri}}downloads/dotnet-install-0.1.0-rc-2-linux-x64.sha256"
                  }
                ]
              }
            ]
            """);

        var result = await ActionScriptTestHost.RunPowerShellFileAsync(
            ActionScriptTestHost.ResolveScriptPath(@"scripts\github-actions\Resolve-SetupDotNetInstallAction.ps1"),
            [
                "-Version", "10.0.x",
                "-RunnerOs", "Linux",
                "-RunnerArch", "X64",
                "-TempDirectory", tempDir.Path
            ],
            new Dictionary<string, string?>
            {
                ["GITHUB_OUTPUT"] = outputPath,
                ["DOTNET_INSTALL_ACTION_GITHUB_API_BASE_URL"] = server.BaseUri.ToString().TrimEnd('/'),
                ["GITHUB_TOKEN"] = string.Empty
            });

        Assert.True(
            result.ExitCode == 0,
            $"ExitCode={result.ExitCode}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}");
        var outputs = ActionScriptTestHost.ParseGitHubOutputFile(outputPath);
        Assert.Equal($"{server.BaseUri}downloads/dotnet-install-0.1.0-rc-2-linux-x64", outputs["download-url"]);
        Assert.Equal($"{server.BaseUri}downloads/dotnet-install-0.1.0-rc-2-linux-x64.sha256", outputs["sha256-url"]);
        Assert.Contains("Latest stable release '0.1.0' did not contain the required assets for 'linux-x64'.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Falling back to latest prerelease '0.1.0-rc-2'.", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveScript_LocalMode_UsesMacOsX64ReleaseAsset()
    {
        using var tempDir = new TemporaryDirectory();
        using var server = new TestHttpServer();
        var outputPath = tempDir.GetPath("github-output.txt");
        File.WriteAllText(outputPath, string.Empty);

        server.AddJson(
            "/repos/WeihanLi/dotnet-install/releases/latest",
            $$"""
            {
              "tag_name": "0.2.0",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "dotnet-install-0.2.0-osx-x64",
                  "browser_download_url": "{{server.BaseUri}}downloads/dotnet-install-0.2.0-osx-x64"
                },
                {
                  "name": "dotnet-install-0.2.0-osx-x64.sha256",
                  "browser_download_url": "{{server.BaseUri}}downloads/dotnet-install-0.2.0-osx-x64.sha256"
                }
              ]
            }
            """);

        var result = await ActionScriptTestHost.RunPowerShellFileAsync(
            ActionScriptTestHost.ResolveScriptPath(@"scripts\github-actions\Resolve-SetupDotNetInstallAction.ps1"),
            [
                "-Version", "10.0.x",
                "-RunnerOs", "macOS",
                "-RunnerArch", "X64",
                "-TempDirectory", tempDir.Path
            ],
            new Dictionary<string, string?>
            {
                ["GITHUB_OUTPUT"] = outputPath,
                ["DOTNET_INSTALL_ACTION_GITHUB_API_BASE_URL"] = server.BaseUri.ToString().TrimEnd('/'),
                ["GITHUB_TOKEN"] = string.Empty
            });

        Assert.True(
            result.ExitCode == 0,
            $"ExitCode={result.ExitCode}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}");
        var outputs = ActionScriptTestHost.ParseGitHubOutputFile(outputPath);
        Assert.Equal($"{server.BaseUri}downloads/dotnet-install-0.2.0-osx-x64", outputs["download-url"]);
        Assert.Equal($"{server.BaseUri}downloads/dotnet-install-0.2.0-osx-x64.sha256", outputs["sha256-url"]);
    }

    [Fact]
    public async Task ResolveScript_LocalMode_UsesLinuxMuslAsset_OnAlpineHost()
    {
        using var tempDir = new TemporaryDirectory();
        using var server = new TestHttpServer();
        var outputPath = tempDir.GetPath("github-output.txt");
        var osReleasePath = tempDir.GetPath("os-release");
        File.WriteAllText(outputPath, string.Empty);
        File.WriteAllText(osReleasePath, "ID=alpine");

        server.AddJson(
            "/repos/WeihanLi/dotnet-install/releases/latest",
            $$"""
            {
              "tag_name": "0.2.0",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "dotnet-install-0.2.0-linux-musl-x64",
                  "browser_download_url": "{{server.BaseUri}}downloads/dotnet-install-0.2.0-linux-musl-x64"
                },
                {
                  "name": "dotnet-install-0.2.0-linux-musl-x64.sha256",
                  "browser_download_url": "{{server.BaseUri}}downloads/dotnet-install-0.2.0-linux-musl-x64.sha256"
                }
              ]
            }
            """);

        var result = await ActionScriptTestHost.RunPowerShellFileAsync(
            ActionScriptTestHost.ResolveScriptPath(@"scripts\github-actions\Resolve-SetupDotNetInstallAction.ps1"),
            [
                "-Version", "10.0.x",
                "-RunnerOs", "Linux",
                "-RunnerArch", "X64",
                "-TempDirectory", tempDir.Path
            ],
            new Dictionary<string, string?>
            {
                ["GITHUB_OUTPUT"] = outputPath,
                ["DOTNET_INSTALL_ACTION_GITHUB_API_BASE_URL"] = server.BaseUri.ToString().TrimEnd('/'),
                ["DOTNET_INSTALL_ACTION_OS_RELEASE_PATH"] = osReleasePath,
                ["GITHUB_TOKEN"] = string.Empty
            });

        Assert.True(
            result.ExitCode == 0,
            $"ExitCode={result.ExitCode}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}");
        var outputs = ActionScriptTestHost.ParseGitHubOutputFile(outputPath);
        Assert.Equal($"{server.BaseUri}downloads/dotnet-install-0.2.0-linux-musl-x64", outputs["download-url"]);
        Assert.Equal($"{server.BaseUri}downloads/dotnet-install-0.2.0-linux-musl-x64.sha256", outputs["sha256-url"]);
        Assert.Contains("RuntimeIdentifier='linux-musl-x64'", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallScript_MultilineVersions_InstallsUniqueResolvedVersions_AndWritesMultilineOutput()
    {
        using var tempDir = new TemporaryDirectory();
        using var server = new TestHttpServer();
        var outputPath = tempDir.GetPath("github-output.txt");
        var envPath = tempDir.GetPath("github-env.txt");
        var pathFile = tempDir.GetPath("github-path.txt");
        File.WriteAllText(outputPath, string.Empty);
        File.WriteAllText(envPath, string.Empty);
        File.WriteAllText(pathFile, string.Empty);

        const string fakeToolName = "fake-tool.ps1";
        var fakeToolContent = """
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArgs)

$dryRun = $false
$version = $null
$installDir = $null

for ($i = 0; $i -lt $RemainingArgs.Count; $i++) {
    switch ($RemainingArgs[$i]) {
        '--dry-run' { $dryRun = $true }
        '--version' { $i++; $version = $RemainingArgs[$i] }
        '--install-dir' { $i++; $installDir = $RemainingArgs[$i] }
    }
}

$resolvedVersion = switch ($version) {
    '10.0.x' { '10.0.100' }
    '10.0.100' { '10.0.100' }
    '9.0.x' { '9.0.200' }
    default { $version }
}

if ($dryRun) {
    Write-Output "Product: Sdk $resolvedVersion | RID: win-x64 | Preview: False"
    exit 0
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
$versionsFile = Join-Path $installDir 'installed-sdks.txt'
if (-not (Test-Path $versionsFile)) {
    Set-Content -LiteralPath $versionsFile -Value '' -NoNewline
}

$existingVersions = @(Get-Content -LiteralPath $versionsFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($existingVersions -notcontains $resolvedVersion) {
    Add-Content -LiteralPath $versionsFile -Value $resolvedVersion
}

$dotnetPath = Join-Path $installDir 'dotnet.ps1'
$dotnetScript = @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArgs)

if ($RemainingArgs.Count -gt 0 -and $RemainingArgs[0] -eq '--list-sdks') {
    $versionsFile = Join-Path $PSScriptRoot 'installed-sdks.txt'
    if (Test-Path $versionsFile) {
        Get-Content -LiteralPath $versionsFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
            "$_ [$PSScriptRoot]"
        }
    }

    exit 0
}

throw "Unsupported fake dotnet command: $($RemainingArgs -join ' ')"
'@

Set-Content -LiteralPath $dotnetPath -Value $dotnetScript
exit 0
""";

        var fakeToolBytes = Encoding.UTF8.GetBytes(fakeToolContent);
        var shaFileContent = ActionScriptTestHost.CreateSha256FileContent(fakeToolBytes, fakeToolName);
        server.AddBinary("/downloads/fake-tool.ps1", fakeToolBytes, contentType: "text/plain");
        server.AddText("/downloads/fake-tool.ps1.sha256", shaFileContent);

        var result = await ActionScriptTestHost.RunPowerShellFileAsync(
            ActionScriptTestHost.ResolveScriptPath(@"scripts\github-actions\Install-DotNetSdkFromRelease.ps1"),
            [
                "-RequestedVersion", $"10.0.x{Environment.NewLine}9.0.x{Environment.NewLine}10.0.100",
                "-InstallDir", tempDir.GetPath("install"),
                "-ToolPath", tempDir.GetPath("downloaded", fakeToolName),
                "-DownloadUrl", $"{server.BaseUri}downloads/fake-tool.ps1",
                "-Sha256Url", $"{server.BaseUri}downloads/fake-tool.ps1.sha256",
                "-RunnerOs", "Windows"
            ],
            new Dictionary<string, string?>
            {
                ["GITHUB_OUTPUT"] = outputPath,
                ["GITHUB_ENV"] = envPath,
                ["GITHUB_PATH"] = pathFile,
                ["DOTNET_INSTALL_ACTION_DOTNET_EXECUTABLE_NAME"] = "dotnet.ps1",
                ["GITHUB_TOKEN"] = string.Empty
            });

        Assert.Equal(0, result.ExitCode);

        var outputs = ActionScriptTestHost.ParseGitHubOutputFile(outputPath);
        Assert.Equal($"10.0.100{Environment.NewLine}9.0.200", outputs["resolved-version"]);
        Assert.Equal(tempDir.GetPath("install"), outputs["install-dir"]);
        Assert.Equal(tempDir.GetPath("install", "dotnet.ps1"), outputs["dotnet-path"]);
        Assert.Contains("Skipping duplicate install.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Verified installed SDK '10.0.100'.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Verified installed SDK '9.0.200'.", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallScript_SingleVersionInput_HandlesScalarSplitResult()
    {
        using var tempDir = new TemporaryDirectory();
        using var server = new TestHttpServer();
        var outputPath = tempDir.GetPath("github-output.txt");
        var envPath = tempDir.GetPath("github-env.txt");
        var pathFile = tempDir.GetPath("github-path.txt");
        File.WriteAllText(outputPath, string.Empty);
        File.WriteAllText(envPath, string.Empty);
        File.WriteAllText(pathFile, string.Empty);

        const string fakeToolName = "fake-tool.ps1";
        var fakeToolContent = """
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArgs)

$dryRun = $false
$version = $null
$installDir = $null

for ($i = 0; $i -lt $RemainingArgs.Count; $i++) {
    switch ($RemainingArgs[$i]) {
        '--dry-run' { $dryRun = $true }
        '--version' { $i++; $version = $RemainingArgs[$i] }
        '--install-dir' { $i++; $installDir = $RemainingArgs[$i] }
    }
}

$resolvedVersion = if ($version -eq '10.0.x') { '10.0.100' } else { $version }

if ($dryRun) {
    Write-Output "Product: Sdk $resolvedVersion | RID: win-x64 | Preview: False"
    exit 0
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Set-Content -LiteralPath (Join-Path $installDir 'installed-sdks.txt') -Value $resolvedVersion
$dotnetPath = Join-Path $installDir 'dotnet.ps1'
$dotnetScript = @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArgs)
if ($RemainingArgs.Count -gt 0 -and $RemainingArgs[0] -eq '--list-sdks') {
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installed-sdks.txt') | ForEach-Object { "$_ [$PSScriptRoot]" }
    exit 0
}
throw "Unsupported fake dotnet command"
'@
Set-Content -LiteralPath $dotnetPath -Value $dotnetScript
exit 0
""";

        var fakeToolBytes = Encoding.UTF8.GetBytes(fakeToolContent);
        var shaFileContent = ActionScriptTestHost.CreateSha256FileContent(fakeToolBytes, fakeToolName);
        server.AddBinary("/downloads/fake-tool.ps1", fakeToolBytes, contentType: "text/plain");
        server.AddText("/downloads/fake-tool.ps1.sha256", shaFileContent);

        var result = await ActionScriptTestHost.RunPowerShellFileAsync(
            ActionScriptTestHost.ResolveScriptPath(@"scripts\github-actions\Install-DotNetSdkFromRelease.ps1"),
            [
                "-RequestedVersion", "10.0.x",
                "-InstallDir", tempDir.GetPath("install"),
                "-ToolPath", tempDir.GetPath("downloaded", fakeToolName),
                "-DownloadUrl", $"{server.BaseUri}downloads/fake-tool.ps1",
                "-Sha256Url", $"{server.BaseUri}downloads/fake-tool.ps1.sha256",
                "-RunnerOs", "Windows"
            ],
            new Dictionary<string, string?>
            {
                ["GITHUB_OUTPUT"] = outputPath,
                ["GITHUB_ENV"] = envPath,
                ["GITHUB_PATH"] = pathFile,
                ["DOTNET_INSTALL_ACTION_DOTNET_EXECUTABLE_NAME"] = "dotnet.ps1",
                ["GITHUB_TOKEN"] = string.Empty
            });

        Assert.True(
            result.ExitCode == 0,
            $"ExitCode={result.ExitCode}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}");
        var outputs = ActionScriptTestHost.ParseGitHubOutputFile(outputPath);
        Assert.Equal("10.0.100", outputs["resolved-version"]);
        Assert.Contains("Requested version count=1", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Verified installed SDK '10.0.100'.", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallScript_GlobalJsonInput_InstallsSdkVersionFromGlobalJson()
    {
        using var tempDir = new TemporaryDirectory();
        using var server = new TestHttpServer();
        var outputPath = tempDir.GetPath("github-output.txt");
        var envPath = tempDir.GetPath("github-env.txt");
        var pathFile = tempDir.GetPath("github-path.txt");
        var workspacePath = tempDir.GetPath("workspace");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(outputPath, string.Empty);
        File.WriteAllText(envPath, string.Empty);
        File.WriteAllText(pathFile, string.Empty);
        File.WriteAllText(tempDir.GetPath("workspace", "global.json"), """
        {
          "sdk": {
            "version": "10.0.321"
          }
        }
        """);

        const string fakeToolName = "fake-tool.ps1";
        var fakeToolContent = """
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArgs)

$dryRun = $false
$globalJsonFile = $null
$installDir = $null

for ($i = 0; $i -lt $RemainingArgs.Count; $i++) {
    switch ($RemainingArgs[$i]) {
        '--dry-run' { $dryRun = $true }
        '--jsonfile' { $i++; $globalJsonFile = $RemainingArgs[$i] }
        '--install-dir' { $i++; $installDir = $RemainingArgs[$i] }
    }
}

if ([string]::IsNullOrWhiteSpace($globalJsonFile)) {
    throw 'Expected --jsonfile to be passed to the tool.'
}

$resolvedVersion = ((Get-Content -LiteralPath $globalJsonFile -Raw) | ConvertFrom-Json).sdk.version

if ($dryRun) {
    Write-Output "Product: Sdk $resolvedVersion | RID: win-x64 | Preview: False"
    exit 0
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Set-Content -LiteralPath (Join-Path $installDir 'installed-sdks.txt') -Value $resolvedVersion
$dotnetPath = Join-Path $installDir 'dotnet.ps1'
$dotnetScript = @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$RemainingArgs)
if ($RemainingArgs.Count -gt 0 -and $RemainingArgs[0] -eq '--list-sdks') {
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installed-sdks.txt') | ForEach-Object { "$_ [$PSScriptRoot]" }
    exit 0
}
throw "Unsupported fake dotnet command"
'@
Set-Content -LiteralPath $dotnetPath -Value $dotnetScript
exit 0
""";

        var fakeToolBytes = Encoding.UTF8.GetBytes(fakeToolContent);
        var shaFileContent = ActionScriptTestHost.CreateSha256FileContent(fakeToolBytes, fakeToolName);
        server.AddBinary("/downloads/fake-tool.ps1", fakeToolBytes, contentType: "text/plain");
        server.AddText("/downloads/fake-tool.ps1.sha256", shaFileContent);

        var result = await ActionScriptTestHost.RunPowerShellFileAsync(
            ActionScriptTestHost.ResolveScriptPath(@"scripts\github-actions\Install-DotNetSdkFromRelease.ps1"),
            [
                "-GlobalJsonFile", "global.json",
                "-InstallDir", tempDir.GetPath("install"),
                "-ToolPath", tempDir.GetPath("downloaded", fakeToolName),
                "-DownloadUrl", $"{server.BaseUri}downloads/fake-tool.ps1",
                "-Sha256Url", $"{server.BaseUri}downloads/fake-tool.ps1.sha256",
                "-RunnerOs", "Windows"
            ],
            new Dictionary<string, string?>
            {
                ["GITHUB_OUTPUT"] = outputPath,
                ["GITHUB_ENV"] = envPath,
                ["GITHUB_PATH"] = pathFile,
                ["GITHUB_WORKSPACE"] = workspacePath,
                ["DOTNET_INSTALL_ACTION_DOTNET_EXECUTABLE_NAME"] = "dotnet.ps1",
                ["GITHUB_TOKEN"] = string.Empty
            });

        Assert.True(
            result.ExitCode == 0,
            $"ExitCode={result.ExitCode}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}");
        var outputs = ActionScriptTestHost.ParseGitHubOutputFile(outputPath);
        Assert.Equal("10.0.321", outputs["resolved-version"]);
        Assert.Contains("ResolvedGlobalJsonFile=", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Verified installed SDK '10.0.321'.", result.StdOut, StringComparison.Ordinal);
    }
}
