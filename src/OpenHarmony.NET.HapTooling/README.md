# Chaldea.OpenHarmony.HapTooling

> 本组件已整合到 OpenHarmony 主仓库。请使用根目录的 `build.ps1` 和 `OpenHarmony.slnx`。

Provider-owned HAP/APP packaging tooling for the XEngine HarmonyOS build
pipeline. This package is one half of the OpenHarmony.NET SDK Bundle that XEngine
imports per project: it supplies the MSBuild targets XEngine invokes to assemble
and verify a signed HarmonyOS package, plus the generic Stage project template,
the `xengine-packaging.json` contract, and the OpenHarmony SDK catalog.

It is a **NoTargets** build-only package (no compile output). Applying it to the
XEngine-generated player csproj pulls in the `BuildHap` / `VerifyHap` /
`BuildApp` / `VerifyApp` targets and the Bundle assets.

## What it ships

| Path | Purpose |
| --- | --- |
| `build/Chaldea.OpenHarmony.HapTooling.props` | Property defaults (`OpenHarmonyAbi`, `HapBuildMode`, hvigorw discovery, Stage root resolution). |
| `build/Chaldea.OpenHarmony.HapTooling.targets` | `BuildHap` / `BuildApp` (hvigorw assembleHap/assembleApp) and `VerifyHap` / `VerifyApp` (receipt + llvm-readelf ELF gate). |
| `templates/stage-player/` | The generic Hvigor Stage project. Tokenized with `{{APPLICATION_ID}}` / `{{APPLICATION_LABEL}}` / `{{VERSION_CODE}}` / `{{API_LEVEL}}` / `{{ABI}}` / `{{PLAYER_LIBRARY}}` / `{{ASSEMBLY_NAME}}`. |
| `templates/stage-player/xengine-packaging.json` | The packaging contract XEngine's `HarmonyOSPackagingContract.ReadAsync` validates. |
| `schemas/sdk-catalog.json` | The OpenHarmony SDK catalog XEngine's `HarmonyOSToolchainResolver.ValidateCatalog` validates. |

## How XEngine invokes it

`HarmonyOSBuildPipeline` runs, in the Stage project directory:

```
dotnet msbuild <packageProject> --no-restore -t:BuildHap \
  -p:Configuration=<Debug|Release> \
  -p:BaseIntermediateOutputPath=<obj> \
  -p:BaseOutputPath=<bin> \
  -p:XEnginePlayerLibraryPath=<abs path to libxengine_player.so>
```

The `packageProject` is the XEngine-generated NativeAOT csproj under `Player/`.
That csproj sets `<XEngineStageProjectDirectory>$(MSBuildThisFileDirectory)..\Stage</XEngineStageProjectDirectory>`,
so `BuildHap` drives the hvigor project that lives in the sibling `Stage/`
directory. Verification is the same call with `-t:VerifyHap`.

## BuildHap target flow

1. `_ResolveStageRoot` &mdash; asserts the Stage dir and its `build-profile.json5` exist.
2. `_StagePlayerLibrary` &mdash; copies `$(XEnginePlayerLibraryPath)` into
   `Stage/entry/src/main/cpp/xengine/libxengine_player.so` (idempotent;
   XEngine also stages it before invoking the target).
3. `_StageRuntimeNativeLibs` &mdash; copies `libc++_shared.so` (and any others in
   `$(OpenHarmonyStageNativeLibs)`) from the Player.Sdk runtime pack into
   `Stage/entry/libs/$(OpenHarmonyAbi)/`.
4. `_InvokeHvigorAssemble` &mdash; runs
   `hvigorw --mode module -p product=default -p module=entry@default -p buildMode=$(HapBuildMode) assembleHap --no-daemon`
   with `WorkingDirectory=$(StageRoot)` and `DEVECO_SDK_HOME` / `OHOS_SDK_HOME`
   exported.
5. `BuildHap` asserts `Stage/entry/build/default/outputs/default/entry-default-signed.hap`
   was produced.

## hvigorw discovery

The target resolves `hvigorw` in this order (first hit wins):

1. `$(HvigorwPath)` &mdash; explicit override.
2. `$(DevEcoSdkHome)\tools\hvigor\bin\hvigorw.bat` (Windows) / `hvigorw`.
3. `hvigorw` on `PATH` (the contract `build-hap-matrix.ps1` relies on).

## Properties

| Property | Default | Notes |
| --- | --- | --- |
| `OpenHarmonyAbi` | `arm64-v8a` | `arm64-v8a` or `x86_64`. |
| `OpenHarmonyApiLevel` | `15` | Catalog lookup key. |
| `OpenHarmonySdkRoot` | `$(OHOS_SDK_ROOT)` | Official OpenHarmony SDK root. |
| `DevEcoSdkHome` | `$(DEVECO_SDK_HOME)` or canonical Windows path | Where hvigorw + signing material live. |
| `XEnginePlayerLibraryPath` | (none) | Required for `BuildHap`/`BuildApp`. |
| `HapBuildMode` | `debug` | `debug` or `release`. |
| `HvigorwPath` | (none) | Explicit hvigorw override. |

## Signing

Debug HAPs rely on DevEco auto-signing material under
`%USERPROFILE%\.ohos\config`. The template's `build-profile.json5` ships a
signingConfig with empty material paths so DevEco fills them at build time; a
provider needing a fixed identity edits the materialized Stage before invoking
`BuildHap`.
