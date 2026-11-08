[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Use-DevEnvironment.ps1')
Push-Location $quasarRoot
try {
    Invoke-QuasarCommand dotnet restore Quasar.slnx --locked-mode
    Invoke-QuasarCommand dotnet build Quasar.slnx --no-restore --configuration Release
    Invoke-QuasarCommand dotnet test Quasar.slnx --no-build --no-restore --configuration Release
    Invoke-QuasarCommand dotnet format Quasar.slnx --no-restore --verify-no-changes
    Push-Location 'src/Quasar.Client'
    try {
        Invoke-QuasarCommand npm.cmd run lint
        Invoke-QuasarCommand npm.cmd run check:renderers
        Invoke-QuasarCommand npm.cmd run build
    } finally { Pop-Location }
    Write-Host 'Verification complete: simulation/session behavior tests, formatting, frontend and renderer checks.'
} finally {
    Pop-Location
}
