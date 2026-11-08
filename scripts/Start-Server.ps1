[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Use-DevEnvironment.ps1')
Push-Location $quasarRoot
try {
    Invoke-QuasarCommand dotnet run --project src/Quasar.Server --launch-profile http
} finally { Pop-Location }
