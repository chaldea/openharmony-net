[CmdletBinding()]
param(
    [string]$Version = '11.0.0-ohos.1-preview.1',
    [ValidatePattern('^11\.0\.[0-9]00$')][string]$FeatureBand = '11.0.100',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$NoBuild
)
. (Join-Path $PSScriptRoot 'common.ps1')
Push-Location $RepoRoot
try {
    $sourceCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source revision.' }
    # Also refresh package metadata when invoked directly after a Git commit.
    foreach ($id in @('Chaldea.OpenHarmony.Sdk','Chaldea.OpenHarmony.Templates',
        'Chaldea.OpenHarmony.Workload',"Chaldea.OpenHarmony.Workload.Manifest-$FeatureBand")) {
        $package = Join-Path $RepoRoot "artifacts/packages/$id.$Version.nupkg"
        if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package }
    }
    $output = Join-Path $RepoRoot 'artifacts/workload/manifest'
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $manifestTemplate = Get-Content -Raw -Encoding UTF8 'src/OpenHarmony.NET.Workload/WorkloadManifest.json.in'
    $manifest = $manifestTemplate.Replace('@VERSION@', $Version)
    $null = $manifest | ConvertFrom-Json
    $manifestPath = Join-Path $output 'WorkloadManifest.json'
    [IO.File]::WriteAllText($manifestPath, $manifest, (New-Object Text.UTF8Encoding $false))
    foreach ($project in @('OpenHarmony.NET.Sdk','OpenHarmony.NET.Templates')) {
        $arguments = @('pack',"src/$project/$project.csproj",'-c',$Configuration,'--nologo',"-p:Version=$Version","-p:RepositoryCommit=$sourceCommit")
        if ($NoBuild) { $arguments += '--no-build' }
        Invoke-Checked dotnet $arguments
    }
    # Always build the tool with this version: its embedded manifest must match
    # the packages being released, including when packing a tag without rebuilding others.
    Invoke-Checked dotnet @('pack','tools/WorkloadInstaller/WorkloadInstaller.csproj',
        '-c',$Configuration,'--nologo',"-p:Version=$Version","-p:WorkloadFeatureBand=$FeatureBand","-p:RepositoryCommit=$sourceCommit")
    Invoke-Checked dotnet @('pack','src/OpenHarmony.NET.Workload/OpenHarmony.NET.Workload.csproj',
        '-c',$Configuration,'--nologo',"-p:Version=$Version","-p:WorkloadFeatureBand=$FeatureBand","-p:GeneratedManifestPath=$manifestPath","-p:RepositoryCommit=$sourceCommit")
    Write-Host "Workload packages: $(Join-Path $RepoRoot 'artifacts/packages'). Full installation also requires both runtime packages of version $Version (build.ps1 -Target Pack)."
}
finally { Pop-Location }
