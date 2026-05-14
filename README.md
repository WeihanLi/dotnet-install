# dotnet-install ![Build](https://github.com/WeihanLi/dotnet-install/actions/workflows/build.yml/badge.svg) [![NuGet Latest](https://img.shields.io/nuget/v/spark.dotnet-install)](https://www.nuget.org/packages/spark.dotnet-install)

Managed `dotnet-install` for developers who want the familiar shell-script workflow in a compiled, NativeAOT-oriented CLI.

`dotnet-install` resolves .NET release metadata, builds an install plan, downloads the selected SDK or runtime artifact, and runs the install flow from a single command. The project aims to stay close to `dotnet-install.sh` and `dotnet-install.ps1` while making the developer experience easier to inspect, test, and automate.

## Install The Tool

You can use `dotnet-install` in two supported ways:

- As a .NET global or local tool from NuGet
- As a single native executable downloaded from a GitHub release

### Option 1: Install From NuGet

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

### Option 2: Download A GitHub Release Binary

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

## Get Started

Install the tool:

```sh
dotnet tool install --global spark.dotnet-install
```

Then use the commands most developers need first:

```sh
# Explore the CLI
dotnet-install --help

# See the tool version
dotnet-install version

# Preview what would be installed
dotnet-install --dry-run --channel LTS

# Install the latest SDK in a feature band
dotnet-install --version 10.0.x

# Upgrade an installed SDK band and remove obsolete versions in that channel
dotnet-install upgrade 10.0.x --dry-run

# Upgrade multiple SDK bands in one command
dotnet-install upgrade 10.0.x 11.0.x

# Update the dotnet-install executable itself from GitHub releases
dotnet-install self-update --dry-run

# Include preview GitHub releases when self-updating
dotnet-install self-update --prerelease
```

