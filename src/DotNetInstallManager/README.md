# dotnet-install

`dotnet-install` is a .NET global tool prototype that aims to mirror the behavior of `dotnet-install.sh` and `dotnet-install.ps1` in managed code.

The tool currently does these parts well:

- Binds and validates CLI options with `System.CommandLine`
- Resolves .NET release metadata
- Builds an install plan for SDK and runtime requests
- Downloads the selected SDK or runtime archive
- Supports feed, proxy, and timeout overrides
- Exposes a preview `remove` command surface

Current limitations:

- `--dry-run` stops after plan generation
- Non-dry-run install currently downloads the archive but does not extract or install it yet
- PATH updates are not implemented yet
- `remove` is a planning stub and does not delete installed bits yet

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
