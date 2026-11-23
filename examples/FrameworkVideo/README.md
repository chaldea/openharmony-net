# FrameworkVideo

Independent C# smoke application for `OpenHarmony.Framework`, with no Miko/Skia dependency.
The bundled generated 320x180 MP4 fixture exercises AVPlayer -> NativeImage -> external GL texture.
It logs frames instead of drawing controls; use Anime for visual acceptance.

Publish with the local package feed and the same platform properties used by other Player.Sdk apps:

```powershell
dotnet publish examples/FrameworkVideo -c Release -o artifacts/framework/probe/publish `
  -p:OpenHarmonyTarget=true -p:OpenHarmonyAbi=x86_64 -p:OpenHarmonyApiLevel=15 `
  -p:RestoreAdditionalProjectSources=<repo>/artifacts/packages `
  -p:OpenHarmonySdkRoot=<repo>/artifacts/native/sdk `
  -p:OpenHarmonyCommandLineTools=<command-line-tools> -p:OpenHarmonyJavaHome=<jdk>
```

The publish target creates the HAP through the Framework package's MSBuild task. Set
`OpenHarmonyCommandLineTools`, `OpenHarmonyJavaHome`, and `OpenHarmonyUseDevelopmentSigning=true`
for local device runs. The package is restored under the normal NuGet cache or the specified
`RestorePackagesPath`.

```powershell
hdc shell aa start -b org.openharmony.frameworkvideo -a EntryAbility --ps openharmonyRoute /video
```

The routes `/pause`, `/seek` (2 seconds), `/resume`, `/release` and `/invalid` exercise controls,
deterministic teardown and malformed-source error reporting. After `/invalid`, use `/release`
then `/video` to verify recovery with a new player. Read the `OpenHarmony` log tag: successful
preparation and frame consumption produce `FRAMEWORK_VIDEO`; malformed media produces
`FRAMEWORK_EXPECTED_MEDIA_ERROR`. No managed exception should reach `OH_FRAMEWORK_ERROR`.
