[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Use-DevEnvironment.ps1')
Push-Location $quasarRoot
try {
    $requiredSdk = (Get-Content global.json -Raw | ConvertFrom-Json).sdk.version
    $sdkAvailable = $false
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $selectedSdk = & dotnet --version 2>$null
        # Running in the repo means global.json already enforces version policy.
        $sdkAvailable = $LASTEXITCODE -eq 0
    }
    if (-not $sdkAvailable) {
        if (-not $IsWindows -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
            throw 'Automatic SDK installation currently supports Windows x64 only.'
        }
        if (Test-Path -LiteralPath $quasarSdkRoot) {
            throw "An incomplete or incompatible SDK exists at $quasarSdkRoot. Inspect it before reinstalling."
        }
        Write-Host "Installing .NET $requiredSdk into $quasarSdkRoot (user-local only)."
        $metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
        $sdks = @($metadata.releases | ForEach-Object { $_.sdks })
        $sdk = $sdks | Where-Object version -eq $requiredSdk | Select-Object -First 1
        $archive = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
        if (-not $archive -or $archive.url -notlike 'https://builds.dotnet.microsoft.com/*') {
            throw 'The pinned SDK is unavailable from the expected Microsoft source.'
        }
        $archivePath = Join-Path $env:TEMP ("quasar-sdk-" + [guid]::NewGuid().ToString('N') + '.zip')
        try {
            Invoke-WebRequest -Uri $archive.url -OutFile $archivePath
            if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash -ne $archive.hash) {
                throw 'SDK SHA-512 verification failed; archive will not be extracted.'
            }
            New-Item -ItemType Directory -Path $quasarSdkRoot | Out-Null
            Expand-Archive -LiteralPath $archivePath -DestinationPath $quasarSdkRoot
        } finally {
            if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath }
        }
        . (Join-Path $PSScriptRoot 'Use-DevEnvironment.ps1')
    }
    Invoke-QuasarCommand dotnet --version
    Invoke-QuasarCommand node --version
    Invoke-QuasarCommand npm.cmd --version
    Invoke-QuasarCommand dotnet restore Quasar.slnx --locked-mode
    Push-Location 'src/Quasar.Client'
    try { Invoke-QuasarCommand npm.cmd ci } finally { Pop-Location }
    Write-Host 'Setup complete. Run .\scripts\Verify.ps1 next.'
} finally {
    Pop-Location
}