![dotnet-install upgrade sample](https://github.com/user-attachments/assets/4de085c0-c1bf-437a-9db3-3091573d75bd)

If you prefer a standalone executable instead of a .NET tool, jump to [Install The Tool](#install-the-tool).

## Why Developers Use It

- Familiar `dotnet-install` semantics without depending on shell scripts
- Strong CLI parsing and validation powered by `System.CommandLine`
- Flexible version selection with channels, exact versions, and wildcard bands
- Release metadata resolution and candidate URL planning you can inspect with `--dry-run`
- Proxy, feed, and timeout controls for CI, enterprise networks, and custom feeds
- NativeAOT-oriented packaging for fast startup and simple distribution

## Common Tasks

Install the latest LTS SDK:

```sh
dotnet-install --channel LTS
```

Install the latest SDK in a specific feature band:

```sh
dotnet-install --version 10.0.x
```

Install a specific SDK exactly:

```sh
dotnet-install --version 10.0.201
```

Install only a runtime:

```sh
dotnet-install --runtime aspnetcore --version 10.0.x
```

Preview the plan without downloading:

```sh
dotnet-install --dry-run --channel STS
```

Install into a custom location:

```sh
dotnet-install --version 10.0.x --install-dir ./tools/dotnet
```

Keep the downloaded archive for inspection or reuse:

```sh
dotnet-install --version 10.0.x --keep-zip
```

Preview removal before deleting anything:

```sh
dotnet-install remove 8.0.204 --dry-run
```

Preview an SDK upgrade before installing/removing anything:

```sh
dotnet-install upgrade 10.0.x --dry-run
```

Upgrade multiple SDK bands in one command:

```sh
dotnet-install upgrade 10.0.x 11.0.x
```

Upgrade an SDK and keep the related runtime installed:

```sh
dotnet-install upgrade 10.0.x --sdk-only
```

Upgrade only the .NET runtime in a channel:

```sh
dotnet-install upgrade 10.0.x --runtime
```

Preview a self-update from GitHub releases:

```sh
dotnet-install self-update --dry-run
```

Include preview releases when self-updating:

```sh
dotnet-install self-update --prerelease
```

## GitHub Action

This repository also ships a first-party composite action at [action.yml](action.yml) for workflows that want to install an SDK through this repo's managed `dotnet-install` release binary instead of `actions/setup-dotnet`.

When the action is used from a release tag such as `@v1`, it downloads the matching release binary for that action version. If it is used from a branch or local path, it resolves the latest published release asset from `WeihanLi/dotnet-install` and uses that binary with the local action scripts. The resolver prefers the latest stable release and falls back to the latest published prerelease only when no stable release exists yet.

Example:

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v6
      - uses: WeihanLi/dotnet-install@v0.2.0
        with:
          version: 10.0.x
      - run: dotnet --info
```

To install multiple SDK bands in one action step, pass a newline-delimited `version` value:

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v6
      - uses: WeihanLi/dotnet-install@v0.2.0
        id: setup-dotnet
        with:
          version: |
            10.0.x
            11.0.x
      - run: dotnet --list-sdks
```

Action inputs:

- `version` required SDK version selector such as `10.0.201`, `10.0.x`, or a newline-delimited list of selectors
- `install-dir` optional SDK install location

Action outputs:

- `resolved-version` single resolved SDK version or a newline-delimited list in request order
- `install-dir`
- `dotnet-root`
- `dotnet-path`

## Status

The project is already useful for developer workstations, CI setup, and install-plan inspection:

- Command-line parsing and validation with `System.CommandLine`
- Channel, quality, exact version, and wildcard version selection
- Release metadata lookup and candidate URL generation
- Artifact download, extraction, install verification, and feed/proxy override options
- Existing-install detection with confirmation prompts
- PATH updates for the current process, plus optional user PATH persistence on Windows
- Removal planning and deletion of installed SDK/runtime assets under the selected install root

Parity with the original shell scripts is still in progress, so behavior should continue to align with `dotnet-install.sh` and `dotnet-install.ps1` where possible.

Current behavior boundaries:

- `--dry-run` stops after install plan generation
- `--quality` can only be combined with `--version Latest`
- If the requested SDK/runtime version is already installed, the command warns and asks for confirmation unless `--yes` is set
- In CI, `--yes` defaults to `true`
- `--persist-path` is supported only on Windows and cannot be combined with `--no-path`
- If another existing .NET installation is already discoverable from known locations, PATH mutation is skipped to avoid shadowing that install
- `remove` is destructive and should be previewed with `--dry-run` first when targeting a shared install root
- `upgrade` resolves the requested SDK/runtime version, skips installation when that resolved version is already present, and removes other installed versions in the same major.minor channel
- SDK upgrades remove the related runtime by default when pruning obsolete SDKs; use `upgrade --sdk-only` to keep the runtime installed
- `self-update` replaces the current executable with the latest matching GitHub release asset for the current RID
- `self-update --prerelease` includes preview releases, and prerelease builds enable that behavior by default

## Command Surface

The root command mirrors the install flow from the shell scripts. Frequently used options include:

- `--channel`
- `--quality`
- `--version`
- `--runtime`
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
- Exact versions such as `8.0.205` are supported, and wildcard selectors such as `8.x` and `8.0.x` resolve to the latest matching release
- `--yes` skips the existing-install confirmation prompt

The `remove` subcommand currently accepts:

- A required `<version>` argument
- `--install-dir`
- `--sdk-only`
- `--dry-run`
- `--verbose`

The `upgrade` subcommand currently accepts:

- One or more required `<version>` arguments such as `10.0.201`, `10.0.x`, or `10.0.x 11.0.x`
- `--runtime` to upgrade only the .NET runtime
- `--sdk-only` to keep the related runtime when upgrading an SDK
- `--install-dir`
- `--dry-run`

The `self-update` subcommand currently accepts:

- `--dry-run`
- `--prerelease`

## PATH Behavior

`dotnet-install` manages two different PATH concerns:

- The PATH used to locate the `dotnet-install` tool itself
- The PATH used to locate the SDK/runtime installation root after the tool completes

Tool acquisition from NuGet or GitHub Releases does not automatically change PATH for you. Put the tool binary in a directory that is already on PATH, or invoke it by full path.

If you installed the tool from a GitHub release binary, `dotnet-install self-update` updates that executable in place. If you installed from NuGet, prefer `dotnet tool update`.

For SDK/runtime installs, the command behaves as follows:

- By default it prepends the resolved install root to the current process `PATH`
- `--no-path` disables all PATH mutation and prints the install location instead
- `--persist-path` also prepends the install root to the user `PATH` for future shells on Windows
- PATH updates are skipped entirely when an existing .NET installation is already discoverable from the selected install root, `DOTNET_INSTALL_DIR`, `DOTNET_ROOT`, the current `dotnet` command location, or well-known default install roots

## Requirements

.NET 10 SDK or newer for building from source

Verify the environment:

```sh
dotnet --list-sdks
```

## Build, Test, Pack

Build and test the full solution:

```sh
dotnet build DotNetInstall.slnx
dotnet test DotNetInstall.slnx
```

Create the global tool package:

```sh
dotnet pack src/DotNetInstall/DotNetInstall.csproj -c Release
```

Publish a self-contained build to `dist`:

```sh
dotnet publish src/DotNetInstall/DotNetInstall.csproj -c Release -f net10.0 --use-current-runtime -o dist
```

## Repository Layout

- `src/DotNetInstall/` application code
- `src/DotNetInstall/Application/` startup and host wiring
- `src/DotNetInstall/Cli/` commands and options
- `src/DotNetInstall/Options/` immutable option models
- `src/DotNetInstall/Services/` metadata resolution, planning, downloading, extraction, and orchestration
- `tests/DotNetInstall.Tests/` unit tests
- `dotnet-install.sh` and `dotnet-install.ps1` reference shell-script behavior

## Additional Docs

- [docs/github-releases.md](docs/github-releases.md)
- [docs/releasing.md](docs/releasing.md)
