param([ValidateSet('all', 'windows', 'android')][string]$Platform = 'all')

& (Join-Path $PSScriptRoot 'scripts/build.ps1') $Platform
exit $LASTEXITCODE
