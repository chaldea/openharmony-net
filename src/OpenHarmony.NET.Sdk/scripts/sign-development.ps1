[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Toolchains,
    [Parameter(Mandatory=$true)][string]$JavaHome,
    [Parameter(Mandatory=$true)][string]$Device,
    [Parameter(Mandatory=$true)][string]$BundleName,
    [Parameter(Mandatory=$true)][string]$InputHap,
    [Parameter(Mandatory=$true)][string]$OutputHap,
    [Parameter(Mandatory=$true)][string]$SigningDirectory
)
# Invoked by hap.ps1, which supplies Invoke-Tool / Require-File. Only the public
# SDK development identity is used; certificates are bound to the chosen device.
$ErrorActionPreference = 'Stop'
$java = Join-Path $JavaHome 'bin/java.exe'
$keytool = Join-Path $JavaHome 'bin/keytool.exe'
$signTool = Join-Path $Toolchains 'lib/hap-sign-tool.jar'
$sdkKeyStore = Join-Path $Toolchains 'lib/OpenHarmony.p12'
Require-File $sdkKeyStore 'SDK development signing is unavailable; provide OpenHarmonySignedHapPath.'
New-Item -ItemType Directory -Force -Path $SigningDirectory | Out-Null
$keyStore = Join-Path $SigningDirectory 'development.p12'
Copy-Item -LiteralPath $sdkKeyStore -Destination $keyStore -Force
$chain = Join-Path $SigningDirectory 'app-chain.pem'
$rootCert = Join-Path $SigningDirectory 'root.cer'
$caCert = Join-Path $SigningDirectory 'ca.cer'
Invoke-Tool $keytool @('-exportcert','-rfc','-alias','openharmony application root ca','-keystore',$keyStore,'-storepass','123456','-file',$rootCert) | Write-Host
Invoke-Tool $keytool @('-exportcert','-rfc','-alias','openharmony application ca','-keystore',$keyStore,'-storepass','123456','-file',$caCert) | Write-Host
Invoke-Tool $java @('-jar',$signTool,'generate-app-cert','-keyAlias','openharmony application release',
    '-keyPwd','123456','-issuer','C=CN,O=OpenHarmony,OU=OpenHarmony Team,CN=OpenHarmony Application CA',
    '-issuerKeyAlias','openharmony application ca','-issuerKeyPwd','123456',
    '-subject','C=CN,O=OpenHarmony,OU=OpenHarmony Team,CN=Dotnet Development','-validity','30',
    '-signAlg','SHA256withECDSA','-keystoreFile',$keyStore,'-keystorePwd','123456',
    '-rootCaCertFile',$rootCert,'-subCaCertFile',$caCert,'-outForm','certChain','-outFile',$chain) | Write-Host
$certificates = [regex]::Matches([IO.File]::ReadAllText($chain), '(?s)-----BEGIN CERTIFICATE-----.*?-----END CERTIFICATE-----')
if ($certificates.Count -ne 3) { throw 'Expected a three-certificate development chain.' }
$hdc = Join-Path $Toolchains 'hdc.exe'
$udid = ((Invoke-Tool $hdc @('-t',$Device,'shell','bm','get','-u')) -split '\r?\n' |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^[0-9A-Fa-f]{64}$' } | Select-Object -First 1)
if ($udid -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Cannot read the target device UDID.' }
$profile = Get-Content (Join-Path $Toolchains 'lib/UnsgnedDebugProfileTemplate.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$profile.uuid = [guid]::NewGuid().ToString()
$profile.validity.'not-before' = [DateTimeOffset]::UtcNow.AddDays(-1).ToUnixTimeSeconds()
$profile.validity.'not-after' = [DateTimeOffset]::UtcNow.AddDays(30).ToUnixTimeSeconds()
$profile.'bundle-info'.'bundle-name' = $BundleName
$profile.'bundle-info'.'development-certificate' = $certificates[0].Value + "`n"
$profile.'debug-info'.'device-ids' = @($udid)
$profilePath = Join-Path $SigningDirectory 'profile.json'
[IO.File]::WriteAllText($profilePath, ($profile | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
$signedProfile = Join-Path $SigningDirectory 'profile.p7b'
Invoke-Tool $java @('-jar',$signTool,'sign-profile','-mode','localSign',
    '-keyAlias','openharmony application profile debug','-keyPwd','123456',
    '-profileCertFile',(Join-Path $Toolchains 'lib/OpenHarmonyProfileDebug.pem'),
    '-inFile',$profilePath,'-signAlg','SHA256withECDSA','-keystoreFile',$keyStore,
    '-keystorePwd','123456','-outFile',$signedProfile) | Write-Host
Invoke-Tool $java @('-jar',$signTool,'sign-app','-mode','localSign',
    '-keyAlias','openharmony application release','-keyPwd','123456','-appCertFile',$chain,
    '-profileFile',$signedProfile,'-inFile',$InputHap,
    '-signAlg','SHA256withECDSA','-keystoreFile',$keyStore,'-keystorePwd','123456',
    '-outFile',$OutputHap,'-compatibleVersion','15') | Write-Host
Invoke-Tool $java @('-jar',$signTool,'verify-app','-inFile',$OutputHap,
    '-outCertChain',(Join-Path $SigningDirectory 'verified-chain.cer'),
    '-outProfile',(Join-Path $SigningDirectory 'verified-profile.p7b')) | Write-Host
