# AGENTS.md

## Project Overview

**dotnet-install** is a managed prototype of the `dotnet-install` scripts. It exposes a CLI named `dotnet-install` that resolves .NET release metadata, builds an install plan, and downloads the selected SDK or runtime artifact.

This repository is not feature-complete yet. The current implementation is strongest in these areas:

- CLI option binding and validation via `System.CommandLine`
- Release metadata lookup and install plan generation
- Artifact download with proxy and feed overrides
- A preview `remove` command surface

These areas are still incomplete or intentionally stubbed:

- Archive extraction and install layout mutation
- PATH updates
- Actual SDK/runtime removal logic
- Full parity with `dotnet-install.sh` and `dotnet-install.ps1`

When changing behavior, prefer aligning with the existing shell scripts in the repository instead of inventing new semantics.

## Technology Stack

- C# / .NET 10
- `System.CommandLine` 2.0.5
- xUnit tests executed through `Microsoft.NET.Test.Sdk`
- NativeAOT-oriented publish settings in the main tool project

## Repository Layout

- `src/DotNetInstallManager/`: application code
- `src/DotNetInstallManager/Application/`: app startup and host wiring
- `src/DotNetInstallManager/Cli/`: command and option definitions
- `src/DotNetInstallManager/Options/`: immutable option models
- `src/DotNetInstallManager/Services/`: metadata resolution, planning, downloading, orchestration
- `tests/DotNetInstallManager.Tests/`: unit tests mirroring source areas
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
dotnet build DotNetInstallManager.slnx
dotnet test DotNetInstallManager.slnx
```

Useful project-level commands:

```bash
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --help
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- remove --help
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- version
dotnet pack src/DotNetInstallManager/DotNetInstallManager.csproj -c Release
dotnet publish src/DotNetInstallManager/DotNetInstallManager.csproj -c Release -f net10.0 --use-current-runtime -o dist
```

Notes:

- `dotnet pack` produces the global-tool package for `dotnet-install`
- `dotnet publish` is the right validation path for publish-related changes
- Build output goes under `artifacts/` because of the root `Directory.Build.props`

## Architecture Notes

### CLI Layer

- `InstallCommandBuilder` defines the root install flow, the `remove` subcommand, and the `version` subcommand.
- The built-in `System.CommandLine` `--version` option is deliberately removed so `--version` can mean `.NET version to install`.
- If you touch command parsing or option aliases, add or update tests in `tests/DotNetInstallManager.Tests/Cli/`.

### Host Layer

- `Program.cs` only handles Ctrl+C cancellation.
- `DotNetInstallHost` composes the orchestrator and command tree.

### Service Layer

- `InstallPlanBuilder` resolves channels, versions, product kind, RID, and candidate download URLs.
- `ReleaseMetadataClient` reads the release index and per-channel metadata.
- `ArtifactDownloader` is responsible for the download step once a plan exists.
- `InstallOrchestrator` currently prints the plan, downloads the selected asset, and stops short of extraction and installation.

### Current Behavioral Boundaries

- `--dry-run` stops after plan generation.
- Non-dry-run install currently downloads the archive but does not extract it.
- `remove` is a planning stub that reports intent; it does not delete installed bits yet.

Document these limitations accurately in code, tests, and docs. Do not claim install or remove completion unless you implemented and verified it.

## Testing Guidance

Run the full test suite after code changes:

```bash
dotnet test DotNetInstallManager.slnx
```

Add focused tests when you change:

- command-line options, aliases, or validation rules
- channel or version selection logic
- RID normalization or URL rewrite behavior
- orchestrator output or error handling

Recommended manual checks, depending on the change:

1. `dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --help`
2. `dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- remove --help`
3. `dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- version`
4. `dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --dry-run --channel LTS`

If you change packaging or publish behavior, also run:

```bash
dotnet pack src/DotNetInstallManager/DotNetInstallManager.csproj -c Release
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

- Prefer fixing behavior in `src/DotNetInstallManager/` and proving it with tests under `tests/DotNetInstallManager.Tests/`.
- Use the shell scripts as a behavior reference when implementing install semantics.
- Keep public CLI names and aliases stable unless the task explicitly changes the command surface.
- When adding new options, validate interactions in `InstallCommandBuilder` and cover them with parser tests.
- When changing metadata resolution, cover both success and failure cases.
- When changing download behavior, consider proxy settings, feed overrides, and timeout handling.

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
- Before opening or updating a PR, ensure `dotnet build DotNetInstallManager.slnx` and `dotnet test DotNetInstallManager.slnx` pass.
