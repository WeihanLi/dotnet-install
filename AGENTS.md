# AGENTS.md

## Project Overview

**dotnet-install** is a managed implementation of the `dotnet-install` scripts. It exposes a CLI named `dotnet-install` that resolves .NET release metadata, builds an install plan, downloads the selected SDK or runtime artifact, extracts it into the selected install root, verifies the result, and can update PATH.

The current implementation is strongest in these areas:

- CLI option binding and validation via `System.CommandLine`
- Release metadata lookup and install plan generation
- Artifact download, extraction, and install verification
- Proxy, feed, timeout, and archive retention controls
- PATH updates for the current process, with optional user PATH persistence on Windows
- SDK/runtime removal under the selected install root
- SDK/runtime upgrade planning and pruning
- Self-update from GitHub release binaries
- First-party GitHub Action scripts for installing SDKs from release binaries

These areas are still incomplete:

- Full parity with `dotnet-install.sh` and `dotnet-install.ps1`
- `--jsonfile`, `--internal`, and `--os` are parsed compatibility switches but are not fully wired into install planning yet
- `--quality daily` is rejected today instead of being auto-resolved from release metadata

When changing behavior, prefer aligning with the existing shell scripts in the repository instead of inventing new semantics.

## Technology Stack

- C# / .NET 10
- `System.CommandLine` 2.0.5
- xUnit tests executed through `Microsoft.NET.Test.Sdk`
- NativeAOT-oriented publish settings in the main tool project

## Repository Layout

- `src/DotNetInstall/`: application code
- `src/DotNetInstall/Application/`: app startup and host wiring
- `src/DotNetInstall/Cli/`: command and option definitions
- `src/DotNetInstall/Options/`: immutable option models
- `src/DotNetInstall/Services/`: metadata resolution, planning, downloading, extraction, removal, self-update, and orchestration
- `tests/DotNetInstall.Tests/`: unit tests mirroring source areas
- `dotnet-install.sh` and `dotnet-install.ps1`: reference behavior for the original scripts
- `artifacts/`: build output; do not edit manually

## Environment Requirements

- .NET 10 SDK is required. The solution uses `.slnx`, which is not supported by older SDKs.

Verify the SDK before doing substantial work:

```bash
dotnet --list-sdks
dotnet --list-runtimes
```

## Build And Test Commands

Use the solution file at the repository root:

```bash
dotnet build DotNetInstall.slnx
dotnet test DotNetInstall.slnx
```

Useful project-level commands:

```bash
dotnet run --project src/DotNetInstall/DotNetInstall.csproj --framework net10.0 -- --help
dotnet run --project src/DotNetInstall/DotNetInstall.csproj --framework net10.0 -- remove --help
dotnet run --project src/DotNetInstall/DotNetInstall.csproj --framework net10.0 -- upgrade --help
dotnet run --project src/DotNetInstall/DotNetInstall.csproj --framework net10.0 -- self-update --help
dotnet run --project src/DotNetInstall/DotNetInstall.csproj --framework net10.0 -- version
dotnet pack src/DotNetInstall/DotNetInstall.csproj -c Release
dotnet publish src/DotNetInstall/DotNetInstall.csproj -c Release -f net10.0 --use-current-runtime -o dist
```

Notes:

- `dotnet pack` produces the global-tool package for `dotnet-install`
- `dotnet publish` is the right validation path for publish-related changes
- Build output goes under `artifacts/` because of the root `Directory.Build.props`

## Architecture Notes

### CLI Layer

- `InstallCommandBuilder` defines the root install flow, the `remove`, `upgrade`, `self-update`, and `version` subcommands.
- The built-in `System.CommandLine` `--version` option is deliberately removed so `--version` can mean `.NET version to install`.
- If you touch command parsing or option aliases, add or update tests in `tests/DotNetInstall.Tests/Cli/`.

### Host Layer

- `Program.cs` only handles Ctrl+C cancellation.
- `DotNetInstallHost` composes the orchestrator and command tree.

### Service Layer

- `InstallPlanBuilder` resolves channels, versions, product kind, RID, and candidate download URLs.
- `ReleaseMetadataClient` reads the release index and per-channel metadata.
- `ArtifactDownloader` downloads the selected asset once a plan exists.
- `ArchiveExtractor` extracts `.zip`, `.tar`, and `.tar.gz` archives while preserving versioned install layout semantics.
- `InstallVerifier` checks that the expected SDK or runtime landed under the install root.
- `InstallRemover` and `RemovalVersionResolver` resolve and delete installed SDK/runtime assets under the selected install root.
- `UpdatePlanner` plans SDK/runtime upgrades and identifies obsolete versions in the same major.minor channel.
- `SelfUpdater` resolves the latest matching GitHub release asset for the current RID, verifies its SHA-256 sidecar, and replaces the current executable.
- `InstallOrchestrator` coordinates install, remove, upgrade, and self-update flows.

