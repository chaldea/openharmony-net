[CmdletBinding()]
param(
    [ValidateSet('Package','Run')][string]$Action = 'Package',
    [Parameter(Mandatory=$true)][string]$PublishDirectory,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z_][A-Za-z0-9_.-]*$')][string]$AssemblyName,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$')][string]$BundleName,
    [ValidateSet('x86_64','arm64-v8a')][string]$Abi = 'x86_64',
    [string]$CommandLineTools = $env:OHOS_COMMAND_LINE_TOOLS,
    [string]$JavaHome = $env:JAVA_HOME,
    [string]$Device = ''
)
$ErrorActionPreference = 'Stop'
$publish = [IO.Path]::GetFullPath($PublishDirectory)
$work = Join-Path (Split-Path $publish -Parent) 'framework-stage'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$settingsPath = Join-Path $work 'settings.json'
@{
    CommandLineTools=$CommandLineTools; JavaHome=$JavaHome; Abi=$Abi; Device=$Device;
    BundleName=$BundleName; AssemblyName=$AssemblyName;
    HapPath=(Join-Path $publish "$AssemblyName-unsigned.hap");
    PublishDirectory=$publish; WorkDirectory=$work; BuildMode='debug';
    StageTemplate=([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../templates/Stage')));
    DevelopmentSigning='true'; SignedHapPath=''
} | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8
& (Join-Path $PSScriptRoot 'hap.ps1') -Action $Action -SettingsPath $settingsPath
