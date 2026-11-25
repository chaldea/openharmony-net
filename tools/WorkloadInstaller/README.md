# Chaldea.OpenHarmony.Workload

The `ohos` command manages the `harmonyos` workload. It embeds the manifest
and installs the SDK, templates and runtime packs for the same release.

Prerequisites: Windows x64, Linux x64 or macOS x64/arm64, a writable standalone
.NET SDK in feature band 11.0.100, and its .NET 11 runtime on PATH. Use the SDK
ZIP/tarball or dotnet-install; MSI-managed SDKs cannot install these file-based
workload packs. Select the standalone SDK with PATH before running:

```powershell
dotnet tool install -g Chaldea.OpenHarmony.Workload
ohos workload install
dotnet new ohos -o MyApp
cd MyApp
dotnet build
```

SDK selection follows `global.json`. Use `DOTNET_ROOT` to override PATH, or
pass `--dotnet-root <directory>` to a workload command. Use that SDK for
subsequent dotnet commands. Add the official HarmonyOS command-line tools'
`bin` directory to PATH for application builds.

- `ohos workload install` registers the manifest and installs the packs.
- `ohos workload list` shows installed workloads and their manifest versions.
- `ohos workload update` updates all installed workloads. Install and update accept
  dotnet options such as `--source <feed>` (repeatable) and `--configfile <file>`.
- `ohos workload uninstall` removes the packs and then unregisters the manifest.
- `ohos workload register` and `ohos workload unregister` manage the manifest separately.

## Emulator commands

Install the official Command Line Tools for your host and add their `bin` directory to PATH.
Use a version whose `Emulator -help` lists `-imageList`, `-install` and `-create`.
On first use, review the license with `Emulator -license` and run `Emulator -license accept` if you agree.

```text
ohos emulator create [name] [--device-type <type>] [--os-version <version>] [--http-proxy <url>]
    [--command-line-tools <directory>] [--instance-path <directory>] [--image-root <directory>]
ohos emulator run [name] [--command-line-tools <directory>] [--instance-path <directory>] [--image-root <directory>]
ohos emulator stop [name] [--command-line-tools <directory>]
```

Run `ohos emulator create` once, then `ohos emulator run` to start the device.
All three commands default to the device name `MyPhone`.
`create` reuses an installed image. On a cache miss, it downloads into a temporary directory
and checks for the SDK package manifest and system image before creating the device.
You can rerun the same command after an interrupted download.
The default device type is `phone`. It prefers `HarmonyOS 6.1.0(23)` when available
and otherwise uses a release from the host's image catalog. To choose another image, pass
its `deviceType` and exact `osVersion` from the catalog on your host.

```text
Emulator -imageList -deviceType phone
```

`--http-proxy` sets a proxy for the image download.

All commands accept `--command-line-tools` to select the installation. Otherwise the
tool uses `OHOS_COMMAND_LINE_TOOLS`, then `~/.Huawei/command-line-tools`, then a CLI
`bin` directory on PATH. Devices are stored in `~/.Huawei/Emulator/deployed` by default.
Create and run look for the requested image in DevEco's `%LOCALAPPDATA%\Huawei\Sdk` on
Windows or `~/Library/Huawei/Sdk` on macOS, then in `~/.Huawei/Sdk`. Create downloads
missing images to `~/.Huawei/Sdk`. Use `--instance-path` to select the device directory
and `--image-root` to use a single directory for image lookup and downloads.
The official SDK is in `<command-line-tools>/sdk`.

Project: https://github.com/chaldea/openharmony-net
