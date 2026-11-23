[CmdletBinding()]
param([string]$Version = '11.0.0-ohos.1-preview.1')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$path = Join-Path $PSScriptRoot "../artifacts/packages/Chaldea.OpenHarmony.Framework.$Version.nupkg"
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($path))
try {
    $nuspecEntry = $zip.GetEntry('Chaldea.OpenHarmony.Framework.nuspec')
    if (!$nuspecEntry) { throw 'Framework package is missing its nuspec.' }
    $reader = [IO.StreamReader]::new($nuspecEntry.Open())
    try { [xml]$nuspec = $reader.ReadToEnd() }
    finally { $reader.Dispose() }
    $bindings = $nuspec.SelectSingleNode('//*[local-name()="dependency" and @id="Chaldea.OpenHarmony.NDK.Bindings"]')
    if (!$bindings -or $bindings.version -ne "[$Version]") {
        throw "Framework package must pin Chaldea.OpenHarmony.NDK.Bindings to [$Version]."
    }
    foreach ($entry in @('lib/net11.0/OpenHarmony.Framework.dll',
        'native/x86_64/libopenharmony_framework.so', 'native/arm64-v8a/libopenharmony_framework.so',
        'buildTransitive/Chaldea.OpenHarmony.Framework.targets', 'tools/net11.0/OpenHarmony.Framework.Tasks.dll',
        'templates/Stage/entry/src/main/ets/SurfaceView.ets')) {
        if (!$zip.GetEntry($entry)) { throw "Framework package is missing $entry" }
    }
    if (@($zip.Entries | Where-Object FullName -Match '\.(cpp|h)$').Count) { throw 'Framework consumers should receive prebuilt native libraries, not native sources.' }
    Write-Host 'Framework package validated: managed API, both native ABIs, Stage and MSBuild tooling.'
} finally { $zip.Dispose() }
