# HarmonyOS graphics native assets

`Chaldea.SkiaSharp.NativeAssets.HarmonyOS` provides SkiaSharp **4.158.0**;
`Chaldea.HarfBuzzSharp.NativeAssets.HarmonyOS` provides HarfBuzzSharp **15.0.0**.
The package release version follows the OpenHarmony.NET release, independently
of those upstream managed API versions. Exact managed dependencies prevent an
incompatible managed library from silently loading this native ABI.

Add both packages to a `net11.0-ohos` workload project, or a Player.Sdk project
with `OpenHarmonyTarget=true`. Select `OpenHarmonyAbi=x86_64` (emulator) or
`arm64-v8a` (device). Only that ABI is copied to build/publish output, where the
HarmonyOS HAP tooling picks it up. Ordinary desktop/Linux builds are unaffected.
Do not combine these packages with Linux/Android native-asset packages.

The binaries are compiled from pinned source with the official HarmonyOS API15
Clang/sysroot, with static libc++, FreeType, PNG, JPEG, WebP, zlib, Expat and
HarfBuzz. Skia supports raster rendering, PDF, SVG/Skottie and EGL/OpenGL ES;
the demo verifies raster rendering. Vulkan, ICU, DNG/RAW and GIF are disabled.
The default font manager scans `/system/fonts`; applications can also load their
own fonts using `SKTypeface.FromFile`/`FromStream`. No Fontconfig is required.

Each ABI includes a build manifest with source revisions, compiler settings,
SDK identity, native dependencies and SHA-256 hashes. Third-party license texts
are included in `THIRD-PARTY-NOTICES.txt`.
The build applies a checked, equivalent aggregate-initialization change in
Skia's PDF code for API15 libc++ compatibility. The upstream checkout stays clean;
the build script hash records the adaptation.

Source build: initialize `dotnet/SkiaSharp` and its `externals/skia` submodule,
then run `tools/build-skia.ps1 -SdkRoot artifacts/native/sdk` on Windows with
Python 3.12+, Ninja, Git and PowerShell 7.3+. Pack with `build.ps1 -Target Pack`.
