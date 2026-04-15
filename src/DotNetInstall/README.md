# dotnet-install

Managed `dotnet-install` for developers who want the familiar shell-script workflow in a compiled CLI.

The tool resolves .NET release metadata, builds an install plan, downloads the selected SDK or runtime artifact, and runs the install flow from a single command.

## Get Started

Install as a global tool:

```bash
dotnet tool install --global spark.dotnet-install
```

Or install into a local tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install --local spark.dotnet-install
```

Then start with the commands most people need:

```bash
dotnet-install --help
dotnet-install version
dotnet-install --dry-run --channel LTS
dotnet-install --version 10.0.x
dotnet-install upgrade 10.0.x --dry-run
dotnet-install self-update --dry-run
```

## Common Tasks

Install the latest LTS SDK:

```bash
dotnet-install --channel LTS
```

Install a specific SDK exactly:

```bash
dotnet-install --version 10.0.201
```

Install only a runtime:

```bash
dotnet-install --runtime aspnetcore --version 10.0.x
```

Install into a custom directory:

```bash
dotnet-install --version 10.0.x --install-dir ./tools/dotnet
```

Preview a removal before deleting anything:

```bash
dotnet-install remove 8.0.204 --dry-run
```

Preview an upgrade before installing/removing anything:

```bash
dotnet-install upgrade 10.0.x --dry-run
```

Upgrade an SDK but keep its related runtime packs and shared runtime:

```bash
dotnet-install upgrade 10.0.x --sdk-only
```

## Highlights

- Familiar `dotnet-install` semantics in managed code
- Strong option binding and validation with `System.CommandLine`
- Channel, exact-version, and wildcard version selection
- Download, extraction, install verification, and feed/proxy override support
- PATH updates for the current process, with optional user PATH persistence on Windows

## Notes

- `--dry-run` stops after plan generation
- `--persist-path` is supported only on Windows and cannot be combined with `--no-path`
- If another .NET installation is already discoverable, PATH mutation is skipped to avoid shadowing it
- `remove` is destructive and should be previewed with `--dry-run` first
- `upgrade` skips installation when the resolved version is already present and removes other installed versions in the same major.minor channel
- `upgrade` removes the related runtime by default when pruning obsolete SDKs; use `--sdk-only` to keep the runtime installed
- `self-update` replaces the current executable with the latest matching GitHub release asset for the current RID

## Project

- Package: `spark.dotnet-install`
- Repository: `https://github.com/WeihanLi/dotnet-install`

See the repository README for source, development, GitHub Action, and release details.
