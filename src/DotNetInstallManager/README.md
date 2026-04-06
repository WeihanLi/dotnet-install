# dotnet-install

`dotnet-install` is a .NET global tool that aims to mirror the behavior of `dotnet-install.sh` and `dotnet-install.ps1` in managed code.

The tool currently does these parts well:

- Binds and validates CLI options with `System.CommandLine`
- Resolves .NET release metadata
- Builds an install plan for SDK and runtime requests
- Downloads, extracts, and verifies the selected SDK or runtime archive
- Supports feed, proxy, and timeout overrides
- Updates the current process `PATH`, with optional user PATH persistence on Windows
- Removes installed SDK/runtime folders and companion assets under the install root

Current limitations:

- `--dry-run` stops after plan generation
- User PATH persistence is supported only on Windows
- PATH updates are skipped when another existing .NET installation is already discoverable, to avoid shadowing it
- `remove` is destructive and should be previewed with `--dry-run` first when targeting a shared install root

## Install

Install as a global tool:

```bash
dotnet tool install --global spark.dotnet-install
```

Install into a local tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install --local spark.dotnet-install
```

## Usage

Show help:

```bash
dotnet-install --help
```

Print the tool version:

```bash
dotnet-install version
```

Generate an install plan without downloading:

```bash
dotnet-install --dry-run --channel LTS
```

Download the selected archive:

```bash
dotnet-install --version 10.0.x
```

Preview a remove plan:

```bash
dotnet-install remove 8.0.204 --dry-run
```

## Project

- Package: `spark.dotnet-install`
- Repository: `https://github.com/WeihanLi/dotnet-install`

See the repository README for source, development, and release details.
