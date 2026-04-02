# Releasing

This repository publishes two release outputs:

- native single-file executables attached to the GitHub release
- the `spark.dotnet-install` NuGet global-tool package

## Release Inputs

Use a stable SemVer tag such as `1.0.0` or a prerelease tag such as `1.1.0-preview.1`.

The workflows also accept tags prefixed with `v`, for example `v1.0.0`. Release packaging normalizes that to `1.0.0` for artifact and package version metadata.

## Publish Flow

Publishing a GitHub release triggers:

- `.github/workflows/release.yml` to build, test, publish native binaries, and attach release assets
- `.github/workflows/nuget.yml` to pack and push the NuGet global-tool package

## GitHub Release Outputs

The release workflow publishes one binary per RID:

- `dotnet-install-<version>-win-x64.exe`
- `dotnet-install-<version>-win-arm64.exe`
- `dotnet-install-<version>-linux-x64`
- `dotnet-install-<version>-linux-arm64`
- `dotnet-install-<version>-osx-arm64`

Each binary also has a matching `.sha256` sidecar file.

## NuGet Output

The NuGet workflow packs and publishes:

- `spark.dotnet-install.<version>.nupkg`

The package version is derived from the release tag after stripping an optional leading `v`.

## Pre-Release Checklist

Before publishing a release:

1. Run `dotnet build DotNetInstallManager.slnx`.
2. Run `dotnet test DotNetInstallManager.slnx`.
3. Run `dotnet pack src/DotNetInstallManager/DotNetInstallManager.csproj -c Release`.
4. Confirm [README.md](../README.md) and [docs/github-releases.md](github-releases.md) still match the actual command surface.
5. Confirm the release tag is the intended SemVer for both GitHub assets and the NuGet package.

## Post-Release Checks

After the workflows complete:

1. Verify every RID asset and matching `.sha256` file is attached to the GitHub release.
2. Verify `spark.dotnet-install` is published on NuGet with the same normalized version.
3. Install from NuGet with `dotnet tool install --global spark.dotnet-install --version <version>`.
4. Run a downloaded release binary with `dotnet-install --help`.
