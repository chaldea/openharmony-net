[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$')][string]$Version,
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
        foreach ($dependency in $metadata.SelectNodes('.//*[local-name()="dependency"]')) {
            if ($dependency.id -like 'OpenHarmony.*') { throw "Old upstream dependency remains: $($dependency.id)" }
            if ($dependency.id -like 'Chaldea.OpenHarmony.*' -and
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
            foreach ($file in @('runtime-pack/sdk/System.Private.CoreLib.dll',"buildTransitive/$id.targets")) {
                if (!$zip.GetEntry($file)) { throw "$id lacks $file" }
            }
        }
        if ($id -eq 'Chaldea.OpenHarmony.Player.Sdk' -and !$zip.GetEntry("build/$id.targets")) { throw 'Player.Sdk build targets must match its renamed package ID.' }
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
