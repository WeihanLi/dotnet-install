# NativeAOT dotnet-install ![Build](https://github.com/WeihanLi/dotnet-install/actions/workflows/build.yml/badge.svg)

This repository now contains a managed prototype for the `dotnet-install` script.
The `src/DotNetInstallManager` project:

- Targets `net10.0` with NativeAOT-friendly settings (PublishAot, trimming-ready properties, invariant globalization).
- Uses `System.CommandLine` 2.0.0 to mirror the existing `dotnet-install.sh` / `.ps1` command surface (channels, qualities, runtimes, feeds, proxy knobs, etc.).
- Adds a `remove` command so you can prototype SDK/runtime uninstallation flows alongside installs.
- Stubs an orchestrator so we can plug in discovery, download, extraction, removal, and verification workflows while preserving the script semantics.

## Getting started

```powershell
# restore & build
cd C:\projects\source\dotnet-install-script
 dotnet build DotNetInstallManager.slnx

# explore install CLI help
 dotnet run --project src/DotNetInstallManager -- --help

# remove a specific SDK/runtime (preview implementation)
 dotnet run --project src/DotNetInstallManager -- remove 8.0.204
```

`InstallOrchestrator` currently prints a plan summary so we can verify option binding before filling in the networking, extraction, and removal layers.

## Continuous integration

- `Build` workflow validates that `DotNetInstallManager.slnx` restores and builds on every push/PR targeting `main`.
- `Release NativeAOT Artifacts` workflow (manual or release-driven) publishes NativeAOT binaries for `win-x64`, `linux-x64`, and `osx-arm64`, uploads them as workflow artifacts, and attaches them to GitHub releases using the tag name for versioning.
