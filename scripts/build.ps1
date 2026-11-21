param([ValidateSet('all', 'windows', 'android')][string]$Platform = 'all')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'BeckerBearings.Tools.psm1') -Force -DisableNameChecking
try { Invoke-BearingsBuild $Platform } catch { Write-Error $_; exit 1 }
