[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SdkRoot,
    [Parameter(Mandatory=$true)][string]$CommandLineTools,
    [Parameter(Mandatory=$true)][string]$JavaHome,
    [string]$Version = '11.0.0-ohos.1-preview.1',
    [ValidateSet('x64','arm64')][string[]]$Architectures = @('x64','arm64'),
    [switch]$RunOnDevice,
    [string]$Device = '127.0.0.1:5555'
)
. (Join-Path $PSScriptRoot 'common.ps1')
$SdkRoot = (Resolve-Path $SdkRoot).Path
$CommandLineTools = (Resolve-Path $CommandLineTools).Path
$JavaHome = (Resolve-Path $JavaHome).Path
$output = Join-Path $RepoRoot ('artifacts/skia-validation/' + [guid]::NewGuid().ToString('N').Substring(0,8))
$sample = Join-Path $RepoRoot 'examples/SkiaDemo'
$cache = Join-Path $output 'packages'
$bundle = 'org.openharmony.dotnet.skia'
$results = @()
New-Item -ItemType Directory -Force -Path $output | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$oldPath = $env:PATH
$oldJava = $env:JAVA_HOME
$oldSdk = $env:DEVECO_SDK_HOME
try {
    foreach ($arch in $Architectures) {
        $abi = if ($arch -eq 'x64') { 'x86_64' } else { 'arm64-v8a' }
        $machine = if ($arch -eq 'x64') { 62 } else { 183 }
        $publish = Join-Path $output "$arch/publish"
        Invoke-Checked dotnet @('publish',"$sample/SkiaDemo.csproj",'-c','Release','-o',$publish,
            '-p:OpenHarmonyTarget=true',"-p:OpenHarmonyAbi=$abi",'-p:OpenHarmonyApiLevel=15',
            "-p:OpenHarmonySdkRoot=$SdkRoot","-p:OpenHarmonyPackageVersion=$Version",
            "-p:RestoreAdditionalProjectSources=$RepoRoot/artifacts/packages","-p:RestorePackagesPath=$cache")
        $stage = Join-Path $output "$arch/Stage"
        Copy-Item -LiteralPath "$sample/Stage" -Destination $stage -Recurse
        $profile = Join-Path $stage 'entry/build-profile.json5'
        [IO.File]::WriteAllText($profile, [IO.File]::ReadAllText($profile).Replace('"x86_64"', "`"$abi`""))
        $libs = Join-Path $stage "entry/libs/$abi"
        New-Item -ItemType Directory -Force -Path $libs | Out-Null
        Get-ChildItem -LiteralPath $publish -File | Where-Object Name -Match '\.so(\.|$)' |
            Copy-Item -Destination $libs
        foreach ($name in @('libSkiaSharp.so', 'libHarfBuzzSharp.so')) {
            $built = Join-Path $RepoRoot "artifacts/native/skia/$abi/$name"
            if ((Get-FileHash "$libs/$name").Hash -ne (Get-FileHash $built).Hash) {
                throw "Restored $name does not match the newly built HarmonyOS library."
            }
        }
        $env:JAVA_HOME = $JavaHome
        $env:DEVECO_SDK_HOME = Join-Path $CommandLineTools 'sdk'
        $env:PATH = "$(Join-Path $CommandLineTools 'tool/node');$(Join-Path $JavaHome 'bin');$oldPath"
        Push-Location $stage
        try {
            Invoke-Checked (Join-Path $CommandLineTools 'bin/ohpm.bat') @('install')
            Invoke-Checked (Join-Path $CommandLineTools 'bin/hvigorw.bat') @('--mode','module','-p','product=default',
                '-p','module=entry@default','-p','buildMode=debug','assembleHap','--no-daemon')
        }
        finally { Pop-Location }
        $hap = Join-Path $output "$arch/SkiaDemo-unsigned.hap"
        Copy-Item "$stage/entry/build/default/outputs/default/entry-default-unsigned.hap" $hap
        $zip = [IO.Compression.ZipFile]::OpenRead($hap)
        try {
            $libraries = @($zip.Entries | Where-Object FullName -Match '^libs/.*\.so(\.|$)')
            foreach ($name in @('libSkiaSharp.so', 'libHarfBuzzSharp.so', 'openharmony_skia.so')) {
                if (!$zip.GetEntry("libs/$abi/$name")) { throw "HAP lacks $name for $abi" }
            }
            foreach ($library in $libraries) {
                $reader = [IO.BinaryReader]::new($library.Open())
                try {
                    $header = $reader.ReadBytes(20)
                    if ($header.Length -ne 20 -or [BitConverter]::ToUInt32($header,0) -ne 0x464c457f -or
                        [BitConverter]::ToUInt16($header,18) -ne $machine -or !$library.FullName.StartsWith("libs/$abi/")) {
                        throw "Wrong ELF ABI in $($library.FullName)"
                    }
                }
                finally { $reader.Dispose() }
            }
        }
        finally { $zip.Dispose() }
        $result = @{ abi=$abi; hapSha256=(Get-FileHash $hap).Hash; nativeLibraries=$libraries.Count; rendered=$false }
        if ($RunOnDevice -and $arch -eq 'x64') {
            $settingsPath = Join-Path $output 'run-settings.json'
            @{
                CommandLineTools=$CommandLineTools; JavaHome=$JavaHome; Abi=$abi; Device=$Device;
                BundleName=$bundle; AssemblyName='SkiaDemo'; HapPath=$hap; PublishDirectory=$publish;
                WorkDirectory=(Join-Path $output 'device'); DevelopmentSigning='true'; SignedHapPath=''
            } | ConvertTo-Json | Set-Content -Encoding UTF8 $settingsPath
            & "$RepoRoot/src/OpenHarmony.NET.Sdk/scripts/hap.ps1" -Action Run -SettingsPath $settingsPath
            $hdc = Join-Path $CommandLineTools 'sdk/default/openharmony/toolchains/hdc.exe'
            $deadline = (Get-Date).AddSeconds(60)
            do {
                $samplePid = (& $hdc -t $Device shell pidof $bundle | Out-String).Trim()
                $log = @()
                if ($samplePid -match '^\d+$') { $log = & $hdc -t $Device shell hilog -x -T SkiaDemo -P $samplePid }
                $text = $log -join "`n"
                if ($text -match 'SKIA_DEMO rendered=1 pixels=640x800 glyphs=([1-9][0-9]*)') { break }
                if ($text -match 'SKIA_DEMO failed') { break }
                Start-Sleep -Seconds 2
            } while ((Get-Date) -lt $deadline)
            $log | Set-Content -Encoding UTF8 (Join-Path $output 'hilog.log')
            if ($text -notmatch 'SKIA_DEMO rendered=1 pixels=640x800 glyphs=([1-9][0-9]*)') {
                throw "Skia rendering was not confirmed by this app process. Evidence: $output"
            }
            $result.glyphs = [int]$Matches[1]
            $result.rendered = $true
            $result.device = $Device
            $result.api = (& $hdc -t $Device shell param get const.ohos.apiversion | Out-String).Trim()
            Invoke-Checked $hdc @('-t',$Device,'shell','snapshot_display','-f','/data/local/tmp/skia-demo.jpeg')
            Invoke-Checked $hdc @('-t',$Device,'file','recv','/data/local/tmp/skia-demo.jpeg',"$output/screenshot.jpeg")
        }
        $results += $result
        $results | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $output 'result.json')
    }
    if ($RunOnDevice -and 'x64' -notin $Architectures) { throw 'RunOnDevice requires x64 in Architectures.' }
    Write-Output "Verified Skia packages and HAPs; evidence: $output"
}
finally {
    $env:PATH = $oldPath
    $env:JAVA_HOME = $oldJava
    $env:DEVECO_SDK_HOME = $oldSdk
}
