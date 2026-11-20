#Requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SdkRoot,
    [ValidateSet('x64','arm64')][string[]]$Architectures = @('x64','arm64'),
    [string]$GitRoot,
    [string]$VisualStudioRoot
)
. (Join-Path $PSScriptRoot 'common.ps1')
$native = Join-Path $RepoRoot 'artifacts/native'
$downloads = Join-Path $native 'downloads'
New-Item -ItemType Directory -Force -Path $downloads | Out-Null
$manifest = Get-Content (Join-Path $RepoRoot 'dotnet/runtime/eng/openharmony/dependencies.json') -Raw | ConvertFrom-Json
function Get-VerifiedDownload($Url, $File, $Sha256) {
    if (!(Test-Path $File)) {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $Url -OutFile $File
    }
    if ((Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash -ne $Sha256) {
        throw "Checksum mismatch: $File"
    }
}
foreach ($dependency in @($manifest.icu, $manifest.openssl)) {
    Get-VerifiedDownload $dependency.url (Join-Path $downloads $dependency.archive) $dependency.sha256
}
$makeArchive = Join-Path $downloads 'make.pkg.tar.zst'
Get-VerifiedDownload 'https://repo.msys2.org/msys/x86_64/make-4.4.1-2-x86_64.pkg.tar.zst' $makeArchive `
    '2408AF61717DAE87B00C855B132769A125C708907FC94A46BB16DAE076113E5C'
New-Item -ItemType Directory -Force -Path (Join-Path $native 'msys') | Out-Null
Invoke-Checked "$env:SystemRoot/System32/tar.exe" @('-xf',$makeArchive,'-C',(Join-Path $native 'msys'))
if (!(Test-Path (Join-Path $native 'icu-source/icu/source/configure'))) {
    Expand-Archive -LiteralPath (Join-Path $downloads $manifest.icu.archive) -DestinationPath (Join-Path $native 'icu-source')
}
if (!$GitRoot) { $GitRoot = Split-Path (Split-Path (Get-Command git.exe).Source) }
if (!$VisualStudioRoot) {
    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    $VisualStudioRoot = (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
}
if (!$VisualStudioRoot) { throw 'Visual Studio C++ build tools are required for ICU host tools.' }
# The upstream OpenSSL make recipes require a shell path without spaces.
$gitLink = Join-Path $native 'git'
if (!(Test-Path $gitLink)) { New-Item -ItemType Junction -Path $gitLink -Target $GitRoot | Out-Null }
$bash = Join-Path $gitLink 'bin/bash.exe'
$oldPath = $env:PATH
try {
    $env:PATH = "$env:SystemRoot/System32;$(Join-Path $gitLink 'usr/bin');$env:PATH"
    $hostScript = Join-Path $PSScriptRoot 'native/build-icu-host.sh'
    $hostCommand = Join-Path $native 'build-host.cmd'
    @"
@echo off
call "$VisualStudioRoot\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b %errorlevel%
"$bash" "$hostScript" "$native"
"@ | Set-Content -LiteralPath $hostCommand -Encoding ascii
    Invoke-Checked cmd.exe @('/c',$hostCommand)
    foreach ($arch in $Architectures) {
        Invoke-Checked $bash @((Join-Path $PSScriptRoot 'native/build-icu-target.sh'),$native,(Join-Path $SdkRoot '15/native'),$arch)
        $triple = if ($arch -eq 'x64') { 'x86_64-linux-ohos' } else { 'aarch64-linux-ohos' }
        $icuBuild = Join-Path $native "icu-install-$arch"
        $sdkManifest = Get-Content (Join-Path $SdkRoot '15/native/oh-uni-package.json') -Raw | ConvertFrom-Json
        @{
            name='icu'; version=$manifest.icu.version; source=$manifest.icu.url; sha256=$manifest.icu.sha256
            license=$manifest.icu.license; sdkApiLevel=15; sdkPackageVersion=$sdkManifest.version
            architecture=$arch; targetTriple=$triple
        } | ConvertTo-Json | Set-Content (Join-Path $icuBuild 'openharmony-icu-provenance.json') -Encoding ascii
        # Refresh the libraries just built even when the version metadata matches
        # an older staged build (for example after fixing compiler/linker flags).
        $abi = if ($arch -eq 'x64') { 'x86_64' } else { 'arm64-v8a' }
        $icuStaging = Join-Path $native "dependencies/icu/$abi/lib"
        New-Item -ItemType Directory -Force -Path $icuStaging | Out-Null
        foreach ($library in @('libicuuc.so.78', 'libicui18n.so.78', 'libicudata.so.78')) {
            $builtLibrary = Join-Path $icuBuild "lib/$library"
            $dynamic = & (Join-Path $SdkRoot '15/native/llvm/bin/llvm-readelf.exe') -d $builtLibrary
            if ($LASTEXITCODE -ne 0 -or ($dynamic -join "`n") -match '\(RPATH\)|\(RUNPATH\)') {
                throw "ICU must be built without a deployment RPATH/RUNPATH: $builtLibrary"
            }
            Copy-Item -LiteralPath $builtLibrary -Destination $icuStaging -Force
        }
        & (Join-Path $RepoRoot 'dotnet/runtime/eng/openharmony/prepare-dependencies.ps1') `
            -SdkRoot $SdkRoot -Architecture $arch -Perl (Join-Path $gitLink 'usr/bin/perl.exe') `
            -Make (Join-Path $native 'msys/usr/bin/make.exe') -IcuBuildRoot $icuBuild `
            -OutputRoot (Join-Path $native 'dependencies') -CacheRoot $downloads -WorkRoot (Join-Path $native 'work')
    }
}
finally { $env:PATH = $oldPath }
