# dotnet-install ![Build](https://github.com/WeihanLi/dotnet-install/actions/workflows/build.yml/badge.svg)

Managed prototype of the `dotnet-install` shell scripts.

This repository contains a .NET 10 command-line tool named `dotnet-install` that resolves .NET release metadata, builds an install plan, downloads the selected SDK or runtime artifact, extracts it into the target install root, verifies the result, and can update `PATH`. The goal is to preserve the behavior of `dotnet-install.sh` and `dotnet-install.ps1` while moving the implementation to managed code and NativeAOT-friendly settings.

## Status

The current implementation is usable for:

- Command-line parsing and validation with `System.CommandLine`
- Channel, quality, exact version, and wildcard version selection
- Existing-install detection with confirmation prompts
- Release metadata lookup
- RID resolution and candidate URL generation
- Artifact download, extraction, verification, and feed/proxy override options
- PATH updates for the current process, plus optional user PATH persistence on Windows
- Removal of installed SDK/runtime folders and related assets under the install root

Current behavior boundaries:

- `--dry-run` stops after install plan generation
- Non-dry-run install downloads the archive, extracts it into the resolved install root, verifies the installed SDK/runtime folder, and updates `PATH` for the current process unless `--no-path` is set
- If the requested SDK/runtime version is already installed, the command warns and asks for confirmation unless `--yes` is set. In CI, `--yes` defaults to `true`
- `--persist-path` also prepends the install root to the user `PATH` for new shells on Windows. It cannot be combined with `--no-path`, and it is not supported on non-Windows platforms
- If another existing .NET installation is already discoverable from known locations, PATH mutation is skipped to avoid shadowing that install
- `remove` infers whether `<version>` is an SDK version or a runtime version. SDK input removes `sdk/<sdk-version>` and its matching SDK `swidtag`, and unless `--sdk-only` is set also removes the corresponding runtime folders and companion assets when metadata resolution succeeds. Runtime input removes only the matching runtime folders and runtime-version companion assets. If SDK-to-runtime metadata cannot be resolved, the command logs that the runtime version must be removed separately. `--dry-run` lists the matching folders without deleting them

## Requirements

- .NET 10 SDK or newer
- A recent `dotnet` CLI with `.slnx` support

Verify the environment:

```sh
dotnet --list-sdks
dotnet --list-runtimes
```

## Getting Started

Build the solution:

```sh
dotnet build DotNetInstallManager.slnx
```

Run the CLI help:

```sh
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --help
```

Show the tool version:

```sh
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- version
```

Generate an install plan without downloading:

```sh
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --dry-run --channel LTS
```

Select the latest matching SDK from a version train:

```sh
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --version 10.0.x
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

```sh
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --channel LTS --version Latest
```

Install and persist the location into the user `PATH` on Windows:

```sh
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --channel LTS --persist-path
```

Preview the planned removal of an SDK:

```sh
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- remove 8.0.204
```

## Command Surface

The root command mirrors the install flow from the shell scripts. Supported options include:

- `--channel`
- `--quality`
- `--version`
- `--internal`
- `--runtime`
- `--shared-runtime`
- `--jsonfile`
- `--install-dir`
- `--architecture`
- `--dry-run`
- `--yes`
- `--no-path`
- `--persist-path`
- `--azure-feed`
- `--uncached-feed`
- `--feed-credential`
- `--proxy-address`
- `--proxy-use-default-credentials`
- `--proxy-bypass-list`
- `--skip-non-versioned-files`
- `--download-timeout`
- `--keep-zip`
- `--zip-path`
- `--runtime-id`
- `--os`
- `--verbose`

Notes:

- `--version` is reserved for the .NET SDK/runtime version to install
- Use the `version` subcommand to print the tool version
- `--quality` can only be combined with `--version Latest`
- Exact versions such as `8.0.205` are supported, and wildcard selectors such as `8.x` and `8.0.x` resolve to the latest matching release
- `--yes` skips the existing-install confirmation prompt, and defaults to `true` when the `CI` environment variable is set
- `--persist-path` is supported only on Windows and cannot be combined with `--no-path`

The `remove` subcommand currently accepts:

- A required `<version>` argument
- `--install-dir`
- `--sdk-only`
- `--dry-run`
- `--verbose`

## Build, Test, Pack

Build and test the full solution:

```sh
dotnet build DotNetInstallManager.slnx
dotnet test DotNetInstallManager.slnx
```

Create the global tool package:

```sh
dotnet pack src/DotNetInstallManager/DotNetInstallManager.csproj -c Release
```

Publish a self-contained build to `dist`:

```sh
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
