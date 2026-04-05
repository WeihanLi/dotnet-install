# dotnet-install ![Build](https://github.com/WeihanLi/dotnet-install/actions/workflows/build.yml/badge.svg) [![NuGet Latest](https://img.shields.io/nuget/v/spark.dotnet-install)](https://www.nuget.org/packages/spark.dotnet-install)

Managed implementation of the `dotnet-install` shell scripts.

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

## Install The Tool

You can use `dotnet-install` in two supported ways:

- As a .NET global or local tool from NuGet
- As a single native executable downloaded from a GitHub release

### Option 1: install from NuGet

Install globally:

```sh
dotnet tool install --global spark.dotnet-install
```

Install into a local tool manifest:

```sh
dotnet new tool-manifest
dotnet tool install --local spark.dotnet-install
```

Update an existing global install:

```sh
dotnet tool update --global spark.dotnet-install
```

Run it:

```sh
dotnet-install --help
dotnet-install version
```

### Option 2: download a GitHub release binary

Each GitHub release publishes a single-file executable per RID:

- `dotnet-install-<version>-win-x64.exe`
- `dotnet-install-<version>-win-arm64.exe`
- `dotnet-install-<version>-linux-x64`
- `dotnet-install-<version>-linux-arm64`
- `dotnet-install-<version>-osx-arm64`

Download the asset that matches the target machine, place it in a directory of your choice, and run it directly. On Linux and macOS, mark it executable first:

```sh
mv ./dotnet-install-<version>-<rid> ./dotnet-install
chmod +x ./dotnet-install
./dotnet-install --help
./dotnet-install version
```

If you want to invoke the release binary as `dotnet-install` from any shell, rename it to `dotnet-install` (or `dotnet-install.exe` on Windows) and place that file in a directory that is already on your shell `PATH`.

See [docs/github-releases.md](docs/github-releases.md) for a release-first setup guide.
See [docs/releasing.md](docs/releasing.md) for the maintainer release checklist.

## GitHub Action

This repository also ships a first-party composite action at [action.yml](action.yml) for workflows that want to install an SDK through this repo's managed `dotnet-install` release binary instead of `actions/setup-dotnet`.

When the action is used from a release tag such as `@v1`, it downloads the matching release binary for that action version. If it is used from a branch or local path, it falls back to the latest published GitHub release.

Example usage:

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v6
      - uses: WeihanLi/dotnet-install@v1
        id: setup-dotnet
        with:
          version: 10.0.x
      - run: dotnet --info
```

Action inputs:

- `version` required SDK version selector such as `10.0.201` or `10.0.x`
- `install-dir` optional SDK install location
- `cache` optional `true` or `false`, defaults to `true`

Action outputs:

- `resolved-version`
- `install-dir`
- `dotnet-root`
- `dotnet-path`

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
dotnet-install --help
```

Show the tool version:

```sh
dotnet-install version
```

Generate an install plan without downloading:

```sh
dotnet-install --dry-run --channel LTS
```

Select the latest matching SDK from a version train:

```sh
dotnet-install --version 10.0.x
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
dotnet-install --version 11.0.x
```

Install and persist the location into the user `PATH` on Windows:

```sh
dotnet-install --channel LTS --persist-path
```

Preview the planned removal of an SDK:

```sh
dotnet-install remove 8.0.204 --dry-run
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

## PATH Behavior

`dotnet-install` manages two different PATH concerns:

- The PATH used to locate the `dotnet-install` tool itself
- The PATH used to locate the SDK/runtime installation root after the tool completes

Tool acquisition from NuGet or GitHub Releases does not automatically change PATH for you. Put the tool binary in a directory that is already on PATH, or invoke it by full path.

For SDK/runtime installs, the command behaves as follows:

- By default it prepends the resolved install root to the current process `PATH`
- `--no-path` disables all PATH mutation and prints the install location instead
- `--persist-path` also prepends the install root to the user `PATH` for future shells on Windows
- `--persist-path` is rejected on non-Windows platforms and cannot be combined with `--no-path`
- PATH updates are skipped entirely when an existing .NET installation is already discoverable from the selected install root, `DOTNET_INSTALL_DIR`, `DOTNET_ROOT`, the current `dotnet` command location, or well-known default install roots
- When PATH updates are skipped, installation still succeeds; the command prints the install location instead of shadowing another .NET installation

## Remove Safety

The `remove` command is destructive. Use `--dry-run` first when targeting a shared install root.

Current safety rules:

- Removal targets are resolved relative to the install root and rejected if they escape that root
- `remove` does not modify process PATH, user PATH, shell profiles, or system-wide environment configuration
- SDK removal starts with `sdk/<sdk-version>` and matching `swidtag` files
- Unless `--sdk-only` is set, SDK removal can also remove matching runtime folders and companion assets such as `host/fxr`, `shared`, `packs`, `templates`, workload metadata, and sdk-manifest entries
- When other SDK versions are still installed, shared companion targets are filtered so the command does not remove assets still referenced by those remaining SDK bands
- Runtime-version input removes only runtime-related folders and companion assets for that runtime version
- If SDK-to-runtime metadata cannot be resolved, the command warns and removes only SDK-specific paths
- On Windows, permission failures can trigger a retry with administrator elevation
- On Linux, permission failures include a `sudo dotnet-install remove <version>` hint
- If no matching SDK or runtime paths are found under the install root, the command fails instead of silently succeeding

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
