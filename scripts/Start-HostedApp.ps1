[CmdletBinding()]
param()

# Foreground by design: no detached process, execution-policy override, or task.
& (Join-Path $PSScriptRoot 'Run-HostedApp.ps1')
