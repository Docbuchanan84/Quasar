[CmdletBinding()]
param([ValidateRange(1024, 65535)][int]$LocalPort = 5080)

. (Join-Path $PSScriptRoot 'Use-DevEnvironment.ps1')
$hostRoot = Join-Path $env:LOCALAPPDATA 'Quasar\host'
$release = Join-Path $hostRoot ('releases\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
Push-Location $quasarRoot
try {
    Push-Location 'src/Quasar.Client'
    try {
        Invoke-QuasarCommand npm.cmd ci
        Invoke-QuasarCommand npm.cmd run build
    } finally { Pop-Location }
    Invoke-QuasarCommand dotnet restore Quasar.slnx --locked-mode
    Invoke-QuasarCommand dotnet publish src/Quasar.Server/Quasar.Server.csproj --no-restore --configuration Release --output $release
    $webRoot = Join-Path $release 'wwwroot'
    New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
    Copy-Item -Path 'src/Quasar.Client/dist/*' -Destination $webRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-HostedApp.ps1') -Destination (Join-Path $hostRoot 'Run-HostedApp.ps1') -Force
    $deployment = [ordered]@{
        publishDirectory = $release
        assemblyPath = Join-Path $release 'Quasar.Server.dll'
        dotnetPath = (Get-Command dotnet).Source
        localPort = $LocalPort
        publishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    $deployment | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hostRoot 'deployment.json') -Encoding utf8
    Write-Host "Published Quasar to $release"
    Write-Host 'Stop the previous host if running, then run .\scripts\Start-HostedApp.ps1. Keep that terminal open.'
} finally { Pop-Location }
