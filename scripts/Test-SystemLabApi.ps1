[CmdletBinding()]
param([string]$BaseUrl = 'http://127.0.0.1:5090')

# Run against a separate lab instance, not an owner's active game: this advances its clock.
$ErrorActionPreference = 'Stop'
$origin = ([uri]$BaseUrl).GetLeftPart([System.UriPartial]::Authority)
$client = [guid]::NewGuid().ToString()
function Assert-Lab($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Send-Lab([hashtable]$Command, [string]$RequestOrigin = $origin, [string]$ContentType = 'application/json') {
    Invoke-WebRequest "$BaseUrl/api/lab/commands" -Method Post -ContentType $ContentType `
        -Headers @{ Origin = $RequestOrigin } -Body ($Command | ConvertTo-Json) -SkipHttpErrorCheck
}
function New-LabCommand([string]$Operation) {
    $state = Invoke-RestMethod "$BaseUrl/api/lab"
    @{ instanceId = $state.instanceId; revision = $state.revision; clientId = $client; operation = $Operation }
}
$initial = Invoke-RestMethod "$BaseUrl/api/lab"
Assert-Lab (-not $initial.running) 'Use a stopped test instance.'
$invalid = New-LabCommand 'configure'
$invalid.durationSeconds = -1
$invalid.mode = 'once'
Assert-Lab ((Send-Lab $invalid).StatusCode -eq 400) 'Invalid duration should return 400.'
Assert-Lab ((Invoke-RestMethod "$BaseUrl/api/lab").revision -eq $initial.revision) 'Invalid command changed state.'
Assert-Lab ((Send-Lab $invalid 'https://untrusted.example').StatusCode -eq 403) 'Cross-origin command accepted.'
Assert-Lab ((Send-Lab $invalid $origin 'text/plain').StatusCode -eq 403) 'Non-JSON command accepted.'
Assert-Lab ((Invoke-WebRequest "$BaseUrl/api/missing" -SkipHttpErrorCheck).StatusCode -eq 404) 'Unknown API must return 404.'
$configuration = New-LabCommand 'configure'
$configuration.durationSeconds = 60
$configuration.mode = 'once'
Assert-Lab ((Send-Lab $configuration).StatusCode -eq 200) 'Valid configuration rejected.'
Assert-Lab ((Send-Lab $configuration).StatusCode -eq 409) 'Stale command accepted.'
$play = New-LabCommand 'play'
Assert-Lab ((Send-Lab $play).StatusCode -eq 200) 'Play rejected.'
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
do {
    Start-Sleep -Milliseconds 100
    $after = Invoke-RestMethod "$BaseUrl/api/lab"
} while ($after.running -and [DateTimeOffset]::UtcNow -lt $deadline)
Assert-Lab (-not $after.running) 'Once did not stop.'
Assert-Lab ($after.world.elapsedTicks -eq $initial.world.elapsedTicks + 60 * 10000000) 'Once advanced the wrong duration.'
# Exercise every supported interval through the real runner, including transitions back to legacy settings.
foreach ($seconds in @(1, 5, 30, 60, 3600, 86400, 432000, 1296000, 2592000, 30, 5, 60)) {
    $before = Invoke-RestMethod "$BaseUrl/api/lab"
    Assert-Lab ($before.world.ship.status -eq 'Idle') 'Use an idle test instance for exact full-interval checks.'
    $configuration = New-LabCommand 'configure'
    $configuration.durationSeconds = $seconds
    $configuration.mode = 'once'
    Assert-Lab ((Send-Lab $configuration).StatusCode -eq 200) "Configuration rejected: $seconds seconds."
    Assert-Lab ((Send-Lab (New-LabCommand 'play')).StatusCode -eq 200) 'Play rejected.'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $after = Invoke-RestMethod "$BaseUrl/api/lab"
    } while ($after.running -and [DateTimeOffset]::UtcNow -lt $deadline)
    Assert-Lab (-not $after.running) "Once did not stop: $seconds seconds."
    Assert-Lab ($after.durationSeconds -eq $seconds) 'Selected interval changed.'
    Assert-Lab ($after.world.elapsedTicks -eq $before.world.elapsedTicks + [long]$seconds * 10000000) "Wrong advance: $seconds seconds."
}
Assert-Lab ((Invoke-WebRequest "$BaseUrl/api/lab").Headers['Cache-Control'] -eq 'no-store') 'Snapshot caching enabled.'
Write-Host 'PASS: HTTP snapshot, command, validation, stale revision, origin/JSON boundary, no-store, unknown API, and all nine intervals through the one-shot host runner.'
