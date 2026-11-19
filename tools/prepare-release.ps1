[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$')][string]$Version,
    [ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit,
    [ValidatePattern('^[0-9a-f]{40}$')][string]$RuntimeCommit,
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '../artifacts/packages'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/release')
)
$ErrorActionPreference = 'Stop'
$catalog = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot 'release-packages.json') | ConvertFrom-Json
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count) {
    throw "Release staging must be empty: $OutputDirectory"
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Read-ZipText($Archive, [string]$Name) {
    $entry = $Archive.GetEntry($Name)
    if (!$entry) { throw "Package is missing $Name" }
    $reader = New-Object IO.StreamReader($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
$files = @()
foreach ($id in $catalog.packages) {
    $path = Join-Path $PackageDirectory "$id.$Version.nupkg"
    if (!(Test-Path -LiteralPath $path)) { throw "Release package missing: $path" }
    $zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($path))
    try {
        $nuspecEntry = @($zip.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' })
        if ($nuspecEntry.Count -ne 1) { throw "Expected one nuspec in $path" }
        [xml]$nuspec = Read-ZipText $zip $nuspecEntry[0].FullName
        $metadata = $nuspec.SelectSingleNode('//*[local-name()="metadata"]')
        if ($metadata.id -ne $id -or $metadata.version -ne $Version) { throw "Package identity mismatch in $path" }
        if ($metadata.repository.url -ne $catalog.repository) { throw "Repository metadata mismatch in $path" }
        if ($SourceCommit -and $metadata.repository.commit -and $metadata.repository.commit -ne $SourceCommit) {
            throw "Package source commit mismatch in $path"
        }
        foreach ($dependency in $metadata.SelectNodes('.//*[local-name()="dependency"]')) {
            if ($dependency.id -like 'OpenHarmony.*') { throw "Old upstream dependency remains: $($dependency.id)" }
            if ($dependency.id -like 'Chaldea.*' -and
                ($dependency.id -notin $catalog.packages -or $dependency.version -ne "[$Version]")) {
                throw "Invalid release dependency: $($dependency.id) $($dependency.version)"
            }
        }
        if ($id -like '*.Manifest-*') {
            $manifest = (Read-ZipText $zip 'data/WorkloadManifest.json') | ConvertFrom-Json
            if ($manifest.version -ne $Version) { throw 'Workload manifest version must match release version.' }
            foreach ($pack in $manifest.packs.PSObject.Properties) {
                if ($pack.Name -notin $catalog.packages -or $pack.Value.version -ne $Version) { throw "Invalid workload pack: $($pack.Name)" }
            }
            if (@($manifest.workloads.harmonyos.packs).Count -ne 4) { throw 'Expected four harmonyos workload packs.' }
        }
        if ($id -like '*.Runtime.NativeAot.*') {
            $manifest = (Read-ZipText $zip 'manifest.json') | ConvertFrom-Json
            if ($manifest.version -ne $Version -or $manifest.schemaVersion -ne 2 -or
                @($manifest.compatibilityEntries | Where-Object sourceDirty).Count) { throw "Invalid runtime provenance in $id" }
            if ($SourceCommit -and $manifest.bindingsCommit -ne $SourceCommit) { throw "Runtime package release commit mismatch in $id" }
            if ($RuntimeCommit -and $manifest.runtimeCommit -ne $RuntimeCommit) { throw "Runtime source revision mismatch in $id" }
            foreach ($file in @('runtime-pack/sdk/System.Private.CoreLib.dll',"buildTransitive/$id.targets")) {
                if (!$zip.GetEntry($file)) { throw "$id lacks $file" }
            }
        }
        if ($id -eq 'Chaldea.OpenHarmony.Player.Sdk' -and !$zip.GetEntry("build/$id.targets")) { throw 'Player.Sdk build targets must match its renamed package ID.' }
        if ($id -like '*.NativeAssets.HarmonyOS') {
            $library = if ($id -eq 'Chaldea.SkiaSharp.NativeAssets.HarmonyOS') { 'libSkiaSharp.so' } else { 'libHarfBuzzSharp.so' }
            $managedId = if ($library -eq 'libSkiaSharp.so') { 'SkiaSharp' } else { 'HarfBuzzSharp' }
            $managedVersion = if ($managedId -eq 'SkiaSharp') { '2.88.9' } else { '7.3.0.3' }
            $dependency = @($metadata.SelectNodes('.//*[local-name()="dependency"]') | Where-Object id -EQ $managedId)
            if (!$dependency.Count -or @($dependency | Where-Object version -NE "[$managedVersion]").Count) {
                throw "Native ABI must pin $managedId to $managedVersion in $id"
            }
            foreach ($required in @("buildTransitive/$id.targets", 'buildTransitive/common/HarmonyOS.NativeAssets.targets', 'THIRD-PARTY-NOTICES.txt')) {
                if (!$zip.GetEntry($required)) { throw "$id lacks $required" }
            }
            foreach ($abi in @('x86_64', 'arm64-v8a')) {
                $entry = $zip.GetEntry("native/$abi/$library")
                if (!$entry) { throw "$id lacks $library for $abi" }
                $manifest = (Read-ZipText $zip "native/$abi/manifest.json") | ConvertFrom-Json
                if ($manifest.schemaVersion -ne 1 -or $manifest.abi -ne $abi -or $manifest.apiLevel -ne 15 -or
                    $manifest.skiaSharpVersion -ne '2.88.9' -or $manifest.harfBuzzSharpVersion -ne '7.3.0.3' -or
                    $manifest.skiaSharpCommit -ne '9699cdf046a989423a5e5eca68e7fd813486d81c' -or
                    $manifest.skiaCommit -ne '4bed689c9c9eb77a120c6a9d54af6a572c85d1c2' -or
                    $manifest.sdkManifestSha256 -notmatch '^[0-9a-f]{64}$') { throw "Invalid graphics provenance in $id/$abi" }
                $record = @($manifest.files | Where-Object name -EQ $library)
                if ($record.Count -ne 1) { throw "Missing graphics hash in $id/$abi" }
                $stream = $entry.Open()
                $hash = [Security.Cryptography.SHA256]::Create()
                try { $actual = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
                finally { $stream.Dispose(); $hash.Dispose() }
                if ($actual -ne $record[0].sha256) { throw "Graphics hash mismatch in $id/$abi" }
                $reader = [IO.BinaryReader]::new($entry.Open())
                try { $header = $reader.ReadBytes(20) } finally { $reader.Dispose() }
                $machine = if ($abi -eq 'x86_64') { 62 } else { 183 }
                if ($header.Length -ne 20 -or [BitConverter]::ToUInt32($header,0) -ne 0x464c457f -or
                    $header[4] -ne 2 -or [BitConverter]::ToUInt16($header,18) -ne $machine) { throw "Wrong graphics ELF in $id/$abi" }
            }
        }
    }
    finally { $zip.Dispose() }
    $files += Get-Item -LiteralPath $path
}
# Only allowlisted packages are copied; stale upstream-named nupkgs cannot be pushed.
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$checksums = foreach ($file in $files) {
    Copy-Item -LiteralPath $file.FullName -Destination $OutputDirectory
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS'), ($checksums -join "`n") + "`n")
Write-Host "Validated and staged $($files.Count) packages at $OutputDirectory"
