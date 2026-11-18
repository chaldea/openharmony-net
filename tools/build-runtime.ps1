[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SdkRoot,
    [string]$DependencyRoot,
    [ValidateSet('x64','arm64')][string[]]$Architectures = @('x64','arm64'),
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$RuntimeDotnetRoot
)
. (Join-Path $PSScriptRoot 'common.ps1')
if (!$DependencyRoot) { $DependencyRoot = Join-Path $RepoRoot 'artifacts/native/dependencies' }
if (!$RuntimeDotnetRoot) { $RuntimeDotnetRoot = Join-Path $RepoRoot 'artifacts/runtime-dotnet' }
$oldDotnet = $env:DOTNET_INSTALL_DIR
try {
    if (Test-Path (Join-Path $RuntimeDotnetRoot 'dotnet.exe')) { $env:DOTNET_INSTALL_DIR = $RuntimeDotnetRoot }
    foreach ($arch in $Architectures) {
        $abi = if ($arch -eq 'x64') { 'x86_64' } else { 'arm64-v8a' }
        & (Join-Path $RepoRoot 'dotnet/runtime/eng/openharmony/build-runtime.ps1') `
            -SdkRoot $SdkRoot -ApiLevel 13 -Architecture $arch -Configuration $Configuration `
            -OpenSslRoot (Join-Path $DependencyRoot "openssl/$abi") `
            -IcuRoot (Join-Path $DependencyRoot "icu/$abi") `
            -ArtifactsRoot (Join-Path $RepoRoot "artifacts/native/runtime/api13/$arch/$Configuration")
        if ($LASTEXITCODE -ne 0) { throw "Runtime build failed for $arch." }
    }
}
finally { $env:DOTNET_INSTALL_DIR = $oldDotnet }
