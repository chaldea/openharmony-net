[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SdkRoot,
    [Parameter(Mandatory=$true)][string]$CommandLineTools,
    [Parameter(Mandatory=$true)][string]$JavaHome,
    [string]$Version = '11.0.0-ohos.1-preview.1',
    [string]$Device = '127.0.0.1:5555',
    [switch]$RunOnDevice
)
. (Join-Path $PSScriptRoot 'common.ps1')
$SdkRoot = (Resolve-Path $SdkRoot).Path
$CommandLineTools = (Resolve-Path $CommandLineTools).Path
$JavaHome = (Resolve-Path $JavaHome).Path
$output = Join-Path $RepoRoot ('artifacts/legacy-examples/' + [guid]::NewGuid().ToString('N').Substring(0,8))
$hdc = Join-Path $CommandLineTools 'sdk/default/openharmony/toolchains/hdc.exe'
$bundle = 'org.openharmony.dotnet.legacy'
$oldPath = $env:PATH
$oldJava = $env:JAVA_HOME
$oldSdk = $env:DEVECO_SDK_HOME
$results = @()
New-Item -ItemType Directory -Force -Path $output | Out-Null
try {
    foreach ($arch in @('x64', 'arm64')) {
        $abi = if ($arch -eq 'x64') { 'x86_64' } else { 'arm64-v8a' }
        $common = @('-c','Release','-p:OpenHarmonyTarget=true',"-p:OpenHarmonyAbi=$abi",
            "-p:RuntimeIdentifier=linux-musl-$arch",'-p:OpenHarmonyApiLevel=15',
            "-p:OpenHarmonySdkRoot=$SdkRoot","-p:OpenHarmonyPackageVersion=$Version",
            "-p:RestoreAdditionalProjectSources=$RepoRoot/artifacts/packages","-p:RestorePackagesPath=$output/packages")
        $hello = Join-Path $output "$arch/hello"
        $demo = Join-Path $output "$arch/demo"
        Invoke-Checked dotnet (@('publish',"$RepoRoot/examples/CrossCompileHello/Hello.csproj",'-o',$hello) + $common)
        Invoke-Checked dotnet (@('publish',"$RepoRoot/examples/OpenHarmony.Demo/OpenHarmony.Demo.csproj",'-o',$demo) + $common)

        # The original native-library demo has no Stage host. Reuse only the tracked
        # smoke host files and replace its bridge/page in this isolated fixture.
        $stage = Join-Path $output "$arch/Stage"
        $prefix = 'examples/NativeAotSmoke/Stage/'
        $files = & git -C $RepoRoot ls-files $prefix
        if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate the Stage fixture.' }
        foreach ($file in $files) {
            if ($file.EndsWith('oh-package-lock.json5')) { continue }
            $target = Join-Path $stage $file.Substring($prefix.Length)
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            Copy-Item -LiteralPath (Join-Path $RepoRoot $file) -Destination $target
        }
        $app = Join-Path $stage 'AppScope/app.json5'
        [IO.File]::WriteAllText($app, [IO.File]::ReadAllText($app).Replace('org.openharmony.dotnet.smoke', $bundle))
        $profile = Join-Path $stage 'entry/build-profile.json5'
        [IO.File]::WriteAllText($profile, [IO.File]::ReadAllText($profile).Replace('"x86_64"', "`"$abi`""))
        foreach ($file in @('entry/oh-package.json5','entry/src/main/cpp/types/libsmoke/oh-package.json5')) {
            $path = Join-Path $stage $file
            [IO.File]::WriteAllText($path, [IO.File]::ReadAllText($path).Replace('libsmoke.so', 'libentry.so'))
        }
        @'
export const CSharpAdd: (left: number, right: number) => number;
'@ | Set-Content -Encoding utf8 (Join-Path $stage 'entry/src/main/cpp/types/libsmoke/index.d.ts')
        @'
cmake_minimum_required(VERSION 3.16)
project(dotnet_legacy)
add_library(entry SHARED smoke.cpp)
target_link_directories(entry PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/../../../libs/${OHOS_ARCH}")
target_link_libraries(entry PRIVATE "-l:OpenHarmony.Demo.so")
set_target_properties(entry PROPERTIES SKIP_BUILD_RPATH TRUE)
'@ | Set-Content -Encoding utf8 (Join-Path $stage 'entry/src/main/cpp/CMakeLists.txt')
        @'
extern "C" void RegisterEntryModule();
__attribute__((constructor)) static void Register() { RegisterEntryModule(); }
'@ | Set-Content -Encoding utf8 (Join-Path $stage 'entry/src/main/cpp/smoke.cpp')
        @'
import demo from 'libentry.so';
import { hilog } from '@kit.PerformanceAnalysisKit';
@Entry
@Component
struct Index {
  @State result: number = 0;
  aboutToAppear(): void {
    this.result = demo.CSharpAdd(19, 23);
    hilog.info(0x0001, 'LegacyDemo', 'LEGACY_DEMO result=%{public}d', this.result);
  }
  build() {
    Column({ space: 20 }) {
      Text('OpenHarmony.Demo / .NET 11').fontSize(24)
      Text(`CSharpAdd(19, 23) = ${this.result}`).fontSize(24)
      XComponent({ id: 'native', type: XComponentType.SURFACE, libraryname: 'entry' })
        .width('100%').height(300)
    }.width('100%').height('100%').padding(24)
  }
}
'@ | Set-Content -Encoding utf8 (Join-Path $stage 'entry/src/main/ets/pages/Index.ets')
        $libs = Join-Path $stage "entry/libs/$abi"
        New-Item -ItemType Directory -Force -Path $libs | Out-Null
        Get-ChildItem -LiteralPath $demo -File | Where-Object Name -Match '\.so(\.|$)' | Copy-Item -Destination $libs
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
        $hap = Join-Path $stage 'entry/build/default/outputs/default/entry-default-unsigned.hap'
        if (!(Test-Path $hap)) { throw 'Legacy demo HAP missing.' }
        $machine = if ($arch -eq 'x64') { 62 } else { 183 }
        foreach ($nativeFile in @((Join-Path $hello 'Hello'), (Join-Path $demo 'OpenHarmony.Demo.so'))) {
            $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($nativeFile))
            try {
                $header = $reader.ReadBytes(20)
                if ($header.Length -ne 20 -or [BitConverter]::ToUInt32($header,0) -ne 0x464c457f -or
                    [BitConverter]::ToUInt16($header,18) -ne $machine) { throw "Wrong ELF architecture: $nativeFile" }
            }
            finally { $reader.Dispose() }
        }
        $result = @{ abi=$abi; hap=$hap; hapSha256=(Get-FileHash $hap).Hash; ran=$false }
        if ($RunOnDevice -and $arch -eq 'x64') {
            $remote = '/data/local/tmp/dotnet11-hello-' + [guid]::NewGuid().ToString('N').Substring(0,8)
            Invoke-Checked $hdc @('-t',$Device,'shell','mkdir','-p',$remote)
            foreach ($file in Get-ChildItem $hello -File) {
                Invoke-Checked $hdc @('-t',$Device,'file','send',$file.FullName,"$remote/$($file.Name)")
            }
            Invoke-Checked $hdc @('-t',$Device,'shell','chmod','755',"$remote/Hello")
            $console = & $hdc -t $Device shell "cd $remote && LD_LIBRARY_PATH=$remote ./Hello"
            $console | Set-Content -Encoding utf8 (Join-Path $output 'hello.log')
            if ($LASTEXITCODE -ne 0 -or ($console -join "`n") -notmatch 'CROSS_COMPILE_HELLO runtime=11 mutex=ok') {
                throw 'The console example did not confirm .NET 11 and named mutex operation.'
            }
            $settings = Join-Path $output 'run-settings.json'
            @{
                CommandLineTools=$CommandLineTools; JavaHome=$JavaHome; Abi=$abi; Device=$Device;
                BundleName=$bundle; AssemblyName='OpenHarmony.Demo'; HapPath=$hap; PublishDirectory=$demo;
                WorkDirectory=(Join-Path $output 'device'); DevelopmentSigning='true'; SignedHapPath=''
            } | ConvertTo-Json | Set-Content -Encoding utf8 $settings
            & "$RepoRoot/src/OpenHarmony.NET.Sdk/scripts/hap.ps1" -Action Run -SettingsPath $settings
            $deadline = (Get-Date).AddSeconds(60)
            do {
                $appPid = (& $hdc -t $Device shell pidof $bundle | Out-String).Trim()
                $log = @()
                if ($appPid -match '^\d+$') { $log = & $hdc -t $Device shell hilog -x -P $appPid }
                $text = $log -join "`n"
                if ($text -match 'LEGACY_DEMO result=42' -and $text -match 'Hello CSharp; runtime=11' -and $text -match 'egl init success') { break }
                Start-Sleep -Seconds 2
            } while ((Get-Date) -lt $deadline)
            $log | Set-Content -Encoding utf8 (Join-Path $output 'demo-hilog.log')
            if ($text -notmatch 'LEGACY_DEMO result=42' -or $text -notmatch 'Hello CSharp; runtime=11' -or $text -notmatch 'egl init success') {
                throw "Legacy demo NAPI/EGL runtime validation failed. Evidence: $output"
            }
            Invoke-Checked $hdc @('-t',$Device,'shell','snapshot_display','-f','/data/local/tmp/dotnet11-legacy.jpeg')
            Invoke-Checked $hdc @('-t',$Device,'file','recv','/data/local/tmp/dotnet11-legacy.jpeg',"$output/screenshot.jpeg")
            $result.ran = $true
            $result.runtimeMajor = 11
            $result.device = $Device
        }
        $results += $result
        $results | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $output 'result.json')
    }
    Write-Output "Verified legacy examples; evidence: $output"
}
finally {
    $env:PATH = $oldPath
    $env:JAVA_HOME = $oldJava
    $env:DEVECO_SDK_HOME = $oldSdk
}
