# OpenHarmony.Framework

Reusable C# platform APIs for .NET 11 NativeAOT on HarmonyOS/OpenHarmony API 15+.
The Framework has no dependency on a UI framework, Skia or an application-specific video interface.

| Layer | Responsibility |
| --- | --- |
| `OpenHarmony.NDK.Bindings` | Literal C ABI imports, structs and enums |
| `OpenHarmony.Framework` | C# ownership, owner-thread dispatch, events, preparation tasks and native errors |
| Framework native host | XComponent, EGL and N-API glue; prebuilt for x86_64 and arm64-v8a |
| Framework Stage + SDK | ArkTS lifecycle/IME shell, native asset selection, HAP packaging |
| UI framework adapter | Draw frames and controls, translate input and video events |

`Player.Sdk` is the NativeAOT build SDK, not a media player. It depends on this package.
The `net11.0-ohos` workload implicitly references this package, keeping the API version aligned
with the workload. This is a platform package linked into the application's NativeAOT binary,
not a modification to the .NET BCL or an OS-installed shared .NET runtime.

## Media and surfaces

```csharp
// In a SurfaceView callback, on its owner thread:
var output = new NativeImage(GraphicsContext);
var player = new MediaPlayer("/data/storage/el2/base/files/sample.mp4", Dispatcher);
player.SetVideoSurface(output.Surface);
player.Loop = true;
player.Muted = true;
player.Play(); // Records intent before preparation.
Task prepared = player.PrepareAsync(); // Observe errors; do not block the UI thread.
// In OnFrame, after the host has drained Dispatcher:
if (output.TryAcquireFrame(out var frame)) {
    // Wrap frame.TextureId (GL_TEXTURE_EXTERNAL_OES) in your renderer.
    // Apply the complete frame.TextureTransform; Timestamp is the raw producer timestamp.
}
// In OnDestroyed, while EGL is still current:
player.Dispose();
output.Dispose();
```

`MediaPlayer` also supports HTTP(S), escaped file URIs, volume, mute, loop, nearest supported
playback rate, seek and audio without a video surface. Its source is immutable. Attach a surface
after setting the source and before preparation. `PrepareAsync` cancellation cancels the wait;
dispose the player to terminate the native operation. Native errors include the operation and code.

`NativeImage` is independent of the player: another producer, such as a future Camera API, can
target its `NativeWindow`. A player retains a surface lease. Disposing the surface while a producer
still owns it throws instead of creating a dangling window pointer. `NativeWindow.Borrow` supports
externally owned windows, whose external lifetime remains the caller's responsibility.
`EglContext` borrows the current context; it does not destroy it. Dispose graphics objects before
the context/window owner is destroyed. Explicit disposal is required for native callback owners.

All methods, disposal and public events belong to `PlatformDispatcher`'s creation thread. Native
callbacks copy transient data and enqueue work; they never call application code directly.
Monotonic callback cookies are removed before native release, so late callbacks are ignored.
`SurfaceApplication` drains the dispatcher before each frame, including idle frames. An alternative
host must drain its dispatcher itself. Public event exceptions propagate to that host; the bundled
host logs them and stops rendering rather than unwinding across unmanaged code.

The texture matrix is the complete native 4x4 matrix converted to `System.Numerics` row-vector
convention. It can express rotation, crop and reflection. A renderer must not assume vertical
flip is the only possible transformation.
`Timestamp` preserves the producer's value. NativeImage does not guarantee a common time unit;
the tested AVPlayer supplies microseconds. Use `MediaPlayer.Position` for the playback clock.

## Application host

Subclass `OpenHarmony.Views.SurfaceView` and export one NativeAOT entry point:

```csharp
[UnmanagedCallersOnly(EntryPoint = "openharmony_initialize", CallConvs = [typeof(CallConvCdecl)])]
public static nint Initialize() => SurfaceApplication.Initialize(() => new MySurfaceView());
```

Set `OpenHarmonySurface=true` and `OpenHarmonyBundleName` in the application. Set
`OpenHarmonyCommandLineTools`, `OpenHarmonyJavaHome`, `OpenHarmonyAbi` and `OpenHarmonyApiLevel`
when publishing. The Framework copies its prebuilt `.so`, generates the Stage shell and packages
the HAP. No application-specific C++ compilation is needed. The shell loads the managed assembly
by name and calls the versioned callback table. Navigation uses the Want parameter `openharmonyRoute`.

The first host supports one surface/application per process, UI-thread rendering, text/editing keys,
foreground/background events and app-level back navigation. It does not yet expose multi-window
composition, IME preedit, accessibility or arbitrary native ArkUI children.

## Extending native capabilities

Add ABI declarations in NDK bindings, then a capability namespace such as `OpenHarmony.Camera`,
`OpenHarmony.Location` or `OpenHarmony.Bluetooth`. Reuse the dispatcher, callback registry and
native error contract; define ownership and permission requirements per capability. Prefer direct
C imports. Only capabilities requiring C++/N-API/ArkTS should extend the provider-owned bridge/Stage.
Keep their public API independent of UI frameworks and keep media output independent of producers.
Camera, Location and Bluetooth are extension directions, not implemented APIs in this release.

Build the native host with `tools/build-framework-native.ps1 -NativeSdk <SDK>/native`, then pack
NDK bindings and this project. `build.ps1 -Target PackTools -FrameworkNativeSdk <SDK>/native`
builds, packages and verifies both ABIs. `examples/FrameworkVideo` is an independent C# frame
consumption diagnostic; Anime in the Miko repository supplies the visual/control acceptance app.

References: [AVPlayer NDK guide](https://github.com/openharmony/docs/blob/master/en/application-dev/media/media/using-ndk-avplayer-for-video-playback.md),
[Huawei AVPlayer API](https://developer.huawei.com/consumer/cn/doc/harmonyos-references/capi-avplayer).
