[CmdletBinding()]
param(
    [ValidateSet('Build','Test','PackTools','Pack')][string]$Target = 'Test',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$Version = '10.0.10-ohos.2-preview.1',
    [string]$RuntimeArtifacts,
    [string]$SdkCatalog
)
. (Join-Path $PSScriptRoot 'tools/common.ps1')
Push-Location $RepoRoot
try {
    New-Item -ItemType Directory -Force -Path artifacts/packages | Out-Null
    $properties = @("-p:OpenHarmonyPackageVersion=$Version", "-p:Version=$Version")
    Invoke-Checked dotnet (@('build','OpenHarmony.slnx','-c',$Configuration,'--nologo') + $properties)
    if ($Target -eq 'Test') {
        Invoke-Checked dotnet @('test','OpenHarmony.slnx','-c',$Configuration,'--no-build','--nologo',
            '--logger','trx','--results-directory','artifacts/test-results')
    }
    if ($Target -in @('PackTools','Pack')) {
        foreach ($project in @(
            'src/OpenHarmony.NDK.Bindings/OpenHarmony.NDK.Bindings.csproj',
            'src/PublishAotCross/package/OpenHarmony.NET.PublishAotCross.csproj',
            'src/OpenHarmony.NET.HapTooling/OpenHarmony.NET.HapTooling.csproj',
            'src/OpenHarmony.NET.Player.Sdk/OpenHarmony.NET.Player.Sdk.csproj'
        )) {
            Invoke-Checked dotnet (@('pack',$project,'-c',$Configuration,'--no-build','--nologo') + $properties)
        }
        & (Join-Path $PSScriptRoot 'tools/pack-workload.ps1') -Version $Version -Configuration $Configuration -NoBuild
    }
    if ($Target -eq 'Pack') {
        if (!$RuntimeArtifacts) { $RuntimeArtifacts = Join-Path $RepoRoot 'artifacts/native/runtime' }
        if (!$SdkCatalog) { $SdkCatalog = Join-Path $RepoRoot 'dotnet/runtime/eng/openharmony/sdk-catalog.json' }
        $runtimeCommit = (& git -C dotnet/runtime rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Cannot determine runtime revision.' }
        $bindingsCommit = (& git rev-parse HEAD).Trim()
        Invoke-Checked dotnet @('run','--project','tools/RuntimePackager/RuntimePackager.csproj',
            '-c',$Configuration,'--no-build','--','--source-root',$RuntimeArtifacts,
            '--output-root','artifacts/runtime','--version',$Version,'--runtime-commit',$runtimeCommit,
            '--bindings-commit',$bindingsCommit,'--api-levels','13,14,15,16,17,18,19,20,21,22,23,24,26',
            '--architectures','arm64,x64','--configuration',$Configuration,'--sdk-catalog',$SdkCatalog)
        foreach ($abi in @('arm64-v8a','x86_64')) {
            Invoke-Checked dotnet (@('pack','src/OpenHarmony.NET.Runtime/pack/OpenHarmony.NET.Runtime.NativeAot.csproj',
                '-c',$Configuration,'--no-build',"-p:RuntimeAbi=$abi") + $properties)
        }
    }
}
finally { Pop-Location }
