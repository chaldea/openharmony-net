[CmdletBinding()]
param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$catalog = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot 'release-packages.json') | ConvertFrom-Json
$results = @()
foreach ($id in $catalog.packages) {
    $lower = $id.ToLowerInvariant()
    try { $index = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/$lower/index.json" }
    catch {
        if ($null -ne $_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) {
            $results += @{ package = $id; status = 'not-found' }
            Write-Host "$id : not found on nuget.org"
            continue
        }
        throw # Network/auth/server failures are not evidence of an available name.
    }
    $latest = $index.versions[-1]
    [xml]$nuspec = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/$lower/$latest/$lower.nuspec"
    $repo = $nuspec.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="repository"]')
    if (!$repo -or $repo.url.TrimEnd('/').Replace('.git','') -ne $catalog.repository) {
        throw "Package ID '$id' already exists on nuget.org and is not identified as this repository. Rename it before publishing."
    }
    $results += @{ package = $id; status = 'same-repository'; latest = $latest }
    Write-Host "$id : existing release from this repository ($latest)"
}
if ($OutputPath) {
    New-Item -ItemType Directory -Force -Path (Split-Path ([IO.Path]::GetFullPath($OutputPath)) -Parent) | Out-Null
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
}
