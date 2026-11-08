# Private Quasar hosting

**Approved and deployed: 2026-11-08.**

The owner's private endpoint is recorded in the ignored
`docs/LOCAL_PROJECT_CONTEXT.md`. Public clones should configure their own Serve
address; `quasar.example.invalid` below is a placeholder, not a live URL.
Connect Tailscale on the phone first. The owner confirmed the page works on the
actual iPhone 17 Pro. Its iOS/browser version and a separate cellular-only test
have not been recorded.

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

## Current process lifetime

**The host currently runs in a managed foreground terminal.** Keep that session,
the PC, and its network available. Tailscale's Serve configuration persists, but
it cannot keep a stopped application running. A reboot, closed host terminal, or
terminated Codex runtime can stop this preview. Unattended startup is not enabled.

Automatic approval review rejected detached background launch attempts with
"blocked by policy". A trial scheduled task also failed to locate its working
directory/executable; a noninteractive registration was denied by Windows.
The failed Quasar task was removed. No alternative hidden launcher or execution
policy override was installed. A durable independent service/startup setup
requires a deliberate follow-up on the PC.

The existing AC/DC sleep-after timeout was observed as zero (never). Power
settings were not changed. Manual sleep, shutdown, network loss, or app termination
still make the service unavailable.

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

The last command remains running; keep its terminal open. Ctrl+C stops it.
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

Expect `application: Quasar`, `status: ready`, `stage: system-lab`. The page and
health response use `Cache-Control: no-store`. Built JS/CSS/favicon assets return
200; unknown API paths return 404 rather than the HTML page.

The pre-change Serve configuration was saved locally as
`%LOCALAPPDATA%\Quasar\host\tailscale-serve-before.json`. To remove **only** the
Quasar route if requested:

```powershell
tailscale serve --https=9443 off
```

Do not run `tailscale serve reset`; that would affect the other services.

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

Still unverified: cellular-only access, exact iOS/browser version, physical phone
gameplay/renderer/lifecycle behavior, reboot recovery, and unattended hosting.

## Task 002 deployment and recovery

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
resume the previous run. Hosting remains the existing foreground process model.
