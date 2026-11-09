# Private Quasar hosting

**Approved and deployed: 2026-11-08.**

The owner's private endpoint is recorded in the ignored
`docs/LOCAL_PROJECT_CONTEXT.md`. Public clones should configure their own Serve
address; `quasar.example.invalid` below is a placeholder, not a live URL.
Connect Tailscale on the phone first. The owner confirmed the System Lab experience
and supplied actual phone portrait screenshots of 2D rendering and active/stopped
Continuous controls. See [device evidence](MOBILE_AND_REMOTE_ACCESS.md). Exact
iOS/browser versions and a separate cellular-only test have not been recorded.

## What runs

Tailscale Serve on private HTTPS port **9443** proxies to
`http://127.0.0.1:5080`. ASP.NET serves the built React System Lab, health,
and authoritative snapshot/command API under one origin. The lab includes 2D/3D
maps, spacecraft orders, time controls, inspection, and event history. See
[System Lab contracts](SYSTEM_LAB.md). State is in-memory: a restart resets it.

Existing Serve routes were compared before/after and preserved:

| HTTPS port | Existing local target |
| --- | --- |
| 443 | `http://127.0.0.1:3000` |
| 8443 | `http://127.0.0.1:8081` |
| 9443 — Quasar | `http://127.0.0.1:5080` |

Access is tailnet-only. No public Funnel, router forwarding, or separate app
login was added. Game commands require same-origin JSON requests and instance/
revision validation. This is one shared private lab, not individual user games;
per-user authentication remains a future design question.

## Current release and process lifetime

