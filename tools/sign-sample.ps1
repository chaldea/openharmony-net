[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CommandLineTools,
    [Parameter(Mandatory=$true)][string]$JavaHome,
    [string]$Device = '127.0.0.1:5555'
)
. (Join-Path $PSScriptRoot 'common.ps1')
$toolchains = Join-Path $CommandLineTools 'sdk/default/openharmony/toolchains'
$java = Join-Path $JavaHome 'bin/java.exe'
$keytool = Join-Path $JavaHome 'bin/keytool.exe'
$signTool = Join-Path $toolchains 'lib/hap-sign-tool.jar'
$sdkKeyStore = Join-Path $toolchains 'lib/OpenHarmony.p12'
$output = Join-Path $RepoRoot 'artifacts/sample'
$signing = Join-Path $output 'signing'
New-Item -ItemType Directory -Force -Path $signing | Out-Null
$keyStore = Join-Path $signing 'development.p12'
Copy-Item -LiteralPath $sdkKeyStore -Destination $keyStore -Force
# Public development identity distributed with the official SDK. This is only
# for the local emulator sample, never a production signing identity.
$chain = Join-Path $signing 'app-chain.pem'
$rootCert = Join-Path $signing 'root.cer'
$caCert = Join-Path $signing 'ca.cer'
Invoke-Checked $keytool @('-exportcert','-rfc','-alias','openharmony application root ca','-keystore',$keyStore,'-storepass','123456','-file',$rootCert)
Invoke-Checked $keytool @('-exportcert','-rfc','-alias','openharmony application ca','-keystore',$keyStore,'-storepass','123456','-file',$caCert)
Invoke-Checked $java @('-jar',$signTool,'generate-app-cert','-keyAlias','openharmony application release',
    '-keyPwd','123456','-issuer','C=CN,O=OpenHarmony,OU=OpenHarmony Team,CN=OpenHarmony Application CA',
    '-issuerKeyAlias','openharmony application ca','-issuerKeyPwd','123456',
    '-subject','C=CN,O=OpenHarmony,OU=OpenHarmony Team,CN=Dotnet Smoke Debug','-validity','30',
    '-signAlg','SHA256withECDSA','-keystoreFile',$keyStore,'-keystorePwd','123456',
    '-rootCaCertFile',$rootCert,'-subCaCertFile',$caCert,'-outForm','certChain','-outFile',$chain)
$certificates = [regex]::Matches((Get-Content $chain -Raw), '(?s)-----BEGIN CERTIFICATE-----.*?-----END CERTIFICATE-----')
if ($certificates.Count -ne 3) { throw 'Expected a three-certificate development chain.' }
$hdc = Join-Path $toolchains 'hdc.exe'
$udid = (& $hdc -t $Device shell bm get -u | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^[0-9A-Fa-f]{64}$' } | Select-Object -First 1)
if ($LASTEXITCODE -ne 0 -or $udid -notmatch '^[0-9A-Fa-f]{64}$') { throw "Cannot read emulator UDID: $udid" }
$profile = Get-Content (Join-Path $toolchains 'lib/UnsgnedDebugProfileTemplate.json') -Raw | ConvertFrom-Json
$profile.uuid = [guid]::NewGuid().ToString()
$profile.validity.'not-before' = [DateTimeOffset]::UtcNow.AddDays(-1).ToUnixTimeSeconds()
$profile.validity.'not-after' = [DateTimeOffset]::UtcNow.AddDays(30).ToUnixTimeSeconds()
$profile.'bundle-info'.'bundle-name' = 'org.openharmony.dotnet.smoke'
$profile.'bundle-info'.'development-certificate' = $certificates[0].Value + "`n"
$profile.'debug-info'.'device-ids' = @($udid)
$profilePath = Join-Path $signing 'profile.json'
[IO.File]::WriteAllText($profilePath, ($profile | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
$signedProfile = Join-Path $signing 'profile.p7b'
Invoke-Checked $java @('-jar',$signTool,'sign-profile','-mode','localSign',
    '-keyAlias','openharmony application profile debug','-keyPwd','123456',
    '-profileCertFile',(Join-Path $toolchains 'lib/OpenHarmonyProfileDebug.pem'),
    '-inFile',$profilePath,'-signAlg','SHA256withECDSA','-keystoreFile',$keyStore,
    '-keystorePwd','123456','-outFile',$signedProfile)
$signedHap = Join-Path $output 'NativeAotSmoke-signed.hap'
Invoke-Checked $java @('-jar',$signTool,'sign-app','-mode','localSign',
    '-keyAlias','openharmony application release','-keyPwd','123456','-appCertFile',$chain,
    '-profileFile',$signedProfile,'-inFile',(Join-Path $output 'NativeAotSmoke-unsigned.hap'),
    '-signAlg','SHA256withECDSA','-keystoreFile',$keyStore,'-keystorePwd','123456',
    '-outFile',$signedHap,'-compatibleVersion','15')
Invoke-Checked $java @('-jar',$signTool,'verify-app','-inFile',$signedHap,
    '-outCertChain',(Join-Path $signing 'verified-chain.cer'),'-outProfile',(Join-Path $signing 'verified-profile.p7b'))
