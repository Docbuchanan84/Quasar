# SQLite campaign storage

[Issue #11](https://github.com/Docbuchanan84/Quasar/issues/11) implements
`Quasar.Server.Persistence.ICampaignStore` in `SqliteCampaignStore`.
`Quasar.Simulation` owns the [checkpoint format and restore validation](SIMULATION_STATE.md).
The server can use the store without starting an HTTP listener or creating a lab
session.

## Data location and durability

- The hosted desktop factory uses `%LOCALAPPDATA%\Quasar\saves`, alongside the
  `Quasar\host` directory. Publishing writes releases and deployment metadata
  under `host`. Development and tests must explicitly pass a separate absolute
  root to the constructor. Issue #12 owns environment selection and registration.
- Each active campaign is a lowercase, 32-digit GUID file with a `.db` suffix.
  Its internal ID must match. Display names and save labels are SQL parameters.
  The API takes typed IDs and accepts the data directory only as trusted host
  configuration. Database files, SQLite sidecars, and direct storage directories
  cannot be links.
- Campaign writes use WAL and `synchronous=FULL` for durable commits on local
  desktop disks. SQLite serializes writers with an immediate transaction. The
  transaction verifies the expected head, inserts the checkpoint, advances the
  head, and rotates recovery entries. A failed transaction leaves the prior
  head and all its saves available. Lock contention is bounded by the
  five-second SQLite command timeout.
- Every named save is a new stable ID and stays outside recovery rotation.
  Recovery saves keep the latest five by default. Reconfigure the count by
  constructing the store with a different positive value. The new limit takes
  effect on the next successful recovery commit. Increasing the count changes
  future retention. Deleted checkpoints can be recovered from an earlier
  backup that contains them.
- Checkpoint metadata carries the ID, sequence, UTC time, build, kind, optional
  label, signed SQLite `INTEGER` ticks, simulation-format version, and SHA-256.
  Loads limit payloads to 64 MiB, verify the digest over the exact UTF-8 bytes,
  decode with `SimulationStateJson`, compare the clock and version, and build a
  detached `SystemLab`. The hash detects accidental damage. File access should
  be restricted to the host account.

## Schema and backup policy

SQLite's application ID identifies Quasar files. `user_version` and the
`schema_migrations` table track the separate database schema version. Version 1
creates the campaign and checkpoint tables. Version 2 adds the recovery-sequence
index. The migration ledger and DDL commit together. Reads accept both versions
without upgrading. `MigrateCampaign` and checkpoint writes apply migrations
forward. Unknown, newer, and invalid campaign files are reported without
initialization or downgrade.

`CreateBackup` uses SQLite's online backup API, including committed WAL data.
It checks SQLite integrity and every checkpoint in the copy before publishing
the standalone database under `saves\backups`. New databases and backups are
published by renaming a prepared file in the destination directory, with no
overwrite. Backup creation requires at least one checkpoint. Each backup has a
unique ID, and all prior backups are retained. The caller should surface backup
failures and monitor disk space. An interrupted or failed publication leaves
prior backups available. Discovery skips unpublished staging files.

`RestoreBackup` works even if the original campaign database is unreadable. It
re-verifies the backup copy and publishes a new campaign ID with the supplied
display name. Checkpoint IDs, sequences, metadata, and payloads remain intact
within the recovered campaign. The original campaign and backup stay available
for inspection. Backup files are independent of campaign files on the same
storage device, so whole-device loss requires copies on another device.

## Issue #12 integration

| Operation | API |
| --- | --- |
| Create and discover | `CreateCampaign(displayName)`, `ListCampaigns()`, `GetCampaign(campaignId)` |
| Upgrade explicitly | `MigrateCampaign(campaignId)` |
| Save | `SaveNamed(campaignId, expectedHead, label, gameBuild, state)`, `SaveRecovery(campaignId, expectedHead, gameBuild, state)` |
| List and load | `ListCheckpoints(campaignId)`, `LoadCheckpoint(campaignId, checkpointId)` |
| Recover | `LoadLatestValidCheckpoint(campaignId)`, `ListBackups(campaignId)`, `CreateBackup(campaignId)`, `RestoreBackup(campaignId, backupId, displayName)` |

Capture `lab.ExportState()` under the session gate. Order and discard stale
captures by session instance and revision under the host's save writer. Run
storage I/O outside the session gate. Use the campaign's `HeadSequence` as the
expected head and the returned checkpoint's sequence as the next head.
`CheckpointConflictException` reports expected and actual sequences. Compare
the capture against the active session before any retry. Sequence reflects
commit order, so an explicit load of earlier game time can be saved intentionally.

`LoadCheckpoint` returns a validated `LoadedCheckpoint` containing metadata,
state, and the detached world ready for the session to adopt under its gate.
`LoadLatestValidCheckpoint` scans newest first across both save kinds and counts
rejected checkpoint rows. A null result with zero rejections means an empty
campaign. Whole-database or campaign-metadata errors can stop the scan and
require trying backups. Discovery returns an entry with an error for unreadable
campaigns. A missing file or checkpoint is distinct from invalid data.
`ListBackups` enumerates candidates by file time. Restore re-verifies each
candidate before adoption. When recovery uses a backup, track the returned
campaign ID as the active campaign and load its head through the same API.

The lifecycle work owns last-played selection, backup timing, autosave cadence,
HTTP error mapping, and installing the detached world in a stopped session.
