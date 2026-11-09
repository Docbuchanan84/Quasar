# System Lab campaign lifecycle

[Issue #12](https://github.com/Docbuchanan84/Quasar/issues/12) integrates the
[checkpoint contract](SIMULATION_STATE.md) and [SQLite store](SQLITE_PERSISTENCE.md)
with the ASP.NET host. [Issue #13](https://github.com/Docbuchanan84/Quasar/issues/13)
owns the player-facing controls.

## Startup, saves, and recovery

- A new installation waits for **New Campaign**. Creation publishes a new
  campaign ID and its initial recovery checkpoint together. Each campaign has
  its own database, even when two campaigns have the same display name.
- Startup uses the last-played campaign ID recorded alongside the databases.
  Without a marker, it selects the most recently updated readable campaign
  with checkpoints, or attempts recovery of an unreadable campaign if none is
  readable. An invalid marker or an unrecoverable selected campaign is reported
  through campaign status. The host waits for an explicit create or load.
- Startup and **Continue** scan the selected campaign newest first across named
  and recovery checkpoints. The store verifies metadata, SHA-256, format, and
  detached simulation restore. If every candidate fails or the database is
  unreadable, the host tries available independent backups newest first. A
  verified backup is restored under a new campaign ID and the original database
  remains available for inspection. The host consumes backups previously
  created with the store's `CreateBackup` operation.
- **Load** opens a specific checkpoint and reports an error if that checkpoint
  fails validation. Successful New, Load, and Continue replace the active
  world, pause time, reset time controls to Once / 1 hour, and issue a fresh
  instance ID with revision zero. They clear the controller lease and any
  course-preview IDs. Committed trajectories, exact pending boundaries, and
  event history are restored with the world.
- Validation and disk work run outside the simulation lock. A campaign switch
  temporarily prevents ticks and commands while it validates the replacement.
  Polling can still read the active world. A failed switch leaves that world and
  its session identity available. Switching away from a dirty world discards its
  unsaved changes on success, so the client should offer a save or confirmation.
- The host serializes save captures and commits. Commands can run while a
  checkpoint is being written, and changes made after capture leave the world
  dirty. Each write uses the campaign's observed `HeadSequence`, even when a
  prior checkpoint was recovered. A conflicting external commit requires a
  reload. Explicitly loading an older checkpoint leaves it clean, and a
  subsequent save commits a new head. Restart always selects the latest
  valid committed checkpoint for the last-played campaign.
- **Save Now** and **Create Named Save** write retained named checkpoints.
  Autosave and graceful shutdown write rotating recovery checkpoints. Shutdown
  pauses the session and allows up to seven seconds for the persistence service.
  Abrupt termination can lose changes made since the last committed checkpoint.

## Configuration

The host reads `%LOCALAPPDATA%\Quasar\config\persistence.json` at startup.
Create the file to override defaults and restart the host to apply edits. A
missing file uses these defaults. An invalid file stops startup before the
host opens campaign storage.

```json
{
  "autosave": {
    "enabled": true,
    "intervalSeconds": 300,
    "recoveryCheckpoints": 5,
    "saveOnStop": false,
    "saveOnSignificantEvent": false
  },
  "saveOnShutdown": true
}
```

The accepted interval is 1–86,400 seconds. Retention is 1–100 recovery
checkpoints per campaign and applies on the next recovery commit. The host
checks once per real second using a monotonic clock. It waits at least the
configured interval from activation or the last recovery attempt and saves
only when dirty. A long simulation jump changes the world once for this
purpose. The optional event triggers can save earlier and coalesce pending
changes with a five-second minimum between early attempts. Significant events
are committed orders, cancellation, and movement boundary events. **Stop**
means the player's Stop command while time was running. Disabling autosave
disables both trigger types. Shutdown and manual saves remain independently
available.

Production storage defaults to `%LOCALAPPDATA%\Quasar\saves`. The Development
environment uses `%LOCALAPPDATA%\Quasar\dev-saves`. Both are outside the
checkout and release folders. For disposable hosts, supply trusted absolute
`Persistence__DataRoot` and `Persistence__SettingsFile` environment variables,
or use `--Persistence:DataRoot` and `--Persistence:SettingsFile` at launch.
Use a fresh temporary directory and a loopback port other than the live host's
port. Campaign API requests accept IDs and labels only.

## HTTP contract for issue #13

All routes below are relative to `/api/campaigns`. Every POST requires
`application/json` with same-origin `Origin`, and a body with the exact current
`instanceId` (string) and `revision` (number) from status or the lab snapshot.
Campaign and checkpoint IDs are UUID strings. API responses use
`Cache-Control: no-store`.

| Method and path | Additional JSON fields | Success |
| --- | --- | --- |
| `GET /status` | | 200, session status and effective `autosave` / `saveOnShutdown` settings |
| `GET /` | | 200, array of `{ id, campaign, error }` entries |
| `GET /{campaignId}/checkpoints` | | 200, `{ checkpoints, rejectedMetadata }`, newest sequence first |
| `POST /new` | `displayName` | 201, replacement session status |
| `POST /save` | | 201, new named checkpoint with label `Manual save` |
| `POST /save-named` | `label` | 201, new named checkpoint |
| `POST /load` | `campaignId`, `checkpointId` | 200, replacement session status |
| `POST /continue` | `campaignId` | 200, session status after recovering the latest valid checkpoint |

Campaign names and save labels allow up to 120 characters and must contain
nonblank text without control characters. Session status contains
`instanceId`, `revision`, `running`, `dirty`, `campaign`, `lastCheckpoint`,
`rejectedCheckpoints`, `recoveredFromCampaignId`, `recoveryError`, and
`autosaveError`. The campaign and checkpoint fields are null before a campaign
is active. A non-null `recoveryError` distinguishes startup failure from first
run. A nonzero `rejectedCheckpoints` or a non-null
`recoveredFromCampaignId` gives the client a recovery notice. An unreadable
campaign has null `campaign` and an `error` in the campaign list. The checkpoint
list can omit malformed metadata and reports the count. A listed checkpoint's
payload is verified on load.

Campaign metadata has `id`, `displayName`, `createdUtc`, `updatedUtc`, and
`headSequence`. Checkpoint metadata has `id`, `sequence`, `kind`
(`named` or `recovery`), nullable `label`, `createdUtc`, `gameBuild`,
`simulationTicks`, `stateFormatVersion`, and `sha256`. UTC dates are ISO 8601.
Sequences and simulation ticks are decimal strings. Lab snapshot
`world.elapsedTicks`, `world.ship.arrivalTicks`, and `world.events[].atTicks`
also use decimal strings when present.

| Status | Meaning |
| --- | --- |
| 400 | Invalid input or malformed JSON |
| 403 | Missing or foreign Origin, or a non-JSON POST |
| 404 | Campaign or checkpoint missing |
| 409 | Stale session (`session_conflict`), changed on-disk head (`checkpoint_conflict`), or no active campaign (`no_active_campaign`) |
| 422 | Corrupt or incompatible campaign or checkpoint (`invalid_campaign_data`), or startup recovery failure with no active world (`recovery_failed`) |
| 503 | Storage unavailable (`storage_unavailable`) |

Handled campaign failures return `{ code, error }`. Request rejection in the
HTTP middleware or JSON binding may have an empty body. On first run
`GET /api/lab` returns 409 and the client can render New Campaign. If startup
recovery failed, the lab returns 422 and the client can offer campaign and
checkpoint choices. After any successful replacement, discard pending preview
and lease UI, then refresh `GET /api/lab` before sending further commands.
Continue restores a paused world. Play remains a separate lab command.
Refresh after a conflict or uncertain network response before deciding whether
to issue another mutation.

Until issue #13 adds the UI, use the API to initialize a fresh disposable host.
These commands target a disposable host at `http://127.0.0.1:5090`.

```powershell
$base = 'http://127.0.0.1:5090'
$status = Invoke-RestMethod "$base/api/campaigns/status"
$body = @{ instanceId = $status.instanceId; revision = $status.revision; displayName = 'Disposable lab' }
Invoke-RestMethod "$base/api/campaigns/new" -Method Post -ContentType 'application/json' `
    -Headers @{ Origin = $base } -Body ($body | ConvertTo-Json)
```

## Verification

Run `./scripts/Verify.ps1` or filter xUnit by `CampaignHttpTests` and
`CampaignLifecycleTests`. The suites use temporary SQLite databases and
disposable Kestrel hosts on ephemeral loopback ports. They cover exact
restart and Continue in flight, after arrival, and after cancellation,
real-time cadence and retention, stale sessions and previews, concurrent
writes and transaction rollback, malformed settings, corrupt heads and backup
recovery, graceful shutdown saves, and abrupt process exit. The
existing hosted preview is a separate release.
