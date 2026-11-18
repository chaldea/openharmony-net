[CmdletBinding()]
param(
    [string]$SdkRoot = (Join-Path $PSScriptRoot '../artifacts/native/sdk'),
    [ValidateSet(13,15)][int[]]$ApiLevels = @(13,15)
)
. (Join-Path $PSScriptRoot 'common.ps1')
$ProgressPreference = 'SilentlyContinue'
$downloads = Join-Path $RepoRoot 'artifacts/native/downloads'
New-Item -ItemType Directory -Force -Path $downloads,$SdkRoot | Out-Null
$components = Invoke-RestMethod -Method Post -ContentType application/json `
    -Body '{"osType":"windows","osArch":"x64","supportVersion":"26.0-ohos-single-1"}' `
    -Uri 'https://repo.harmonyos.com/sdkmanager/v5/ohos/getSdkList'
foreach ($api in $ApiLevels) {
    $destination = Join-Path $SdkRoot $api
    if (Test-Path (Join-Path $destination 'native/oh-uni-package.json')) { continue }
    $component = $components | Where-Object { $_.apiVersion -eq "$api" -and $_.path -eq 'native' } | Select-Object -First 1
    if (!$component) { throw "Official catalog has no Windows Native SDK for API $api." }
    $archive = Join-Path $downloads "native-api$api.zip"
    if (!(Test-Path $archive)) { Invoke-WebRequest -UseBasicParsing -Uri $component.archive.url -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $component.archive.checksum) {
        throw "Official SDK archive checksum mismatch: $archive"
    }
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
    & (Join-Path $RepoRoot 'dotnet/runtime/eng/openharmony/validate-sdk.ps1') -SdkRoot $SdkRoot -ApiLevel $api -Architecture x64
}
