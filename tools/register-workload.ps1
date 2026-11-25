[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$DotnetRoot,
    [switch]$Unregister
)
# Maintainer compatibility wrapper. End users install Chaldea.OpenHarmony.Workload.
. (Join-Path $PSScriptRoot 'common.ps1')
$action = if ($Unregister) { 'unregister' } else { 'register' }
Invoke-Checked dotnet @('run','--project',(Join-Path $RepoRoot 'tools/WorkloadInstaller/WorkloadInstaller.csproj'),
    '-c','Release','--','workload',$action,'--dotnet-root',([IO.Path]::GetFullPath($DotnetRoot)))
