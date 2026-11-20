$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path $PSScriptRoot -Parent
$sdkVersion = (Get-Content -Raw (Join-Path $RepoRoot 'global.json') | ConvertFrom-Json).sdk.version
foreach ($directory in @('artifacts/dotnet-sdk11', 'artifacts/dotnet-sdk')) {
    $localDotnet = Join-Path $RepoRoot $directory
    if ((Test-Path (Join-Path $localDotnet 'dotnet.exe')) -and
        (Test-Path (Join-Path $localDotnet "sdk/$sdkVersion"))) {
        $env:PATH = "$localDotnet;$env:PATH"
        break
    }
}

function Invoke-Checked {
    param([Parameter(Mandatory=$true)][string]$Command, [string[]]$Arguments = @())
    # Windows PowerShell treats native stderr (including ordinary compiler
    # warnings) as ErrorRecord objects. The native exit code is authoritative.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Command @Arguments
        $nativeExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    if ($nativeExitCode -ne 0) {
        throw "$Command failed with exit code $nativeExitCode."
    }
}
