#Requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SdkRoot,
    [ValidateSet('x64','arm64')][string[]]$Architectures = @('x64','arm64'),
    [string]$Python = 'python',
    [string]$Ninja = 'ninja',
    [int]$Jobs = 8
)
. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-Checked $Python (@((Join-Path $PSScriptRoot 'native/build-skia.py'),
    '--sdk-root', (Resolve-Path $SdkRoot).Path, '--ninja', $Ninja, '--jobs', "$Jobs",
    '--architectures') + $Architectures)
