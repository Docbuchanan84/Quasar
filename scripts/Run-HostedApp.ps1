# Foreground runtime wrapper also copied outside the OneDrive checkout.
$ErrorActionPreference = 'Stop'
$hostRoot = Join-Path $env:LOCALAPPDATA 'Quasar\host'
$deployment = Get-Content -LiteralPath (Join-Path $hostRoot 'deployment.json') -Raw | ConvertFrom-Json
$logPath = Join-Path $hostRoot 'host.log'
if ((Test-Path -LiteralPath $logPath) -and (Get-Item -LiteralPath $logPath).Length -gt 5MB) {
    Move-Item -LiteralPath $logPath -Destination (Join-Path $hostRoot 'host.previous.log') -Force
}
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:DOTNET_ROOT = Split-Path -Parent $deployment.dotnetPath
$env:Logging__LogLevel__Default = 'Warning'
Set-Location -LiteralPath $deployment.publishDirectory
Write-Host "Starting Quasar at http://127.0.0.1:$($deployment.localPort). Keep this terminal open; stop with Ctrl+C."
Write-Host "Runtime log: $logPath"
& $deployment.dotnetPath $deployment.assemblyPath --urls "http://127.0.0.1:$($deployment.localPort)" >> $logPath 2>&1
exit $LASTEXITCODE
