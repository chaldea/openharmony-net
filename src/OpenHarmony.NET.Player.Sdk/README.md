# OpenHarmony.NET.Player.Sdk

> 本组件已整合到 OpenHarmony 主仓库。请使用根目录的 `build.ps1` 和 `OpenHarmony.slnx`。

An MSBuild NuGet package that consolidates the OpenHarmony NativeAOT player
`.so` build setup. Adding a single `PackageReference` to an XEngine-generated
csproj is enough to produce `libxengine_player.so` via `dotnet publish`.

## What it does

When applied to a project with `OpenHarmonyTarget=true`, the package:

1. **Pulls in the cross-compile toolchain** transitively, by depending on
   `OpenHarmony.NET.PublishAotCross`. Its `build/*.targets` (Crosscompile.targets)
   drive the SDK resolver, clang/sysroot selection, and the NativeAOT link
   invocation for the OpenHarmony target triple.

2. **Pulls in the runtime packs** for both ABIs
   (`OpenHarmony.NET.Runtime.NativeAot.arm64-v8a` and `.x86_64`). Their
   `buildTransitive` targets supply `IlcFrameworkNativePath`/`IlcSdkPath` and the
   native runtime libs (libc++_shared.so, libcrypto, ICU, etc.).

3. **Sets sane defaults** matching the Blazor.Hybrid `Entry.csproj` pattern
   (`PublishAot=true`, `NativeLib=Shared`, `SelfContained=true`), never
   overriding a value the consumer already set.

4. **Pins the active ABI.** Both runtime packs set
   `IlcFrameworkNativePath`/`IlcSdkPath` unconditionally (they are not gated on
   `$(OpenHarmonyAbi)`). `OpenHarmony.NET.Player.Sdk.targets` runs after NuGet
   imports them and forces those properties (plus the `NativeSharedLibrary`
   items) to match the build's `OpenHarmonyAbi`, so depending on both packs is
   safe.

## Usage

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>XEngineGame</AssemblyName>
    <RuntimeIdentifier>linux-musl-arm64</RuntimeIdentifier>
    <PublishAot>true</PublishAot>
    <NativeLib>Shared</NativeLib>
    <SelfContained>true</SelfContained>
    <OpenHarmonyTarget>true</OpenHarmonyTarget>
    <RestoreAdditionalProjectSources>$(MSBuildThisFileDirectory)..\artifacts\packages</RestoreAdditionalProjectSources>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="OpenHarmony.NET.Player.Sdk" Version="10.0.10-ohos.2-preview.1" />
    <PackageReference Include="OpenHarmony.NET.HapTooling" Version="10.0.10-ohos.2-preview.1" PrivateAssets="all" />
    <PackageReference Include="OpenHarmony.NDK.Bindings" Version="10.0.10-ohos.2-preview.1" />
  </ItemGroup>
</Project>
```

```sh
dotnet publish -c Release \
  -p:RuntimeIdentifier=linux-musl-arm64 \
  -p:OpenHarmonyAbi=arm64-v8a \
  -p:OpenHarmonyApiLevel=23 \
  -p:OpenHarmonySdkRoot=<sdk>
```

Adjust the local package source to your project location. This produces
`XEngineGame.so` (AArch64, NEEDED libc.so / libc++_shared.so).
The repository's `tools/verify-packages.ps1` publishes an isolated consumer for
both ABIs and checks all emitted native library architectures and C# exports.
