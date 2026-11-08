# System Lab map navigation

Implemented from the owner's navigation request on 8 November 2026. These are
client camera controls; they never move canonical objects or advance game time.
The surface inset in the planet inspector is illustrative CSS, not another
interactive map. The two interactive maps are `SystemMap.tsx` and `SpaceMap.tsx`.

## Controls

| Action | 2D | 3D |
| --- | --- | --- |
| Select | Click/tap a symbol, or use the object list | Click/tap a mesh, or use the object list |
| Pan | Left mouse drag or one/two-finger swipe | Right/middle/Ctrl-left drag or two-finger swipe |
| Rotate/orbit | Not applicable to the XY projection | Left mouse drag or one-finger swipe |
| Zoom | Wheel or pinch; compact + / − alternatives | Wheel or pinch; compact + / − alternatives |
| Recover orientation | **Reset View** restores the initial centred fit | **Reset View** restores origin, home angles and distance |
| Focus | Not added | **Focus Selected** makes the selected object's position the orbit pivot |

The four directional arrows and 3D rotate-step button are removed because their
gestures provide equivalent movement. Visible gesture hints describe the inputs.
Reset and zoom alternatives remain; object-list selection remains keyboard/touch
accessible. Existing Babylon keyboard inputs remain available on the 3D canvas.
Touch gestures are confined to the canvas; page scrolling/zoom outside it remains
available. Reference guides, projected labels, selection markers, course vectors,
inspector, travel orders and time controls keep their existing roles.

Focus preserves the current orbit angles and distance. A focused moving ship
remains the pivot as new server snapshots arrive. Mouse panning or two-finger
navigation releases this follow behavior, allowing free exploration; selection
itself is retained. Wheel zoom and one-finger orbit keep the focused pivot.
Changing selection does not automatically recentre the camera. Reset explicitly
clears follow and all pending camera motion. It does not reset the simulation.

## Diagnosis and implementation

Inspection found several concrete problems rather than a need for more buttons:

- Babylon's camera used its default **Y-up**, while Quasar's tactical plane is
  **XY with Z depth**. The camera now uses Z-up, giving a consistent orientation
  between views without changing any simulation coordinates. The home view looks
  from negative Y so positive X points right, matching the 2D map's orientation.
- Panning had a fixed sensitivity of 150, independent of distance and viewport.
  It now uses screen-plane world units per CSS pixel at the orbit pivot. Pan has
  no inertia, so it stops with the pointer; orbit/zoom use reduced inertia (0.45).
- The only pivot was the origin. Explicit focus supplies a useful selected-object
  pivot without camera changes on ordinary selection. Babylon's target setter is
  told to preserve orbit angles and distance when the pivot changes.
- Reset changed camera coordinates but did not clear accumulated input/velocity.
  It now cancels interpolation and both legacy and movement-system inertia.
- Tiny 2D movements already panned the map even when ultimately treated as taps.
  Both maps now distinguish taps from navigation with a 6 CSS-pixel threshold.
  A gesture remains multi-touch until all pointers lift. Lifting a pinch finger
  cannot become a selection or an unexpected one-finger orbit/pan. Cancellation,
  lost capture and window blur clear interaction state.
- Babylon 9.35's `attachControl` compatibility bridge rewrites the first bare pan
  mapping. Browser testing caught it overwriting an initially prepended middle
  mapping. Appending the extra middle mapping preserves right/middle/Ctrl-left
  panning after attachment; the regression test exercises attachment too.

Babylon still owns 3D capture, gesture math, camera movement and mesh picking.
A small subclass of its existing pointer input gates single-pointer motion until
drag intent is established. Pixi retains its existing DOM Pointer Events path.
The shared `MapGesture` tracks interaction intent rather than moving either camera.
No competing camera controller or runtime dependency was introduced.

3D wheel zoom is proportional to radius, pinch uses Babylon's natural distance
ratio, radius stays between 0.4 and 30 render units, and beta stays 0.05 radians
away from either pole to prevent pole flips. Horizontal orbit remains unrestricted.
2D wheel units are normalized; wheel/pinch preserve the point beneath the cursor
or finger midpoint, including translation during a pinch. Its existing 15–800
zoom limits remain. These are presentation bounds, not physical size constraints.

## Verification and limits

- `scripts/Verify.ps1`: locked restore, Release build, 51 existing xUnit cases,
  .NET formatting, frontend lint, renderer loading/bundling, nine new Node/Babylon
  navigation tests, TypeScript and production build. No runtime dependency/lock
  changes. The existing large lazy Babylon chunk warning remains.
- `src/Quasar.Client/scripts/check-map-browser.js`: 38 passing Playwright CLI
  checks of real Pixi/Babylon canvases in desktop Chromium and a 393 × 852 viewport
  with native CDP touch streams, including a remount/resize regression. This also
  issues an order and advances a disposable
  host to verify the course overlay and focused ship tracking. Screenshots remain
  in ignored `output/playwright/` and were visually reviewed.
- Production-bundle smoke check: both maps loaded from the isolated host; 3D
  focus/reset worked at phone width without page errors.
- The browser script requires Vite on loopback 5174 and an **isolated** backend on
  loopback 5094. It routes only that test browser's API requests to the isolated
  host. Start from a fresh lab; never direct it at a live game. From the repo root:
  `npx --yes --package @playwright/cli playwright-cli -s=quasar-nav open about:blank`,
  then `npx --yes --package @playwright/cli playwright-cli -s=quasar-nav run-code --filename src/Quasar.Client/scripts/check-map-browser.js`.

These checks establish desktop Chromium and emulated-touch behavior, not Safari,
physical iPhone rendering, GPU capacity, or every lifecycle/gesture combination.
Next: check the changed 2D/3D gestures and recovery controls on the owner's actual
iPhone 17 Pro and record its iOS/browser versions. See MOBILE_AND_REMOTE_ACCESS.md.

Source context: the live **Quasar — Game Design Record** title and interface/mobile
headings were read through the Google connector on 8 November 2026. Read access
was verified; write tools are exposed but no write was attempted or requested.
The record was not modified. This repository document is the implementation trail.

Library references: [Babylon camera inputs](https://doc.babylonjs.com/features/featuresDeepDive/cameras/customizingCameraInputs/)
and the installed 9.35.0 sources under `node_modules/@babylonjs/core/Cameras`.
