[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$DotnetRoot,
    [Parameter(Mandatory=$true)][string]$CommandLineTools,
    [Parameter(Mandatory=$true)][string]$JavaHome,
    [string]$PackageSource,
    [string]$Version = '11.0.0-ohos.1-preview.1',
    [string]$Device = '127.0.0.1:5555',
    [switch]$RunOnDevice,
    [switch]$KeepInstalled
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$DotnetRoot = [IO.Path]::GetFullPath($DotnetRoot)
$executableExtension = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
$dotnet = Join-Path $DotnetRoot "dotnet$executableExtension"
if (!$PackageSource) { $PackageSource = Join-Path $repository 'artifacts/packages' }
if ($PackageSource -notmatch '^https?://') { $PackageSource = [IO.Path]::GetFullPath($PackageSource) }
$sdkVersion = (& $dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^11\.0\.(\d+)$') { throw 'Select a .NET 11 SDK.' }
$featureBand = '11.0.' + ([int][Math]::Floor([int]$Matches[1] / 100) * 100)
$records = @((Join-Path $DotnetRoot "metadata/workloads/X64/$featureBand/InstalledWorkloads/harmonyos"),
    (Join-Path $DotnetRoot "metadata/workloads/Arm64/$featureBand/InstalledWorkloads/harmonyos"),
    (Join-Path $DotnetRoot "metadata/workloads/$featureBand/InstalledWorkloads/harmonyos"))
if ((Test-Path -LiteralPath $records) -contains $true) { throw 'Use a standalone SDK without harmonyos installed. This verification exercises install/uninstall.' }
$runRoot = Join-Path $repository ('artifacts/wl-test/' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$projectRoot = Join-Path $runRoot 'app'
$project = Join-Path $projectRoot 'WorkloadSmoke.csproj'
$oldEnvironment = @{}
foreach ($name in @('PATH','DOTNET_ROOT','DOTNET_ROOT_X64','DOTNET_ROOT_ARM64','DOTNET_CLI_HOME','NUGET_PACKAGES','OHOS_COMMAND_LINE_TOOLS','OHOS_SDK_ROOT','JAVA_HOME','DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE')) {
    $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$installed = $false

function Invoke-Dotnet {
    param([string]$Name, [string[]]$Arguments, [string]$ExpectedFailure, [string]$Executable = $dotnet)
    $log = Join-Path $runRoot "$Name.log"
    if ($ExpectedFailure) { Write-Host "Verify negative test '$Name' (command must fail with the expected diagnostic)." }
    else { Write-Host "Verify: $(Split-Path $Executable -Leaf) $($Arguments -join ' ')" }
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Executable @Arguments *> $log
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    $text = Get-Content -Raw -Encoding UTF8 $log
    if ($ExpectedFailure) {
        if ($code -eq 0 -or $text -notmatch $ExpectedFailure) {
            Write-Host $text
            throw "Expected failure '$ExpectedFailure' was not observed. See $log"
        }
        # Do not feed expected compiler errors into GitHub's problem matcher.
        Write-Host "PASS: negative test '$Name' observed the expected diagnostic (exit code $code)."
    }
    elseif ($code -ne 0) {
        Write-Host $text
        throw "dotnet exited with $code. See $log"
    }
}

function Assert-Hap {
    param([string]$Abi, [string]$Rid, [int]$Machine)
    $hap = Join-Path $projectRoot "bin/Release/net11.0-ohos/$Rid/publish/WorkloadSmoke-unsigned.hap"
    if (!(Test-Path -LiteralPath $hap)) { throw "HAP missing: $hap" }
    $zip = [IO.Compression.ZipFile]::OpenRead($hap)
    try {
        foreach ($name in @('WorkloadSmoke.so','libc++_shared.so','libopenharmony_framework.so')) {
            if (!$zip.GetEntry("libs/$Abi/$name")) { throw "$hap is missing $name" }
        }
        $native = @($zip.Entries | Where-Object { $_.FullName -match '^libs/.*\.so(\.|$)' })
        foreach ($entry in $native) {
            if (!$entry.FullName.StartsWith("libs/$Abi/")) { throw "Unexpected ABI: $($entry.FullName)" }
            $stream = $entry.Open()
            try {
                $bytes = New-Object byte[] 20
                $read = 0
                while ($read -lt 20) {
                    $count = $stream.Read($bytes, $read, 20 - $read)
                    if (!$count) { throw "Truncated ELF: $($entry.FullName)" }
                    $read += $count
                }
                if ($bytes[0] -ne 127 -or [Text.Encoding]::ASCII.GetString($bytes,1,3) -ne 'ELF' -or
                    $bytes[4] -ne 2 -or $bytes[5] -ne 1 -or [BitConverter]::ToUInt16($bytes,18) -ne $Machine) {
                    throw "Wrong ELF architecture: $($entry.FullName)"
                }
            }
            finally { $stream.Dispose() }
        }
        return @{ abi = $Abi; hap = $hap; nativeLibraries = $native.Count; sha256 = (Get-FileHash $hap -Algorithm SHA256).Hash }
    }
    finally { $zip.Dispose() }
}

try {
    $env:PATH = @($DotnetRoot, (Join-Path $CommandLineTools 'bin'), $env:PATH) -join [IO.Path]::PathSeparator
    $env:DOTNET_ROOT = $null
    $env:DOTNET_ROOT_X64 = $null
    $env:DOTNET_ROOT_ARM64 = $null
    $env:DOTNET_CLI_HOME = Join-Path $runRoot 'cli-home'
    $env:NUGET_PACKAGES = Join-Path $runRoot 'nuget'
    $env:OHOS_COMMAND_LINE_TOOLS = $null
    $env:OHOS_SDK_ROOT = $null
    $env:JAVA_HOME = $JavaHome
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    # Stop repository-level MSBuild settings and package sources from leaking in.
    [IO.File]::WriteAllText((Join-Path $runRoot 'Directory.Build.props'), '<Project />')
    [IO.File]::WriteAllText((Join-Path $runRoot 'Directory.Build.targets'), '<Project />')
    $escapedSource = [Security.SecurityElement]::Escape($PackageSource)
    [IO.File]::WriteAllText((Join-Path $runRoot 'NuGet.Config'), "<configuration><packageSources><clear/><add key='workload' value='$escapedSource'/><add key='nuget.org' value='https://api.nuget.org/v3/index.json'/></packageSources></configuration>")
    @{ sdk = @{ version = $sdkVersion; rollForward = 'disable' } } | ConvertTo-Json | Set-Content (Join-Path $runRoot 'global.json')
    $toolPath = Join-Path $runRoot 'tools'
    Invoke-Dotnet 'tool-install' @('tool','install','Chaldea.OpenHarmony.Workload','--tool-path',$toolPath,
        '--version',$Version,'--source',$PackageSource,'--configfile',(Join-Path $runRoot 'NuGet.Config'))
    $ohos = Join-Path $toolPath $(if ($executableExtension) { 'ohos.cmd' } else { 'ohos' })
    Invoke-Dotnet 'tool-command' @('--version') -Executable $ohos
    Invoke-Dotnet 'install' @('workload','install','--source',$PackageSource) -Executable $ohos
    $installed = $true
    Invoke-Dotnet 'list' @('workload','list') -Executable $ohos
    if ((Get-Content -Raw (Join-Path $runRoot 'list.log')) -notmatch '\bharmonyos\b') { throw 'harmonyos is absent from workload list.' }
    Invoke-Dotnet 'new' @('new','ohos','-n','WorkloadSmoke','-o',$projectRoot)
    Invoke-Dotnet 'missing-sdk' @('build',$project,"-p:OpenHarmonySdkRoot=$runRoot/missing-sdk") 'OHOS1001.*missing-sdk'
    Invoke-Dotnet 'invalid-abi' @('build',$project,'-p:OpenHarmonyAbi=invalid') 'OpenHarmonyAbi must be'
    Invoke-Dotnet 'build' @('build',$project)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Invoke-Dotnet 'publish-x64' @('publish',$project)
    $x64 = Assert-Hap 'x86_64' 'linux-musl-x64' 62
    Invoke-Dotnet 'publish-arm64' @('publish',$project,'-p:OpenHarmonyAbi=arm64-v8a')
    $arm64 = Assert-Hap 'arm64-v8a' 'linux-musl-arm64' 183
    $deviceResult = $null
    if ($RunOnDevice) {
        Invoke-Dotnet 'run' @('run','--project',$project,"-p:OpenHarmonyDevice=$Device",'-p:OpenHarmonyBundleName=org.openharmony.dotnet.workloadsmoke')
        $hdc = Join-Path $CommandLineTools "sdk/default/openharmony/toolchains/hdc$executableExtension"
        $deadline = (Get-Date).AddSeconds(60)
        $rendered = $false
        do {
            $appPid = (& $hdc -t $Device shell pidof org.openharmony.dotnet.workloadsmoke | Out-String).Trim()
            $log = ''
            if ($appPid -match '^\d+$') { $log = (& $hdc -t $Device shell hilog -x -T OpenHarmony -P $appPid | Out-String) }
            $rendered = $log -match 'OH_FRAMEWORK created runtime=11 backend=OpenGL' -and $log -match 'OH_HARMONY first_frame=1'
            if ($rendered) { break }
            Start-Sleep -Seconds 2
        } while ((Get-Date) -lt $deadline)
        $log | Set-Content (Join-Path $runRoot 'hilog.log')
        if (!$rendered) { throw 'The installed application did not render a Framework frame on .NET 11.' }
        $deviceResult = @{ target = $Device; runtimeMajor = 11; backend = 'OpenGL'; firstFrame = $true }
    }
    Invoke-Dotnet 'uninstall' @('workload','uninstall') -Executable $ohos
    $installed = $false
    if ((Test-Path -LiteralPath $records) -contains $true) { throw 'Workload installation record remains after uninstall.' }
    if (Test-Path (Join-Path $DotnetRoot "sdk-manifests/$featureBand/chaldea.openharmony.workload")) { throw 'Manifest remains after uninstall.' }
    Invoke-Dotnet 'tool-register' @('workload','register','--dotnet-root',$DotnetRoot) -Executable $ohos
    Invoke-Dotnet 'missing-workload' @('build',$project,'--no-restore') 'NETSDK1147'
    Invoke-Dotnet 'tool-unregister' @('workload','unregister') -Executable $ohos
    Invoke-Dotnet 'tool-reregister' @('workload','register') -Executable $ohos
    Invoke-Dotnet 'workload-restore' @('workload','restore',$project,'--source',$PackageSource,'--skip-manifest-update')
    $installed = $true
    Invoke-Dotnet 'build-restored' @('build',$project)
    Invoke-Dotnet 'update' @('workload','update','--source',$PackageSource) -Executable $ohos
    $result = @{ sdk = $sdkVersion; featureBand = $featureBand; version = $Version; packageSource = $PackageSource;
        x64 = $x64; arm64 = $arm64; device = $deviceResult; success = $true }
    $resultJson = $result | ConvertTo-Json -Depth 6
    $resultJson | Set-Content (Join-Path $runRoot 'result.json')
    # CI retains the verification result in the job log; no HAP/log artifact is needed.
    Write-Host $resultJson
    Write-Host "Workload verification passed. Evidence: $runRoot"
}
finally {
    try {
        if ($installed -and !$KeepInstalled -and (Test-Path -LiteralPath $records) -contains $true) {
            Invoke-Dotnet 'cleanup-uninstall' @('workload','uninstall') -Executable $ohos
        }
    }
    finally {
        foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name], 'Process') }
    }
}
