# Course plotting and map integration

Implemented for the owner's Agent 4 request on 8 November 2026. The deployed
course release integrates celestial queries, spacecraft trajectories, navigation,
and time controls. No runtime dependency, physics solver, fleet model,
save format or renderer was added. The lab currently exposes one ship and two
planet destinations; fleet orders and arbitrary map-coordinate waypoints await
corresponding authoritative host contracts.

Prerequisite branches were consolidated in owner-authorized PR #7 before this
course branch was published against main. See [integration](AGENT_INTEGRATION.md).

## Player workflow

1. Select Pathfinder, then **Plot Course** while time processing is stopped.
2. Select a planet in either map, the existing object list or the destination
   selector. Selection still inspects that object; the plotting origin remains
   the selected ship.
3. Inspect the preview and optionally change the whole-second flight duration
   (one second to ten Julian years, matching the lab service). Inputs trigger a
   debounced recalculation; **Recalculate** also permits an explicit retry.
4. **Issue Order** confirms the reviewed plan. Only that action commits movement.
   **Cancel preview** simply discards local UI state, including when previewing a
   replacement for an existing course.

The panel shows origin/destination, departure, sampled path distance (explicitly
approximate), travel duration, ETA, required delta-v, fuel estimate availability
and feasibility/model warnings. It never substitutes zero for unavailable fuel.
Flight duration is the supported adjustment; departure is the current stopped
simulation epoch. The panel does not invent thrust, fuel capacity or launch-window
controls. Numerical infeasibility is reported from the planner, without drawing a
straight-line substitute. See [trajectory limits](SPACECRAFT_TRAJECTORIES.md).

## Exact confirmation and stale state

`GET /api/lab/course-preview?destinationId=...&flightSeconds=...` wraps the existing
planner with an opaque `previewId`, host `instanceId`/`revision`, source ship ID,
destination ID, summary, warnings and the existing `TrajectoryDisplay` samples.
The sampled-distance figure sums consecutive server-supplied sample chords; it
is a rendering approximation, not an analytical physical path length.

The host retains at most 32 immutable solved plans as disposable confirmation
tokens. Preview reads change no canonical position, velocity, clock, revision,
orders or history. Discarding a preview needs no server command. Tokens are
instance-bound and are invalidated by any successful command or clock tick.
An expired/evicted token receives HTTP 409 and requires recalculation/review.

`commit-course` on the existing same-origin JSON command endpoint accepts only
`previewId` in addition to the normal instance/revision/client envelope. It commits
the retained **solved plan**, never browser geometry or a newly solved plan from
potentially changed inputs. Execution's exact departure-state/time/catalogue
validation still applies. A successful commit consumes all outstanding previews.
Existing direct `travel`, query and `cancel-travel` APIs remain compatible, but
the player UI no longer bypasses preview with immediate travel buttons.

The React hook keys results by host instance/revision, ship, destination, flight
duration and explicit retry. Old overlays and confirmation eligibility disappear
immediately on a key change. Cleanup aborts in-flight requests; late results are
ignored. Polling/reconnect refreshes state. Host restart discards plotting state.
No uncertain order is automatically replayed; the plotting panel closes after a
confirmation attempt so another attempt starts with a new review.

## Shared visualization and performance

React shares the same committed and preview sample arrays with either lazy map.
Both use Agent 2's system XYZ kilometres and the existing shared map scale.
Pixi projects XY; Babylon preserves XYZ. Neither integrates gravity, extrapolates
ship motion, advances time or sends a command from its camera/render loop.

- Preview: gold dashed path; committed: green solid path. A replacement preview
  can coexist with the committed course until explicit confirmation.
- Departure markers use squares/boxes. Arrival/future-destination markers use
  outlined diamonds at the final authoritative trajectory sample, rather than
  the destination's present position. Markers are unpickable in 3D, preserving
  physical-object selection. The map legend explains the shapes and colors.
- The full departure-to-arrival path remains fixed while the ship symbol follows
  authoritative snapshots. Arrival removes the completed path via server state.
  Switching/closing maps does not change execution or the retained plan.
