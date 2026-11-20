[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CommandLineTools,
    [string]$Device = '127.0.0.1:5555',
    [string]$HapPath,
    [int]$TimeoutSeconds = 60
)
. (Join-Path $PSScriptRoot 'common.ps1')
$hdc = Join-Path $CommandLineTools 'sdk/default/openharmony/toolchains/hdc.exe'
$evidence = Join-Path $RepoRoot 'artifacts/sample/evidence'
if (!$HapPath) { $HapPath = Join-Path $RepoRoot 'artifacts/sample/NativeAotSmoke-signed.hap' }
if (!(Test-Path $HapPath)) { throw "HAP missing: $HapPath" }
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$install = & $hdc -t $Device install -r $HapPath
$install | Set-Content (Join-Path $evidence 'install.log')
if ($LASTEXITCODE -ne 0 -or ($install -join "`n") -notmatch 'successfully') {
    throw "HAP install failed: $($install -join ' ')"
}
# Restart only this sample so that the following log marker belongs to this run.
& $hdc -t $Device shell aa force-stop org.openharmony.dotnet.smoke | Out-Null
Invoke-Checked $hdc @('-t',$Device,'shell','aa','start','-a','EntryAbility','-b','org.openharmony.dotnet.smoke')
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
do {
    $samplePid = (& $hdc -t $Device shell pidof org.openharmony.dotnet.smoke | Out-String).Trim()
    $log = @()
    if ($samplePid -match '^\d+$') {
        $log = & $hdc -t $Device shell hilog -x -T DotNetSmoke -P $samplePid
    }
    if (($log -join "`n") -match 'DOTNET_SMOKE result=42 runtime=11 runs=1') { break }
    Start-Sleep -Seconds 2
} while ((Get-Date) -lt $deadline)
$log | Set-Content (Join-Path $evidence 'hilog.log')
if (($log -join "`n") -notmatch 'DOTNET_SMOKE result=42 runtime=11 runs=1') {
    throw 'The installed app did not report a successful .NET 11 NativeAOT invocation.'
}
Invoke-Checked $hdc @('-t',$Device,'shell','snapshot_display','-f','/data/local/tmp/dotnet-smoke.jpeg')
Invoke-Checked $hdc @('-t',$Device,'file','recv','/data/local/tmp/dotnet-smoke.jpeg',(Join-Path $evidence 'screenshot.jpeg'))
@{
    device = $Device
    api = (& $hdc -t $Device shell param get const.ohos.apiversion | Out-String).Trim()
    architecture = (& $hdc -t $Device shell uname -m | Out-String).Trim()
    hapSha256 = (Get-FileHash $HapPath -Algorithm SHA256).Hash
    result = 42
    runtimeMajor = 11
    utc = [DateTimeOffset]::UtcNow.ToString('o')
} | ConvertTo-Json | Set-Content (Join-Path $evidence 'result.json')
Write-Output "Verified .NET 11 NativeAOT on $Device; evidence: $evidence"
