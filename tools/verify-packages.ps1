[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SdkRoot,
    [ValidateSet('x64','arm64')][string[]]$Architectures = @('x64','arm64'),
    [string]$Version = '11.0.0-ohos.1-preview.1',
    [int]$ApiLevel = 15
)
. (Join-Path $PSScriptRoot 'common.ps1')
$SdkRoot = (Resolve-Path -LiteralPath $SdkRoot).Path
$output = Join-Path $RepoRoot ('artifacts/package-validation/' + [guid]::NewGuid().ToString('N').Substring(0,8))
$packages = Join-Path $RepoRoot 'artifacts/packages'
# Always restore the just-built packages, even when their version is unchanged.
$cache = Join-Path $output ('cache-' + [guid]::NewGuid().ToString('N'))
$readelf = Join-Path $SdkRoot "$ApiLevel/native/llvm/bin/llvm-readelf.exe"
if (!(Test-Path $readelf)) { throw "llvm-readelf was not found: $readelf" }
$results = @()
foreach ($arch in $Architectures) {
    $abi = if ($arch -eq 'x64') { 'x86_64' } else { 'arm64-v8a' }
    $machine = if ($arch -eq 'x64') { 62 } else { 183 }
    $publish = Join-Path $output "$arch/publish"
    Invoke-Checked dotnet @('publish',(Join-Path $RepoRoot 'tests/PackageConsumer/PackageConsumer.csproj'),
        '-c','Release','-o',$publish,"-p:RestoreAdditionalProjectSources=$packages",
        "-p:RestorePackagesPath=$cache","-p:OpenHarmonyPackageVersion=$Version",
        "-p:BaseIntermediateOutputPath=$output/$arch/obj/","-p:BaseOutputPath=$output/$arch/bin/",
        "-p:RuntimeIdentifier=linux-musl-$arch","-p:OpenHarmonyAbi=$abi",
        "-p:OpenHarmonyApiLevel=$ApiLevel","-p:OpenHarmonySdkRoot=$SdkRoot")
    $library = Join-Path $publish 'PackageConsumer.so'
    $symbols = & $readelf --dyn-syms $library
    if ($LASTEXITCODE -ne 0 -or ($symbols -join "`n") -notmatch 'openharmony_dotnet_add' -or
        ($symbols -join "`n") -notmatch 'openharmony_dotnet_version') {
        throw "NativeAOT exports missing for $abi."
    }
    $libraries = @(Get-ChildItem -LiteralPath $publish -File | Where-Object { $_.Name -match '\.so(\.|$)' })
    foreach ($file in $libraries) {
        $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($file.FullName))
        try {
            if ($reader.ReadUInt32() -ne 0x464c457f) { throw "Not an ELF library: $($file.Name)" }
            $reader.BaseStream.Position = 18
            if ($reader.ReadUInt16() -ne $machine) { throw "Wrong ABI in $($file.Name); expected $abi." }
        }
        finally { $reader.Dispose() }
    }
    $results += @{
        abi = $abi; version = $Version; api = $ApiLevel
        nativeLibraries = $libraries.Count
        librarySha256 = (Get-FileHash $library -Algorithm SHA256).Hash
    }
}
$results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'result.json')
Write-Output "Verified Player.Sdk package consumers: $($Architectures -join ', '); evidence: $output"
