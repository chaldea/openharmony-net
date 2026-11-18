# NativeAOT smoke application

`Exports.cs` exports C# functions through NativeAOT. The Stage application loads
`libsmoke.so`, whose N-API methods call the managed exports. The page checks
`19 + 23 == 42` and `Environment.Version.Major == 10`; C# also allocates a list,
executes LINQ, and runs GC on each invocation.

The normal solution build compiles the managed library without NativeAOT so it
does not require a Harmony SDK. `tools/build-sample.ps1` enables OpenHarmony
NativeAOT, consumes this workspace's runtime pack, stages the native libraries,
and invokes the official command line tools. Run it from the repository root
after building and packaging the runtime; see the root README.

The Stage profile supports API 15 and newer and targets the x86_64 emulator.
The compiler uses the installed command line SDK. Native runtime and dependency
binaries use the pinned API 13/API 15 SDKs so they can run on the API 17 emulator.

`tools/sign-sample.ps1` uses the SDK's public development identity for this sample
and writes a device-bound profile valid for 30 days under `artifacts/sample/signing/`.
`tools/verify-sample.ps1` installs the resulting HAP, restarts only this sample,
checks its process logs, and saves a screenshot and evidence JSON. A successful
launch alone is insufficient: the C# result and runtime version must both match.
