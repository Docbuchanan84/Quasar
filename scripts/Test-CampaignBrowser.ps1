[CmdletBinding()]
param([string]$PreviousServerAssembly)

. (Join-Path $PSScriptRoot 'Use-DevEnvironment.ps1')
$run = [IO.Directory]::CreateTempSubdirectory('quasar-browser-').FullName
$settings = Join-Path $run 'settings.json'
'{"autosave":{"enabled":false},"saveOnShutdown":false}' | Set-Content -LiteralPath $settings
$assembly = Join-Path $quasarRoot 'src/Quasar.Server/bin/Release/net10.0/Quasar.Server.dll'
if (!$PreviousServerAssembly) { $PreviousServerAssembly = $assembly }
$PreviousServerAssembly = (Resolve-Path -LiteralPath $PreviousServerAssembly).Path
$dist = Join-Path $quasarRoot 'src/Quasar.Client/dist'
$scenario = Join-Path $PSScriptRoot 'playwright/campaigns.js'
$session = Split-Path -Leaf $run
Write-Host "Disposable campaign browser artifacts: $run"
$null = Get-Command sqlite3.exe
$dotnet = (Get-Command dotnet).Source
$hostProcess = $null
$address = $null

function Start-DisposableHost([string]$Binary, [string]$DataRoot) {
    $script:address = $null
    $stdout = Join-Path $run 'host.log'
    $stderr = Join-Path $run 'host-error.log'
    $script:hostProcess = Start-Process -FilePath $dotnet -WindowStyle Hidden -PassThru `
        -WorkingDirectory $run -RedirectStandardOutput $stdout -RedirectStandardError $stderr `
        -ArgumentList @(
            ('"{0}"' -f $Binary), '--urls', 'http://127.0.0.1:0',
            '--webroot', ('"{0}"' -f $dist),
            '--Persistence:DataRoot', ('"{0}"' -f $DataRoot),
            '--Persistence:SettingsFile', ('"{0}"' -f $settings)
        )
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $line = Select-String -LiteralPath $stdout -Pattern 'Now listening on: (http://127\.0\.0\.1:\d+)' |
            Select-Object -First 1
        if ($line) {
            $script:address = $line.Matches[0].Groups[1].Value
            return
        }
        if ($hostProcess.HasExited) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Disposable host did not start. See $stdout and $stderr."
}

function Stop-DisposableHost {
    if ($hostProcess) {
        if (!$hostProcess.HasExited) { $hostProcess.Kill() }
        if (!$hostProcess.WaitForExit(10000)) { throw 'Disposable host did not exit.' }
        $hostProcess.Dispose()
        $script:hostProcess = $null
    }
}

function Invoke-BrowserPhase([string]$Phase, [switch]$Mobile) {
    $cli = @('--yes', '--package', '@playwright/cli@0.1.23', 'playwright-cli', "-s=$session")
    $device = if ($Mobile) { @('--device', 'iPhone 17 Pro') } else { @() }
    try {
        Invoke-QuasarCommand npx.cmd @cli open "$address/#$Phase" @device
        $result = & npx.cmd @cli run-code --filename $scenario
        $exitCode = $LASTEXITCODE
        if ($exitCode -or !($result | Select-String -SimpleMatch ('"CAMPAIGN_BROWSER_OK ' + $Phase + '"'))) {
            $result | Write-Host
            throw "Browser phase failed: $Phase"
        }
        Write-Host "Browser phase passed: $Phase"
    } finally {
        & npx.cmd @cli close | Out-Null
    }
}

function Read-Json([string]$Path) {
    Invoke-RestMethod -Uri "$address$Path" -TimeoutSec 5
}

function Assert-Midflight($Before, $BeforeShip) {
    $after = Read-Json '/api/lab'
    $ship = Read-Json ('/api/lab/spacecraft/state?atTicks=' + $after.world.elapsedTicks)
    if ($after.running -or $after.instanceId -eq $Before.instanceId -or
        ($after.world | ConvertTo-Json -Depth 30 -Compress) -cne
        ($Before.world | ConvertTo-Json -Depth 30 -Compress) -or
        ($ship | ConvertTo-Json -Depth 30 -Compress) -cne
        ($BeforeShip | ConvertTo-Json -Depth 30 -Compress)) {
        throw 'Exact paused world and trajectory were not restored under a new instance.'
    }
    $after
}

Push-Location $quasarRoot
try {
    $data = Join-Path $run 'original'
    Start-DisposableHost $PreviousServerAssembly $data
    Invoke-BrowserPhase create -Mobile
    $before = Read-Json '/api/lab'
    $beforeStatus = Read-Json '/api/campaigns/status'
    $beforeShip = Read-Json ('/api/lab/spacecraft/state?atTicks=' + $before.world.elapsedTicks)
    if ($before.world.elapsedTicks -ne '108000000000' -or
        $before.world.trajectory.arrivalTicks -ne '216000000000' -or
        $beforeStatus.lastCheckpoint.label -ne 'Midflight' -or $beforeStatus.dirty) {
        throw 'Expected a committed named save exactly halfway to Iris.'
    }
    Stop-DisposableHost

    Start-DisposableHost $assembly $data
    $restarted = Assert-Midflight $before $beforeShip
    Invoke-BrowserPhase resume -Mobile
    $null = Assert-Midflight $restarted $beforeShip
    if (@(Read-Json '/api/campaigns').Count -ne 1) { throw 'Cancelled New Game created a campaign.' }
    Invoke-BrowserPhase arrival
    $head = Read-Json '/api/campaigns/status'
    if ($head.lastCheckpoint.sequence -ne '5') { throw 'Expected five checkpoints.' }
    Stop-DisposableHost

    $recovery = New-Item -ItemType Directory (Join-Path $run 'recovery')
    $incompatible = New-Item -ItemType Directory (Join-Path $run 'incompatible')
    $database = ([guid]$head.campaign.id).ToString('N') + '.db'
    $source = Join-Path $data $database
    $originalHash = (Get-FileHash -LiteralPath $source).Hash
    $recoveryDb = Join-Path $recovery.FullName $database
    $incompatibleDb = Join-Path $incompatible.FullName $database
    foreach ($copy in @($recoveryDb, $incompatibleDb)) {
        Invoke-QuasarCommand sqlite3.exe -readonly $source ('.backup "' + $copy.Replace('\', '/') + '"')
    }
    Invoke-QuasarCommand sqlite3.exe $recoveryDb "UPDATE checkpoints SET payload = X'00' WHERE sequence = 5;"
    Invoke-QuasarCommand sqlite3.exe $incompatibleDb 'PRAGMA user_version = 999;'
    $incompatibleHash = (Get-FileHash -LiteralPath $incompatibleDb).Hash

    Start-DisposableHost $assembly $recovery.FullName
    Invoke-BrowserPhase recover -Mobile
    Invoke-BrowserPhase uncertain
    Stop-DisposableHost

    Start-DisposableHost $assembly $incompatible.FullName
    Invoke-BrowserPhase incompatible -Mobile
    Stop-DisposableHost
    if ((Get-FileHash -LiteralPath $source).Hash -ne $originalHash -or
        (Get-FileHash -LiteralPath $incompatibleDb).Hash -ne $incompatibleHash) {
        throw 'Recovery changed an original or unsupported database.'
    }
    Write-Host "Campaign browser checks passed. Disposable fixtures and logs: $run"
} finally {
    Stop-DisposableHost
    Pop-Location
}
