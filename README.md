# dotnet-install ![Build](https://github.com/WeihanLi/dotnet-install/actions/workflows/build.yml/badge.svg)

Managed prototype of the `dotnet-install` shell scripts.

This repository contains a .NET 10 command-line tool named `dotnet-install` that resolves .NET release metadata, builds an install plan, and downloads the selected SDK or runtime artifact. The goal is to preserve the behavior of `dotnet-install.sh` and `dotnet-install.ps1` while moving the implementation to managed code and NativeAOT-friendly settings.

## Status

The current implementation is usable for:

- Command-line parsing and validation with `System.CommandLine`
- Channel, quality, version, and runtime selection
- Release metadata lookup
- RID resolution and candidate URL generation
- Artifact download, including proxy and feed override options
- A preview `remove` command surface for future uninstall work

These areas are still incomplete:

- Actual SDK/runtime removal
- Full parity with `dotnet-install.sh` and `dotnet-install.ps1`

Current behavior boundaries:

- `--dry-run` stops after install plan generation
- Non-dry-run install downloads the archive, extracts it into the resolved install root, verifies the installed SDK/runtime folder, and updates PATH for the current process unless `--no-path` is set
- `remove` reports intent only and does not delete installed bits

## Requirements

- .NET 10 SDK or newer
- A recent `dotnet` CLI with `.slnx` support

Verify the environment:

```powershell
dotnet --list-sdks
dotnet --list-runtimes
```

## Getting Started

Build the solution:

```powershell
dotnet build DotNetInstallManager.slnx
```

Run the CLI help:

```powershell
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --help
```

Show the tool version:

```powershell
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- version
```

Generate an install plan without downloading:

```powershell
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --dry-run --channel LTS
```

Example output:

```text
dotnet-install plan for channel 10.0 (10.0.5)
Product: Sdk 10.0.201 | RID: win-x64 | Preview: False
InstallRoot: C:\Users\<user>\AppData\Local\Microsoft\dotnet
Primary URL: https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.201/dotnet-sdk-10.0.201-win-x64.zip
Candidate URLs:
  [0] https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.201/dotnet-sdk-10.0.201-win-x64.zip
DryRun: True | KeepZip: False | ZipPath: <temp>
```

Download an SDK archive:

```powershell
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --channel LTS --version Latest
```

Preview the planned removal of an SDK:

```powershell
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- remove 8.0.204
```

## Command Surface

The root command mirrors the install flow from the shell scripts. Supported options include:

- `--channel`
- `--quality`
- `--version`
- `--runtime`
- `--jsonfile`
- `--install-dir`
- `--architecture`
- `--dry-run`
- `--azure-feed`
- `--uncached-feed`
- `--feed-credential`
- `--proxy-address`
- `--proxy-use-default-credentials`
- `--proxy-bypass-list`
- `--download-timeout`
- `--keep-zip`
- `--zip-path`
- `--verbose`

Notes:

- `--version` is reserved for the .NET SDK/runtime version to install
- Use the `version` subcommand to print the tool version
- `--quality` can only be combined with `--version Latest`

The preview `remove` subcommand currently accepts:

- A required `<version>` argument
- `--install-dir`
- `--sdk-only`
- `--verbose`

## Build, Test, Pack

Build and test the full solution:

```powershell
dotnet build DotNetInstallManager.slnx
dotnet test DotNetInstallManager.slnx
```

Create the global tool package:

```powershell
dotnet pack src/DotNetInstallManager/DotNetInstallManager.csproj -c Release
```

Publish a self-contained build to `dist`:

```powershell
dotnet publish src/DotNetInstallManager/DotNetInstallManager.csproj -c Release -f net10.0 --use-current-runtime -o dist
```

## Repository Layout

- `src/DotNetInstallManager/` application code
- `src/DotNetInstallManager/Application/` startup and host wiring
- `src/DotNetInstallManager/Cli/` commands and options
- `src/DotNetInstallManager/Options/` immutable option models
- `src/DotNetInstallManager/Services/` metadata resolution, planning, downloading, orchestration
- `tests/DotNetInstallManager.Tests/` unit tests
- `dotnet-install.sh` and `dotnet-install.ps1` reference shell-script behavior

## Implementation Notes

- The project targets `net10.0`
- `System.CommandLine` 2.0.5 powers parsing and validation
- The main tool project is configured for NativeAOT-oriented publishing
- Build output is redirected under `artifacts/`

When changing behavior, prefer matching the existing shell scripts instead of inventing new semantics.
