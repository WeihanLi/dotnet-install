# AGENTS.md

## Project Overview

**dotnet-install** is a command-line tool for installing .NET SDK as an alternative for dotnet-install script.

Key capabilities:

- Install .NET SDK

**Technology stack**: C# / .NET 10, `System.CommandLine`, `xunit.v3`.

## Environment Setup

### Prerequisites

- **.NET 10 SDK** — required (the solution file `.slnx` format is not supported by .NET 8 or older)

```bash
dotnet --list-sdks      # should show 10.0.x
dotnet --list-runtimes  # should show Microsoft.NETCore.App 10.0.x
```

## Build Commands

```bash
# Build the solution
dotnet build

# Build with explicit solution file
dotnet build dotnet-DotNetInstallManager.slnx

# Package the tool as a NuGet package
dotnet pack src/DotNetInstallManager/DotNetInstallManager.csproj -c Release
# Output: src/DotNetInstallManager/bin/Release/dotnet-DotNetInstallManager.{version}.nupkg

# Publish AOT binary (Release only)
dotnet publish src/DotNetInstallManager/DotNetInstallManager.csproj -f net10.0 --use-current-runtime -o dist
```

## Testing Instructions

### Run All Tests

```bash
dotnet test
```

### Test Conventions

- Test framework: **xunit**
- Unit tests are in `tests/DotNetInstallManager.Tests/`, mirroring the `src/DotNetInstallManager/` structure

## Development Workflow

### Run the Application Locally

```bash
# Show help
dotnet run --project src/DotNetInstallManager/DotNetInstallManager.csproj --framework net10.0 -- --help
```

### Install and Test as a Global Tool

```bash
dotnet pack src/DotNetInstallManager/DotNetInstallManager.csproj -c Release
dotnet tool install --global --add-source src/DotNetInstallManager/bin/Release dotnet-DotNetInstallManager
dotnet-install --help
```

### Manual Validation Checklist

After making changes, verify these scenarios:

1. **CLI Help**: `dotnet-install --help` — all options display correctly
2. **Package installation**: global tool installs and `dotnet-install` command responds

## Code Style Guidelines

- **Language**: C# with `LangVersion=preview` (set in `Directory.Build.props`)
- **Nullable reference types**: enabled everywhere
- **Implicit usings**: enabled; common WeihanLi.Common namespaces are globally imported
- **File headers**: every `.cs` file must begin with:

  ```csharp
  // Copyright (c) Weihan Li. All rights reserved.
  // Licensed under the MIT license.
  ```

- **Namespaces**: file-scoped namespace declarations (`namespace Foo;`)
- **Primary constructors**: preferred where applicable
- **`var`**: preferred for all local variable declarations
- **Indentation**: 4 spaces for C# files, 2 spaces for XML/JSON project files
- **Newline**: open braces on new lines (`csharp_new_line_before_open_brace = all`)
- **Sorting**: system `using` directives are **not** sorted first
- Run `dotnet format` to automatically fix formatting issues

## Pull Request Guidelines

- Target the **`dev`** branch for feature and bug-fix PRs
- Ensure `dotnet build` and `dotnet test` pass locally before opening a PR
- The pre-commit hook (`.husky/`) automatically runs `dotnet build`
- Code formatting is enforced via `dotnet format` — run it before committing

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
