# Chaldea.OpenHarmony.Workload

Registers the `harmonyos` workload manifest using .NET, with no PowerShell script
or repository checkout. The manifest is embedded in this tool and pins packs to
the same release version. See the repository README for application quick start.

Prerequisites: Windows x64, Linux x64 or macOS x64/arm64, a writable standalone
.NET SDK in feature band 11.0.100, and its .NET 11 runtime on PATH. Use the SDK
ZIP/tarball or dotnet-install; MSI-managed SDKs cannot install these file-based
workload packs. Select the standalone SDK with PATH before running:

```powershell
dotnet tool install -g Chaldea.OpenHarmony.Workload --prerelease
harmonyos-workload init
dotnet workload install harmonyos --skip-manifest-update
dotnet new ohos -n HelloHarmony
```

`init --dotnet-root <directory>` selects a specific standalone SDK. Use that same
SDK's `dotnet` for workload and application commands. `init` checks the selected
feature band, rejects conflicting manifests, and enables manifest update mode
through the CLI. It does not download the HarmonyOS SDK or install workload packs.

Before switching tool versions, uninstall harmonyos and run
`harmonyos-workload unregister`. Then update the tool, initialize its manifest,
and install harmonyos again. Repeating `init` for the same manifest is safe.

Project: https://github.com/chaldea/openharmony-net
