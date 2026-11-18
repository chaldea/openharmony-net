[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Package','Run')][string]$Action,
    [Parameter(Mandatory=$true)][string]$SettingsPath
)
$ErrorActionPreference = 'Stop'
$settings = Get-Content -Raw -Encoding UTF8 -LiteralPath $SettingsPath | ConvertFrom-Json

function Invoke-Tool {
    param([string]$Executable, [string[]]$Arguments)
    $oldPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & $Executable @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $oldPreference }
    $text = (($output | ForEach-Object { $_.ToString() }) -join "`n").Trim()
    if ($exitCode -ne 0) { throw "$Executable exited with ${exitCode}: $text" }
    return $text
}

function Require-File([string]$Path, [string]$Hint) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Hint Missing file: $Path" }
}

if (!$settings.CommandLineTools) { throw 'Set OpenHarmonyCommandLineTools or OHOS_COMMAND_LINE_TOOLS to the official command-line-tools directory.' }
if (!$settings.JavaHome) { throw 'Set OpenHarmonyJavaHome or JAVA_HOME to a JDK 17+ directory.' }
$cli = [IO.Path]::GetFullPath($settings.CommandLineTools)
$toolchains = Join-Path $cli 'sdk/default/openharmony/toolchains'
$java = Join-Path $settings.JavaHome 'bin/java.exe'
Require-File $java 'Set OpenHarmonyJavaHome or JAVA_HOME to a JDK 17+ directory.'
$oldJava = $env:JAVA_HOME
$oldSdk = $env:DEVECO_SDK_HOME
$oldPath = $env:PATH
try {
    $env:JAVA_HOME = $settings.JavaHome
    $env:DEVECO_SDK_HOME = Join-Path $cli 'sdk'
    $env:PATH = "$(Join-Path $cli 'tool/node');$(Join-Path $settings.JavaHome 'bin');$env:PATH"
    if ($Action -eq 'Package') {
        $ohpm = Join-Path $cli 'bin/ohpm.bat'
        $hvigor = Join-Path $cli 'bin/hvigorw.bat'
        Require-File $ohpm 'Install the official HarmonyOS command-line tools.'
        Require-File $hvigor 'Install the official HarmonyOS command-line tools.'
        $stage = Join-Path $settings.WorkDirectory 'Stage'
        $template = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../templates/Stage'))
        foreach ($file in Get-ChildItem -LiteralPath $template -Recurse -File) {
            $relative = $file.FullName.Substring($template.Length).TrimStart('\', '/')
            $destination = Join-Path $stage $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
            if ($file.Extension -eq '.png') { Copy-Item -LiteralPath $file.FullName -Destination $destination -Force }
            else {
                $text = [IO.File]::ReadAllText($file.FullName).Replace('@BUNDLE_NAME@', $settings.BundleName).Replace('@ASSEMBLY_NAME@', $settings.AssemblyName).Replace('@ABI@', $settings.Abi)
                if (!(Test-Path -LiteralPath $destination) -or [IO.File]::ReadAllText($destination) -ne $text) {
                    [IO.File]::WriteAllText($destination, $text, (New-Object Text.UTF8Encoding $false))
                }
            }
        }
        $libs = Join-Path $stage "entry/libs/$($settings.Abi)"
        New-Item -ItemType Directory -Force -Path $libs | Out-Null
        Require-File (Join-Path $settings.PublishDirectory "$($settings.AssemblyName).so") 'NativeAOT output was not generated.'
        Get-ChildItem -LiteralPath $settings.PublishDirectory -File | Where-Object { $_.Name -match '\.so(\.|$)' } |
            Copy-Item -Destination $libs -Force
        Push-Location $stage
        try {
            Invoke-Tool $ohpm @('install') | Write-Host
            Invoke-Tool $hvigor @('--mode','module','-p','product=default','-p','module=entry@default',
                '-p',"buildMode=$($settings.BuildMode)",'assembleHap','--no-daemon') | Write-Host
        }
        finally { Pop-Location }
        $hap = Join-Path $stage 'entry/build/default/outputs/default/entry-default-unsigned.hap'
        Require-File $hap 'Hvigor did not generate a HAP.'
        New-Item -ItemType Directory -Force -Path (Split-Path $settings.HapPath -Parent) | Out-Null
        Copy-Item -LiteralPath $hap -Destination $settings.HapPath -Force
        Write-Host "OpenHarmony HAP: $($settings.HapPath)"
    }
    else {
        $hdc = Join-Path $toolchains 'hdc.exe'
        Require-File $hdc 'Install the HarmonyOS toolchains component.'
        $targets = @((Invoke-Tool $hdc @('list','targets')) -split '\r?\n' | ForEach-Object { $_.Trim() } |
            Where-Object { $_ -and $_ -ne '[Empty]' })
        $device = $settings.Device
        if (!$device) {
            if ($targets.Count -ne 1) { throw 'Connect one device or start an emulator; with multiple devices set OpenHarmonyDevice to an HDC target.' }
            $device = $targets[0]
        }
        if ($device -notin $targets) { throw "HDC target '$device' is not connected. Start the emulator or connect the device." }
        $architecture = Invoke-Tool $hdc @('-t',$device,'shell','uname','-m')
        $expected = if ($settings.Abi -eq 'x86_64') { 'x86_64' } else { 'aarch64' }
        if ($architecture.Trim() -ne $expected) { throw "Device architecture is $architecture. Set OpenHarmonyAbi to match the device (x86_64 or arm64-v8a)." }
        $signedHap = $settings.SignedHapPath
        if (!$signedHap) {
            if ($settings.DevelopmentSigning -ne 'true') { throw 'Set OpenHarmonySignedHapPath to a signed HAP, or use a Debug build for SDK development signing.' }
            $signedHap = Join-Path $settings.PublishDirectory "$($settings.AssemblyName)-signed.hap"
            & (Join-Path $PSScriptRoot 'sign-development.ps1') -Toolchains $toolchains -JavaHome $settings.JavaHome `
                -Device $device -BundleName $settings.BundleName -InputHap $settings.HapPath -OutputHap $signedHap `
                -SigningDirectory (Join-Path $settings.WorkDirectory 'signing')
        }
        Require-File $signedHap 'The signed HAP was not found.'
        $install = Invoke-Tool $hdc @('-t',$device,'install','-r',$signedHap)
        if ($install -notmatch 'successfully') { throw "HAP installation failed: $install" }
        $install | Write-Host
        Invoke-Tool $hdc @('-t',$device,'shell','aa','force-stop',$settings.BundleName) | Write-Host
        $start = Invoke-Tool $hdc @('-t',$device,'shell','aa','start','-a','EntryAbility','-b',$settings.BundleName)
        if ($start -notmatch 'success') { throw "Ability start failed: $start" }
        $start | Write-Host
        Write-Host "Started $($settings.BundleName) on $device."
    }
}
finally {
    $env:JAVA_HOME = $oldJava
    $env:DEVECO_SDK_HOME = $oldSdk
    $env:PATH = $oldPath
}
