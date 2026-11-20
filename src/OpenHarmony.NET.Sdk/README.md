# Chaldea.OpenHarmony.Sdk

Installed by the `harmonyos` .NET workload on Windows x64, Linux x64 and macOS
x64/arm64. This package supplies
MSBuild support for `net11.0-ohos`, a NativeAOT entry-point bridge, the Stage
application shell, HAP packaging and device launch scripts. It is a workload SDK,
not a `PackageReference` or an SDK name to put in the application's `Sdk` attribute.

Install the bootstrap tool and workload, then create an application:

Use a writable standalone .NET SDK 11.0.1xx on PATH; MSI-managed workload
installations are not supported. Linux and macOS also need PowerShell 7 (`pwsh`)
on PATH for HAP packaging. Install the HarmonyOS CLI tools for the host OS and
architecture, and JDK 17+. The current packages are a preview release.

```powershell
dotnet tool install -g Chaldea.OpenHarmony.Workload --prerelease
harmonyos-workload init
dotnet workload install harmonyos --skip-manifest-update
$env:OHOS_COMMAND_LINE_TOOLS = 'F:\.huawei\command-line-tools'
$env:JAVA_HOME = 'D:\Program Files\Huawei\DevEco Studio\jbr'
dotnet new ohos -n MyApp
cd MyApp
dotnet build
dotnet run
dotnet publish
```

Applications use `Microsoft.NET.Sdk` and `TargetFramework=net11.0-ohos`.
The default entry point is `public static int App.Run()`; change it with
`OpenHarmonyEntryPoint`. The provided Stage shell displays its result and the
runtime major version. The minimum platform is API 15. `OpenHarmonyApiLevel`
selects the native compiler SDK (inferred when only one SDK is present).

`build` compiles managed code and validates the SDK. `publish` compiles NativeAOT
and creates `bin/<configuration>/net11.0-ohos/<rid>/publish/<assembly>-unsigned.hap`.
`run` publishes, signs a Debug build with the public SDK development identity,
installs through HDC, and launches EntryAbility. Start the emulator separately.
The generated profile is bound to the selected device and valid for 30 days.
Release runs require `OpenHarmonySignedHapPath` or an explicit
`OpenHarmonyUseDevelopmentSigning=true`; store distribution needs your own signing.

Set `OpenHarmonyAbi=arm64-v8a` for a device or `x86_64` for an emulator (default).
`OpenHarmonyDevice` selects an HDC target when multiple targets are connected.
Native SDK-only builds can use `OpenHarmonySdkRoot` / `OHOS_SDK_ROOT` and an explicit
`OpenHarmonyApiLevel`; HAP publishing still needs the official command-line tools
and JDK. No SDK download, certificate, or machine path is bundled in this package.
