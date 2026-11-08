[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$hostRoot = Join-Path $env:LOCALAPPDATA 'Quasar\host'
$running = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" | Where-Object {
    $_.CommandLine -like "*$hostRoot\releases\*Quasar.Server.dll*"
}
foreach ($process in $running) { Stop-Process -Id $process.ProcessId -ErrorAction SilentlyContinue }
Write-Host 'Published Quasar host stopped. Tailscale routes were not modified.'