- Solving/sampling is server work on meaningful requests/commit, never redraw.
  Polling preserves an unchanged committed path reference after one comparison
  per new snapshot; Babylon rebuilds course meshes only when shared layers change.
- Pixi retains symbols/text, redraws on state/input/resize/resume and performs no
  periodic serialization or idle redraw. Hit testing scans once without sorting.
- Babylon reconciles object meshes on snapshot changes, caches unchanged label
  projections, and reads viewport layout before writing label styles. Camera
  operations remain local. Existing lazy loading, hidden-tab pause and disposal
  behavior remain. App-local Pixi disposal explicitly preserves global pools so
  late StrictMode initialization cleanup cannot invalidate an active replacement.

## Verification and boundaries

- `scripts/Verify.ps1`: 99 xUnit cases, nine navigation and two coordinate Node
  cases, locked restore, Release build, .NET format, frontend lint, renderer load/
  bundling, TypeScript and Vite production build. The existing lazy Babylon
  bundle-size warning remains; it is not a functional failure.
- New session tests prove isolated previews/discard, exact reviewed-plan commit,
  stale/cross-instance/evicted/reused-token rejection, invalid/running inputs and
  mapless large-jump arrival. The old interval regression now expects the
  authoritative gravitational coast rather than a stationary unordered ship.
- Playwright CLI: 22 course-workflow checks on real canvases, plus 38 navigation checks (37 existing and a lifecycle regression), covering
  desktop/native Chromium touch behavior. Course checks cover map/list/
  selector destinations, adjustment/validation, no camera-induced planning or
  physical movement, shared XYZ, distinct simultaneous preview/committed layers,
  cancellation, explicit confirmation and exact arrival after a 30-day request.
- Desktop and 393 × 852 panel screenshots visually reviewed; no horizontal
  overflow. Artifacts remain ignored under `output/playwright/`.
- Synthetic 1,000-object desktop response fixture: retained Pixi labels, both maps
  mounted, zoom/reset worked, zero planning requests and zero browser exceptions.
  One post-fix automation sample: Pixi two-button interaction 180 ms, Babylon load
  628 ms and two-button interaction 326 ms. These include automation overhead;
  they are neither sustained frame rates nor capacity/physical-phone claims.

From the repository root, start the disposable backend and frontend in separate
terminals after running Setup.ps1:

```powershell
. .\scripts\Use-DevEnvironment.ps1
dotnet run --project src/Quasar.Server --no-launch-profile -- --urls http://127.0.0.1:5094
```

```powershell
Set-Location src/Quasar.Client
npm.cmd run dev -- --port 5174
```

The Playwright scripts route API calls to the disposable host. The default Vite
proxy still points to 5080, so exercise this setup through the scripts:

```powershell
npx --yes --package @playwright/cli playwright-cli -s=quasar-course open about:blank
npx --yes --package @playwright/cli playwright-cli -s=quasar-course run-code --filename src/Quasar.Client/scripts/check-course-browser.js
npx --yes --package @playwright/cli playwright-cli -s=quasar-course run-code --filename src/Quasar.Client/scripts/check-map-browser.js
npx --yes --package @playwright/cli playwright-cli -s=quasar-course run-code --filename src/Quasar.Client/scripts/check-map-load-browser.js
```

The course script expects a fresh stopped lab; restart only that disposable host
before repeating it. PR #8 merged as `a3a052f` and was deployed through the private
preview. HTTPS served the course client and returned a 65-point Iris preview.
Physical
iPhone 17 Pro/Safari testing, sustained large-world budgets and moving-target UI
fixtures remain open. Generic moving-target trajectory correctness is covered by
Agent 3's simulation tests; the visible lab planets remain intentionally fixed.

The live **Quasar — Game Design Record** includes the current release, player
workflow, verification, and open design work. The 8 November 2026 update was
written with a revision guard and verified by readback.

Next: validate preview, cancellation, confirmation and both maps on the owner's
physical iPhone 17 Pro and record browser/OS versions.