Gameplay release `a3a052f` (PR #8) was deployed on 8 November 2026. The host
serves the merged System Lab, including the nine time intervals, gravitational
movement, updated navigation, and course plotting. Private HTTPS returned the
new client and a 65-point Iris course preview. The deployment retained the prior
release and a `deployment-before-course-*.json` metadata backup. The restart
began a new shared in-memory lab with Pathfinder coasting at elapsed zero.

The current launcher is a separate PowerShell process that runs the foreground
`Run-HostedApp.ps1` wrapper independently of the tool command session. Keep the
PC and network available. Tailscale Serve persists independently of the app.
Startup after reboot is manual. Earlier temporary command sessions failed to
keep the host running between turns, and a separate visible terminal was used
for the earlier recovery described below. Historical startup experiments included
a rejected detached launch and an unsuccessful scheduled task, which was removed.
The current deployment uses the existing runner.

## Start or update from the PC

Use PowerShell in the checkout. Setup/build requires PowerShell 7; the foreground
runtime wrapper also works with Windows PowerShell. All paths resolve from the
scripts, so no permanent PATH change is required.

```powershell
Set-Location 'C:\path\to\Quasar'
.\scripts\Verify.ps1
.\scripts\Publish-HostedApp.ps1
.\scripts\Stop-HostedApp.ps1
.\scripts\Start-HostedApp.ps1
```

For a manual start, run the command in a PowerShell terminal on the PC and keep
it open. Ctrl+C stops the foreground wrapper. The app starts manually after reboot.
To restart the existing publication without rebuilding, use just Stop then Start.
Stop targets only Quasar assemblies published under its own host directory.
It does not alter Tailscale or other projects.

Publishing creates a new release under
`%LOCALAPPDATA%\Quasar\host\releases\<timestamp>`, copies the Vite output into its
`wwwroot`, and writes `deployment.json`. Existing releases are retained.
Runtime logs are in `%LOCALAPPDATA%\Quasar\host\host.log`; the wrapper rotates
a log larger than 5 MiB at startup. The original source tree is not the live
document root. Do not run the development server on 5080 concurrently.

## Checks and recovery

```powershell
Invoke-RestMethod 'http://127.0.0.1:5080/api/health'
Invoke-RestMethod 'https://quasar.example.invalid:9443/api/health'
tailscale serve status
```

Expect `application: Quasar`, `status: ready`, `stage: system-lab`. For the course
release, check the Plot Course control and the read-only course preview endpoint
while time processing is stopped. The page and health response use
`Cache-Control: no-store`. Built JS/CSS/favicon assets return
200; unknown API paths return 404 rather than the HTML page.

The pre-change Serve configuration was saved locally as
`%LOCALAPPDATA%\Quasar\host\tailscale-serve-before.json`. To remove **only** the
Quasar route if requested:

```powershell
tailscale serve --https=9443 off
```

Do not run `tailscale serve reset`; that would affect the other services.

## Campaign release and rollback

The campaign release keeps data in `%LOCALAPPDATA%\Quasar\saves` by default,
separate from the release and deployment metadata. Development uses
`%LOCALAPPDATA%\Quasar\dev-saves`. Check any configured data-root override
before publishing and keep it outside the checkout and the release directory.
The existing deployed course release holds its world in memory. Its state
cannot be imported by the campaign release.

1. Record the active release and copy `deployment.json` to a separate file
   before publishing. If a persistence-enabled host is already running, make a
   named save and allow a graceful stop. With the host stopped, copy the full
   save directory including `last-played`, backups, and any SQLite sidecars to a
   separate location. An operator can also use the store's verified SQLite
   backup operation for a standalone database.
2. Publish the merged build and start it with the same account and data root.
   Check `/api/health`, `/api/campaigns/status`, and the browser. Confirm the
   expected campaign/checkpoint and paused state before advancing or issuing
   orders. A fresh persistence installation starts at New Game.
3. If a checkpoint fails, use **Load Game** to pick an earlier save or
   **Continue** to attempt recovery from an earlier checkpoint or a verified
   backup. Keep a copy of damaged files for diagnosis. If recovery fails,
   test a known-good backup in a disposable data root before restoring service.
4. To roll back, stop Quasar, archive the current release metadata and save
   directory, then restore the earlier deployment metadata. Use a copy of the
   pre-update save directory if compatibility is uncertain or a forward
   migration occurred. Keep post-update saves separately for recovery with a
   compatible build. Rolling back to the in-memory course release starts its
   original fixture.

SQLite schema and simulation checkpoint versions are checked independently.
Known SQLite migrations run forward inside transactions on writes. Releases
reject newer or unsupported data. Before shipping a format change, verify a
backup, exercise migration and restore with disposable copies from the prior
release, and document rollback compatibility. See
[storage policy](SQLITE_PERSISTENCE.md#schema-and-backup-policy).

## Historical skeleton validation (before Task 002)

- Same-origin page, built JS/CSS, and backend health returned 200 over private
  HTTPS with normal certificate verification. Original Serve routes were unchanged.
- Browser preview reached Connected; manual checks worked. Offline/online
  recovery was exercised in browser emulation.
- Narrow mobile layout at 402 CSS px had no horizontal overflow; the main action
  has a 48 CSS px minimum height. The page was visually inspected. The 1440 CSS px
  desktop layout also had no horizontal overflow and a fresh load had no console
  errors or warnings.
- The owner confirmed "Works perfectly" on the actual iPhone 17 Pro.
- Build, formatting, frontend lint/type/build, and headless renderer checks passed.
  The existing simulation test project still has no behavior tests.

The later Task 002 owner check supersedes the skeleton-only validation for basic
portrait 2D gameplay. Still unverified: cellular-only access, exact iOS/browser
versions, physical-phone 3D/complete gesture/lifecycle coverage, reboot recovery,
and unattended hosting.

## Release rollback and Task 002 deployment history

Release publication preserves previous release directories. The pre-Task-002
deployment metadata was copied to `deployment-before-task002.json` in the host
directory, alongside a before-change Serve configuration. No Serve configuration
change is needed to update gameplay.

If rollback is needed, stop only Quasar, preserve the current deployment metadata,
restore the earlier metadata to `deployment.json`, then run Start-HostedApp.ps1.
Old releases are retained; verify health after restart. All in-memory lab progress
is lost on either update or rollback. Existing browser pages should reload after
deployment; a failed lazy map load leaves the controls/inspector available and
offers Reload display.

The app's continuous runner is active only after Play. It processes at most four
selected intervals per second and stops on arrival or controller lease expiry
(five seconds). A browser reload creates a new controller ID and does not silently
resume the previous run. See the current process lifetime section for the running launcher.

## Requested reset and later acceptance (2026-11-08)

After deployment checks, the owner explicitly requested a reset. Only Quasar was
restarted through the existing scripts. Readback verified elapsed zero, paused,
Pathfinder idle, four objects, and one initialization event. The reset changed no
Serve route and added no reset endpoint/button. Subsequent owner play advanced the
world again; those later screenshots are historical evidence, not its current state.

The owner then accepted the phone look and behavior and supplied the two portrait
screenshots described in MOBILE_AND_REMOTE_ACCESS.md. This documentation update
requires no release publication, host restart, live-world mutation, or network
reconfiguration. Do not reset ongoing play to match a screenshot or old test result.

## White/empty page triage

On 2026-11-08, an owner white-screen report coincided with a missing host process
and connection refusal on loopback 5080. Starting the existing publication restored
HTTPS and the browser UI. No log established why the foreground process exited;
do not assume a renderer defect or a specific process-termination cause.

Check loopback health and the private HTTPS page/health first. If loopback refuses
connections and Quasar is absent, use Start-HostedApp.ps1 to start the existing
release; publishing and changing Serve are unnecessary. Verify page/assets and
snapshot responses, then inspect browser errors if the host is healthy but the
page remains empty. Never use a tailnet-wide Serve reset to recover this app.

A host that has already exited has lost this lab's in-memory state. Starting it
creates a fresh fixture; it is not saved-game recovery. A remaining blank page
may require a browser refresh after the endpoint recovers. Durable process
supervision and recoverable save/load remain separate follow-up work.

### Download prompt after an empty page

The owner's subsequent phone screenshot offered `document.txt` instead of the
game. At inspection, the host was absent again and Serve returned an empty 502
for both `/` and `/index.html`, with no Content-Type. This was an unavailable
origin, not an observed attachment header on a healthy game response. The phone's
download behavior is consistent with that response, but its exact browser
handling was not independently reproduced.

Started the existing release in a separate visible PowerShell window, leaving
Serve routes, published files, and startup configuration unchanged. Verified
private HTTPS returned 200 and `text/html` without Content-Disposition; referenced
JavaScript/CSS returned 200 with their correct MIME types. The page and 2D map
rendered with no browser warnings/errors. After recovery, dismiss a stale
download prompt and navigate afresh (an unused query string can avoid reusing
the failed navigation). Do not save the download as a required game file.
The owner then confirmed "Game loads now" on the phone using the fresh navigation.
