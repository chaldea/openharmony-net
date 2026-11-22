[CmdletBinding()]
param(
    [ValidateSet('Build','Test','PackTools','PackRuntime','Pack')][string]$Target = 'Test',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$Version = '11.0.0-ohos.1-preview.1',
    [string]$RuntimeArtifacts,
    [string]$SdkCatalog,
    [string]$FrameworkNativeSdk = $env:OHOS_NATIVE_HOME
)
. (Join-Path $PSScriptRoot 'tools/common.ps1')
Push-Location $RepoRoot
try {
    New-Item -ItemType Directory -Force -Path artifacts/packages | Out-Null
    $sourceCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source revision.' }
    # Explicitly refresh metadata even when pack reuses incremental build outputs.
    $properties = @("-p:OpenHarmonyPackageVersion=$Version", "-p:Version=$Version", "-p:RepositoryCommit=$sourceCommit")
    Invoke-Checked dotnet (@('build','OpenHarmony.slnx','-c',$Configuration,'--nologo') + $properties)
    if ($Target -eq 'Test') {
        Invoke-Checked dotnet @('test','OpenHarmony.slnx','-c',$Configuration,'--no-build','--nologo',
            '--logger','trx','--results-directory','artifacts/test-results')
    }
    if ($Target -in @('PackTools','Pack')) {
        if (!$FrameworkNativeSdk -and $env:OHOS_COMMAND_LINE_TOOLS) {
            $FrameworkNativeSdk = Join-Path $env:OHOS_COMMAND_LINE_TOOLS 'sdk/default/openharmony/native'
        }
        if (!$FrameworkNativeSdk -and (Test-Path (Join-Path $RepoRoot 'artifacts/native/sdk/15/native'))) {
            $FrameworkNativeSdk = Join-Path $RepoRoot 'artifacts/native/sdk/15/native'
        }
        & (Join-Path $PSScriptRoot 'tools/build-framework-native.ps1') -NativeSdk $FrameworkNativeSdk
        # NuGet's timestamp check does not notice a changed Git revision.
        $catalog = Get-Content -Raw (Join-Path $RepoRoot 'tools/release-packages.json') | ConvertFrom-Json
        foreach ($id in $catalog.packages) {
            if ($Target -eq 'PackTools' -and ($id -like '*.Runtime.NativeAot.*' -or $id -like '*.NativeAssets.HarmonyOS')) { continue }
            $package = Join-Path $RepoRoot "artifacts/packages/$id.$Version.nupkg"
            if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package }
        }
        foreach ($project in @(
            'src/OpenHarmony.NDK.Bindings/OpenHarmony.NDK.Bindings.csproj',
            'src/OpenHarmony.Framework/OpenHarmony.Framework.csproj',
            'src/PublishAotCross/package/OpenHarmony.NET.PublishAotCross.csproj',
            'src/OpenHarmony.NET.HapTooling/OpenHarmony.NET.HapTooling.csproj',
            'src/OpenHarmony.NET.Player.Sdk/OpenHarmony.NET.Player.Sdk.csproj'
        )) {
            Invoke-Checked dotnet (@('pack',$project,'-c',$Configuration,'--no-build','--nologo') + $properties)
        }
        & (Join-Path $PSScriptRoot 'tools/pack-workload.ps1') -Version $Version -Configuration $Configuration -NoBuild
        & (Join-Path $PSScriptRoot 'tools/verify-framework-package.ps1') -Version $Version
    }
    if ($Target -eq 'Pack') {
        foreach ($project in @('SkiaSharp.NativeAssets.HarmonyOS','HarfBuzzSharp.NativeAssets.HarmonyOS')) {
            Invoke-Checked dotnet (@('pack',"src/$project/$project.csproj",'-c',$Configuration,'--no-build','--nologo') + $properties)
        }
    }
    if ($Target -in @('PackRuntime','Pack')) {
        if (!$RuntimeArtifacts) { $RuntimeArtifacts = Join-Path $RepoRoot 'artifacts/native/runtime' }
        if (!$SdkCatalog) { $SdkCatalog = Join-Path $RepoRoot 'dotnet/runtime/eng/openharmony/sdk-catalog.json' }
        $runtimeCommit = (& git -C dotnet/runtime rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Cannot determine runtime revision.' }
        Invoke-Checked dotnet @('run','--project','tools/RuntimePackager/RuntimePackager.csproj',
            '-c',$Configuration,'--no-build','--','--source-root',$RuntimeArtifacts,
            '--output-root','artifacts/runtime','--version',$Version,'--runtime-commit',$runtimeCommit,
            '--bindings-commit',$sourceCommit,'--api-levels','13,14,15,16,17,18,19,20,21,22,23,24,26',
            '--architectures','arm64,x64','--configuration',$Configuration,'--sdk-catalog',$SdkCatalog)
        foreach ($abi in @('arm64-v8a','x86_64')) {
            Invoke-Checked dotnet (@('pack','src/OpenHarmony.NET.Runtime/pack/OpenHarmony.NET.Runtime.NativeAot.csproj',
                '-c',$Configuration,'--no-build',"-p:RuntimeAbi=$abi") + $properties)
        }
    }
}
finally { Pop-Location }
