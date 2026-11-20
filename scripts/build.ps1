param([ValidateSet('all', 'windows', 'android')][string]$Platform = 'all')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'GloveGo.Tools.psm1') -Force -DisableNameChecking
try { Invoke-GloveBuild $Platform } catch { Write-Error $_; exit 1 }
