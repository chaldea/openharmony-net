[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SdkRoot,
    [Parameter(Mandatory=$true)][string]$CommandLineTools,
    [Parameter(Mandatory=$true)][string]$JavaHome,
    [string]$RuntimeReleaseRoot,
    [int]$ApiLevel = 15
)
. (Join-Path $PSScriptRoot 'common.ps1')
if (!$RuntimeReleaseRoot) { $RuntimeReleaseRoot = Join-Path $RepoRoot 'artifacts/runtime/releases/10.0.10-ohos.2-preview.1' }
$sample = Join-Path $RepoRoot 'examples/NativeAotSmoke'
$publish = Join-Path $RepoRoot 'artifacts/sample/publish'
$stage = Join-Path $sample 'Stage'
$oldJava = $env:JAVA_HOME
$oldSdk = $env:DEVECO_SDK_HOME
$oldPath = $env:PATH
try {
    Invoke-Checked dotnet @('publish',(Join-Path $sample 'NativeAotSmoke.csproj'),'-c','Release','-o',$publish,
        '-p:OpenHarmonyTarget=true','-p:OpenHarmonyAbi=x86_64',"-p:OpenHarmonyApiLevel=$ApiLevel",
        "-p:OpenHarmonySdkRoot=$SdkRoot","-p:OpenHarmonyRuntimePackageRoot=$RuntimeReleaseRoot")
    $libs = Join-Path $stage 'entry/libs/x86_64'
    New-Item -ItemType Directory -Force -Path $libs | Out-Null
    Get-ChildItem -LiteralPath $publish -File | Where-Object { $_.Name -match '\.so(\.|$)' } |
        Copy-Item -Destination $libs -Force
    $env:JAVA_HOME = $JavaHome
    $env:DEVECO_SDK_HOME = Join-Path $CommandLineTools 'sdk'
    $env:PATH = "$(Join-Path $CommandLineTools 'tool/node');$(Join-Path $JavaHome 'bin');$env:PATH"
    Push-Location $stage
    try {
        Invoke-Checked (Join-Path $CommandLineTools 'bin/ohpm.bat') @('install')
        Invoke-Checked (Join-Path $CommandLineTools 'bin/hvigorw.bat') @('--mode','module','-p','product=default',
            '-p','module=entry@default','-p','buildMode=debug','assembleHap','--no-daemon')
    }
    finally { Pop-Location }
    $hap = Join-Path $stage 'entry/build/default/outputs/default/entry-default-unsigned.hap'
    if (!(Test-Path $hap)) { throw "HAP output missing: $hap" }
    Copy-Item -LiteralPath $hap -Destination (Join-Path $RepoRoot 'artifacts/sample/NativeAotSmoke-unsigned.hap') -Force
}
finally {
    $env:JAVA_HOME = $oldJava
    $env:DEVECO_SDK_HOME = $oldSdk
    $env:PATH = $oldPath
}
