# GitHub Release Usage

This repository publishes native single-file binaries in each GitHub release.

Use a GitHub release when you want a standalone `dotnet-install` executable without first installing the NuGet global tool package.

## Choose The Correct Asset

Select the release asset that matches the target machine:

- `dotnet-install-<version>-win-x64.exe`
- `dotnet-install-<version>-win-arm64.exe`
- `dotnet-install-<version>-linux-x64`
- `dotnet-install-<version>-linux-arm64`
- `dotnet-install-<version>-osx-arm64`

The asset is the executable itself. It is not wrapped in a `.zip` or `.tar.gz`.

Each binary asset is accompanied by a `.sha256` sidecar file containing its SHA-256 digest.

## Windows

1. Download the `win-x64` or `win-arm64` `.exe` asset from the release page.
2. Move it to a stable location such as `%USERPROFILE%\\bin\\dotnet-install.exe`.
3. If you want `dotnet-install` available from any shell, add that directory to your user `PATH`.
4. Run it directly:

```powershell
.\dotnet-install.exe --help
.\dotnet-install.exe version
```

## Linux And macOS

1. Download the asset for your RID.
2. Rename it to `dotnet-install` if you want a stable local command name.
3. Mark it executable:

    ```sh
    chmod +x ./dotnet-install
    ```

4. Run it:

    ```sh
    ./dotnet-install --help
    ./dotnet-install version
    ```

5. If you want it available globally, move it to a directory already on your `PATH`, such as `$HOME/.local/bin`.

## Verify The Binary

After placing the executable, confirm the tool is runnable:

```sh
dotnet-install version
dotnet-install --help
```

If the command is not found, the binary directory is not on your shell `PATH`. Run the file by path or add its directory to PATH explicitly.

If you want to verify the download before running it, compare the asset against its matching `.sha256` file. For example:

```powershell
Get-FileHash .\dotnet-install-<version>-win-x64.exe -Algorithm SHA256
Get-Content .\dotnet-install-<version>-win-x64.exe.sha256
```

```sh
sha256sum dotnet-install-<version>-linux-x64
cat dotnet-install-<version>-linux-x64.sha256
```

## Using The Tool After Download

Downloading the release binary only installs the `dotnet-install` tool itself. It does not install any SDK or runtime until you run a command such as:

```sh
dotnet-install --channel LTS
dotnet-install --version 10.0.x
dotnet-install --runtime dotnet --channel LTS
```

When those install commands complete:

- current-process PATH is updated unless `--no-path` is set
- user PATH persistence requires `--persist-path` and is supported only on Windows
- PATH updates are skipped if an existing .NET installation is already discoverable, to avoid shadowing it

Use `--dry-run` first if you want to inspect the resolved install plan before downloading anything.
