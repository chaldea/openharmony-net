[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SettingsPath)
$ErrorActionPreference = 'Stop'
$settings = Get-Content -Raw -Encoding UTF8 -LiteralPath $SettingsPath | ConvertFrom-Json
$arguments = @('msbuild', $settings.ProjectPath, '-target:OpenHarmonyRun', '-nologo')
foreach ($property in $settings.Properties.PSObject.Properties) {
    # MSBuild uses semicolons/commas as property separators, even inside quotes.
    $value = [regex]::Replace([string]$property.Value, '[%$@();,?*'']', { param($match) '%' + ([int][char]$match.Value).ToString('X2') })
    $arguments += "-property:$($property.Name)=$value"
}
& $settings.DotnetPath @arguments
exit $LASTEXITCODE
