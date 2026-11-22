[CmdletBinding()]
param([string]$NativeSdk = $env:OHOS_NATIVE_HOME)
$ErrorActionPreference = 'Stop'
if (!$NativeSdk) { throw 'Pass -NativeSdk pointing to the official SDK native directory (API 15+).' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$suffix = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
$compiler = Join-Path $NativeSdk "llvm/bin/clang++$suffix"
foreach ($abi in @('x86_64', 'arm64-v8a')) {
    $triple = if ($abi -eq 'x86_64') { 'x86_64-linux-ohos' } else { 'aarch64-linux-ohos' }
    $output = Join-Path $root "artifacts/framework/native/$abi"
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    & $compiler "--target=$triple" "--sysroot=$NativeSdk/sysroot" -std=c++17 -O2 -fPIC -shared `
        -static-libstdc++ '-Wl,-soname,libopenharmony_framework.so' '-Wl,--no-undefined' `
        (Join-Path $root 'src/OpenHarmony.Framework/native/surface_host.cpp') `
        '-lace_napi.z' '-lace_ndk.z' '-lhilog_ndk.z' -lEGL -lGLESv2 -ldl `
        -o (Join-Path $output 'libopenharmony_framework.so')
    if ($LASTEXITCODE -ne 0) { throw "Framework native build failed for $abi" }
}
