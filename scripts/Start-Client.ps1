[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Use-DevEnvironment.ps1')
Push-Location (Join-Path $quasarRoot 'src/Quasar.Client')
try { Invoke-QuasarCommand npm.cmd run dev } finally { Pop-Location }
