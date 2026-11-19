[CmdletBinding()]
param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '../artifacts/packages'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/ci-packages')
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repository = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content (Join-Path $repository 'Directory.Build.props')
$version = $props.Project.PropertyGroup.OpenHarmonyPackageVersion.InnerText
$baseline = Get-Content -Raw (Join-Path $PSScriptRoot 'ci-runtime-packages.json') | ConvertFrom-Json
if ($version -ne $baseline.version) {
    throw "Update ci-runtime-packages.json to the published runtime baseline for $version before running PR validation."
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
if (Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.nupkg') {
    throw 'Use an empty CI package output directory to avoid mixing builds.'
}
$catalog = Get-Content -Raw (Join-Path $PSScriptRoot 'release-packages.json') | ConvertFrom-Json
foreach ($id in $catalog.packages) {
    if ($id -like '*.NativeAssets.HarmonyOS') { continue }
    $name = "$id.$version.nupkg"
    $destination = Join-Path $OutputDirectory $name
    if ($id -like '*.Runtime.NativeAot.*') {
        $expected = $baseline.sha256.$id
        if (!$expected -or $expected -notmatch '^[0-9a-f]{64}$') { throw "Missing runtime checksum: $id" }
        $lowerId = $id.ToLowerInvariant()
        $lowerName = $name.ToLowerInvariant()
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$lowerId/$version/$lowerName" -OutFile $destination
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) {
            Remove-Item -LiteralPath $destination
            throw "Runtime package checksum mismatch: $name"
        }
    }
    else {
        $source = Join-Path $PackageDirectory $name
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Build PackTools first. Missing package: $source" }
        Copy-Item -LiteralPath $source -Destination $destination
    }
}
Write-Host "CI workload feed: $OutputDirectory"