### Current Behavioral Boundaries

- `--dry-run` stops after plan generation.
- Non-dry-run install downloads, extracts, verifies, and then configures PATH according to the selected options.
- Existing SDK/runtime installs trigger a warning and confirmation prompt unless `--yes` is set; `--yes` defaults to `true` in CI.
- `--persist-path` is supported only on Windows and cannot be combined with `--no-path`.
- PATH mutation is skipped when another .NET installation is already discoverable from the selected install root, `DOTNET_INSTALL_DIR`, `DOTNET_ROOT`, the current `dotnet` command location, or known default install roots.
- `remove` is destructive and should be previewed with `--dry-run` first when targeting a shared install root.
- `upgrade` installs the requested SDK/runtime when needed and removes other installed versions in the same major.minor channel.
- SDK upgrades remove the related runtime by default when pruning obsolete SDKs; use `upgrade --sdk-only` to keep the runtime installed.
- `self-update` updates GitHub release binaries in place. NuGet tool installs should generally be updated with `dotnet tool update`.

Document these limitations accurately in code, tests, and docs.

## Testing Guidance

Run the full test suite after code changes:

```bash
dotnet test DotNetInstall.slnx
```

Add focused tests when you change:

- command-line options, aliases, or validation rules
- channel or version selection logic
- RID normalization or URL rewrite behavior
- orchestrator output or error handling

Recommended manual checks, depending on the change:

1. `dotnet run --project src/DotNetInstall/DotNetInstall.csproj --framework net10.0 -- --help`
2. `dotnet run --project src/DotNetInstall/DotNetInstall.csproj --framework net10.0 -- remove --help`
3. `dotnet run --project src/DotNetInstall/DotNetInstall.csproj --framework net10.0 -- upgrade --help`
4. `dotnet run --project src/DotNetInstall/DotNetInstall.csproj --framework net10.0 -- self-update --help`
5. `dotnet run --project src/DotNetInstall/DotNetInstall.csproj --framework net10.0 -- version`
6. `dotnet run --project src/DotNetInstall/DotNetInstall.csproj --framework net10.0 -- --dry-run --channel LTS`

If you change packaging or publish behavior, also run:

```bash
dotnet pack src/DotNetInstall/DotNetInstall.csproj -c Release
```

## Coding Conventions

Follow the repository's `.editorconfig` and existing source style.

- Use 4 spaces in C# files and 2 spaces in project, XML, and JSON files.
- Keep braces on new lines.
- Place `using` directives outside namespaces.
- Sort `System` usings first.
- Nullable reference types and implicit usings are enabled.
- Prefer small, explicit changes over broad refactors.
- Preserve the current record-based option models and file-scoped namespaces.

Do not assume generated outputs under `artifacts/` should be committed or edited unless the task explicitly requires that.

## Change Guidance For Agents

- Prefer fixing behavior in `src/DotNetInstall/` and proving it with tests under `tests/DotNetInstall.Tests/`.
- Use the shell scripts as a behavior reference when implementing install semantics.
- Keep public CLI names and aliases stable unless the task explicitly changes the command surface.
- When adding new options, validate interactions in `InstallCommandBuilder` and cover them with parser tests.
- When changing metadata resolution, cover both success and failure cases.
- When changing download behavior, consider proxy settings, feed overrides, and timeout handling.
- When changing extraction, removal, PATH, upgrade, self-update, or GitHub Action behavior, add focused tests around the service or script boundary that owns the behavior.

### Commit Message Convention

Follow the [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) specification:

```text
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

Common types:

| Type | When to use |
| ------ | ------------- |
| `feat` | A new feature |
| `fix` | A bug fix |
| `docs` | Documentation changes only |
| `style` | Formatting changes (no logic change) |
| `refactor` | Code restructuring (no feature or fix) |
| `test` | Adding or updating tests |
| `chore` | Build process, dependency updates, tooling |
| `perf` | Performance improvements |
| `ci` | CI/CD workflow changes |

Examples:

```text
feat(exec): support .rest file extension in exec command
fix(middleware): handle null response body in logging middleware
docs: update installation instructions in README
chore: bump WeihanLi.Common to 1.0.87
```

- Use the **imperative mood** in the description ("add" not "added")
- Keep the first line at 72 characters or fewer
- Reference issues in the footer: `Fixes #123` or `Closes #123`

## Pull Request Notes

- The default branch is `main`.
- Prefer small, reviewable commits.
- Conventional Commits are a good fit for this repository, for example `fix(cli): validate version and quality combination`.
- Before opening or updating a PR, ensure `dotnet build DotNetInstall.slnx` and `dotnet test DotNetInstall.slnx` pass.
