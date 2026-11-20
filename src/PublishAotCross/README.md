# Chaldea.OpenHarmony.PublishAotCross

> 本组件已整合到 OpenHarmony 主仓库。请使用根目录的 `build.ps1` 和 `OpenHarmony.slnx`。

This is a NuGet package with an MSBuild target to aid in crosscompilation with [PublishAot](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/). It helps resolving following error:

```sh
$ dotnet publish -r linux-x64
Microsoft.NETCore.Native.Publish.targets(59,5): error : Cross-OS native compilation is not supported.
```

This nuget package allows using [Zig](https://ziglang.org/) as the linker/sysroot to allow crosscompiling to linux-x64/linux-arm64/linux-musl-x64/linux-musl-arm64 from a Windows machine.

## OpenHarmony and .NET 11

Set `OpenHarmonyTarget=true` to use the official HarmonyOS SDK Clang/LLD
toolchain instead of the legacy fakeclang/Zig path. API15 is the default
compatibility profile. APIs 13 through 24 and API26 are accepted; API25 is
intentionally unsupported.

The resolver requires a matching Native SDK manifest. APIs 16, 17, 19, 21, 22,
and 24 are therefore reported as unavailable on SDK installations that do not
contain those Native packages; an emulator system image is never used as a
compiler SDK substitute.

```powershell
dotnet publish -c Release -r linux-musl-arm64 `
  -p:PublishAot=true `
  -p:OpenHarmonyTarget=true `
  -p:OpenHarmonySdkRoot=C:\Users\<user>\AppData\Local\OpenHarmony\Sdk `
  -p:OpenHarmonyApiLevel=26 `
  -p:OpenHarmonyAbi=arm64-v8a
```

The target reads each Native SDK's `oh-uni-package.json` and selects the one whose
manifest API matches `OpenHarmonyApiLevel`; SDK folder names are not inferred.
It derives `OpenHarmonySysroot`, `OpenHarmonyClang`, `OpenHarmonyClangXX`,
`OpenHarmonyLinker`, and `OpenHarmonyTargetTriple` from that verified package.
The supported triples are
`aarch64-linux-ohos` and `x86_64-linux-ohos`; stock `linux-musl-*` RIDs remain
only for .NET runtime-pack restore compatibility.

1. [Download](https://ziglang.org/download/) an archive with Zig for your host machine, extract it and place it on your PATH. I'm using zig-windows-x86_64-0.11.0-dev.4006+bf827d0b5.zip.
2. Optional: [download](https://releases.llvm.org/) LLVM. We only need llvm-objcopy executable so if you care about size, you can delete the rest. The executable needs to be on PATH - you could copy it next to the Zig executable. This step is optional and is required only to strip symbols (make the produced executables smaller). If you don't care about stripping symbols, you can skip it.
3. To your project that is already using Native AOT, add a reference to this NuGet package.
4. Publish for one of the newly available RIDs:
    * `dotnet publish -r linux-x64`
    * `dotnet publish -r linux-arm64`
    * `dotnet publish -r linux-musl-x64`
    * `dotnet publish -r linux-musl-arm64`

    If you skipped the second optional step to download llvm-objcopy, you must also pass `/p:StripSymbols=false` to the publish command, or you'll see an error instructing you to do that.

Even though Zig allows crosscompiling for Windows as well, it's not possible to crosscompile PublishAot like this due to ABI differences (MSVC vs. MingW ABI).
