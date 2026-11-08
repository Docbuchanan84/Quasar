# Dot-source this file: . .\scripts\Use-DevEnvironment.ps1
$ErrorActionPreference = 'Stop'
$quasarRoot = Split-Path -Parent $PSScriptRoot
$quasarSdkRoot = if ($env:QUASAR_DOTNET_ROOT) {
    $env:QUASAR_DOTNET_ROOT
} else {
    Join-Path $env:LOCALAPPDATA 'Quasar\dotnet'
}

if (Test-Path -LiteralPath (Join-Path $quasarSdkRoot 'dotnet.exe')) {
    $env:DOTNET_ROOT = $quasarSdkRoot
    if (($env:PATH -split ';') -notcontains $quasarSdkRoot) {
        $env:PATH = "$quasarSdkRoot;$env:PATH"
    }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Invoke-QuasarCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(ValueFromRemainingArguments)][string[]]$CommandArguments
    )
    & $Command @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}
