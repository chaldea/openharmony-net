[CmdletBinding()]
param([string]$DotnetRoot)
. (Join-Path $PSScriptRoot 'common.ps1')

$runtimeRoot = Join-Path $RepoRoot 'dotnet/runtime'
$runtimeGlobalJson = Join-Path $runtimeRoot 'global.json'
if (!(Test-Path -LiteralPath $runtimeGlobalJson)) {
    throw 'Initialize dotnet/runtime before checking its build SDK.'
}
$runtimeConfig = Get-Content -Raw -LiteralPath $runtimeGlobalJson | ConvertFrom-Json
if ($runtimeConfig.sdk.version -ne $sdkVersion -or $runtimeConfig.tools.dotnet -ne $sdkVersion) {
    throw "Runtime sdk.version and tools.dotnet must both match SDK $sdkVersion in the root global.json. Found sdk.version=$($runtimeConfig.sdk.version), tools.dotnet=$($runtimeConfig.tools.dotnet)."
}
if (!$DotnetRoot) {
    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $DotnetRoot = Split-Path $dotnetCommand.Source -Parent
}
$DotnetRoot = [IO.Path]::GetFullPath($DotnetRoot)
$dotnet = Join-Path $DotnetRoot 'dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet) -or !(Test-Path -LiteralPath (Join-Path $DotnetRoot "sdk/$sdkVersion"))) {
    throw "Install SDK $sdkVersion before building runtime. It was not found in '$DotnetRoot'. Pass -DotnetRoot here or -RuntimeDotnetRoot to build-runtime.ps1 to select an existing installation."
}
Push-Location $runtimeRoot
try {
    $actual = & $dotnet --version
    if ($LASTEXITCODE -ne 0 -or "$actual".Trim() -ne $sdkVersion) {
        throw "Runtime requires SDK $sdkVersion; '$dotnet' selected '$actual'."
    }
}
finally { Pop-Location }
Write-Host "Runtime build SDK: $sdkVersion ($DotnetRoot)"
return $DotnetRoot
