#Requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('GetTag','Export','Restore')][string]$Action,
    [string]$RuntimeArtifacts,
    [string]$AssetDirectory,
    [string]$Repository = $env:GITHUB_REPOSITORY
)
. (Join-Path $PSScriptRoot 'common.ps1')

# The gitlink covers the runtime's SDK catalog and dependency manifest as well as its source.
# Use Git objects so checkout line endings cannot change the tag.
$buildInputs = @(
    'global.json', 'tools/common.ps1', 'tools/resolve-runtime-sdk.ps1',
    'tools/install-native-sdk.ps1', 'tools/prepare-native-dependencies.ps1',
    'tools/native/build-icu-host.sh', 'tools/native/build-icu-target.sh', 'tools/build-runtime.ps1'
)
$gitlink = & git -C $RepoRoot ls-tree HEAD -- dotnet/runtime
if ($LASTEXITCODE -ne 0 -or $gitlink -notmatch '^160000 commit ([0-9a-f]{40})\s+dotnet/runtime$') {
    throw 'Cannot determine the pinned dotnet/runtime revision.'
}
$runtimeCommit = $Matches[1]
$entries = & git -C $RepoRoot ls-tree HEAD -- @buildInputs
if ($LASTEXITCODE -ne 0 -or @($entries).Count -ne $buildInputs.Count) {
    throw 'Cannot determine the pinned runtime build inputs.'
}
$bytes = [Text.Encoding]::UTF8.GetBytes($entries -join "`n")
$fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
$tag = "runtime-$runtimeCommit-$fingerprint"
if ($Action -eq 'GetTag') { return $tag }

function Get-BaselinePaths([string]$Root) {
    foreach ($arch in @('x64','arm64')) {
        $base = "api13/$arch/Release"
        $provenancePath = "$base/runtime-build-provenance.json"
        $provenance = Get-Content -Raw -LiteralPath (Join-Path $Root $provenancePath) | ConvertFrom-Json
        if ($provenance.sourceCommit -cne $runtimeCommit -or $provenance.sourceDirty -ne $false -or
            $provenance.buildApi -ne 13 -or $provenance.architecture -cne $arch -or
            $provenance.configuration -cne 'Release' -or $provenance.configureOnly -ne $false) {
            throw "Runtime provenance does not match the clean $arch API13 baseline at $runtimeCommit."
        }
        $provenancePath
        foreach ($path in @("$base/bin/coreclr/openharmony.$arch.Release", "$base/bin/microsoft.netcore.app.runtime.linux-musl-$arch")) {
            if (!(Test-Path -LiteralPath (Join-Path $Root $path) -PathType Container)) {
                throw "Incomplete runtime baseline. Missing $path."
            }
            $path
        }
    }
}

$download = $Action -eq 'Restore' -and !$AssetDirectory
if (!$AssetDirectory) {
    $directory = if ($download) { 'runtime-downloads' } else { 'runtime-baselines' }
    $AssetDirectory = Join-Path $RepoRoot "artifacts/$directory/$tag"
}
$AssetDirectory = [IO.Path]::GetFullPath($AssetDirectory)
$archive = Join-Path $AssetDirectory 'runtime.zip'
$checksum = "$archive.sha256"

if ($Action -eq 'Export') {
    if (!$RuntimeArtifacts) { $RuntimeArtifacts = Join-Path $RepoRoot 'artifacts/native/runtime' }
    Invoke-Checked git (@('-C',$RepoRoot,'diff','--quiet','HEAD','--') + $buildInputs)
    $paths = Get-BaselinePaths $RuntimeArtifacts
    New-Item -ItemType Directory -Force -Path $AssetDirectory | Out-Null
    Invoke-Checked tar (@('-caf',$archive,'-C',$RuntimeArtifacts) + $paths)
    (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash | Set-Content -LiteralPath $checksum -Encoding ascii
    Write-Host "Prepared $tag in $AssetDirectory"
    return
}

if (!$RuntimeArtifacts) { $RuntimeArtifacts = Join-Path $RepoRoot 'artifacts/runtime-baseline' }
$RuntimeArtifacts = [IO.Path]::GetFullPath($RuntimeArtifacts)
if (Test-Path -LiteralPath $RuntimeArtifacts) { throw "Choose a new runtime restore directory. Already exists: $RuntimeArtifacts" }
if ($download) {
    if (!$Repository) { throw 'Specify the repository that publishes runtime baselines with -Repository.' }
    New-Item -ItemType Directory -Force -Path $AssetDirectory | Out-Null
    foreach ($file in @($archive, $checksum)) {
        if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file }
    }
    & gh release download $tag --repo $Repository --pattern runtime.zip --pattern runtime.zip.sha256 --dir $AssetDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Cannot download $Repository release $tag. See docs/maintainers.md for baseline publication."
    }
}
$expected = (Get-Content -Raw -LiteralPath $checksum).Trim()
if ($expected -notmatch '^[0-9a-f]{64}$' -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) {
    throw "Runtime baseline checksum mismatch: $archive"
}
$parent = New-Item -ItemType Directory -Force -Path (Split-Path $RuntimeArtifacts -Parent)
$destination = Join-Path $parent.FullName (Split-Path $RuntimeArtifacts -Leaf)
$staging = Join-Path $parent.FullName ('.runtime-restore-' + [guid]::NewGuid().ToString('N'))
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $staging)
    Get-BaselinePaths $staging | Out-Null
    [IO.Directory]::Move($staging, $destination)
}
finally {
    if (Test-Path -LiteralPath $staging) {
        $resolved = (Resolve-Path -LiteralPath $staging).ProviderPath
        if ((Split-Path $resolved -Parent) -eq (Split-Path $staging -Parent)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
Write-Host "Restored $tag to $RuntimeArtifacts"
