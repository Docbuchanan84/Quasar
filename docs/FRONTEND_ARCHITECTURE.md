# Approved frontend architecture

Both maps display server-sampled course previews and committed trajectories,
including planned departure and arrival markers. React owns route selection and
confirmation. See [course plotting](COURSE_PLOTTING.md) for the interaction and
[spacecraft trajectories](SPACECRAFT_TRAJECTORIES.md) for movement.

**Stack approved: 8 November 2026. Task 002 implemented and deployed the same day.**
The owner subsequently confirmed the initial phone experience and supplied two
portrait screenshots of the working 2D map. See [mobile validation evidence](MOBILE_AND_REMOTE_ACCESS.md).
This acceptance establishes a useful baseline; it does not select permanent
physics, a scheduler, or a measured mobile capacity.

## Responsibilities

| Technology | Responsibility | System Lab implementation |
| --- | --- | --- |
| React / TypeScript / Vite | Controls, inspection, orders, history, layout, connection state | Shared snapshot and selection; collapsible inspector/history; compact time selectors |
| Babylon.js 9.35.0 | Genuine 3D views and future globes | Selectable XYZ symbols, orbit reference guides, shared course overlays, camera rotation/pan/zoom, readable screen-space labels |
| PixiJS 8.22.0 | 2D tactical/operational maps and plans | Selectable XY projection, Z labels, symbols, grid/reference guides, shared course overlays, pan/zoom |

Both approved renderer versions are installed and locked. No React renderer
wrappers, GUI package, external art/font service, or push transport are needed
by the client. Campaign storage lives on the host. MapLibre remains deferred.
The representative surface inset uses procedural CSS and sample overlays.

## Authority and shared coordinates

C# owns the clock, identities, positions, movement, orders, and arrivals. ASP.NET
exposes the deliberate snapshot/command boundary. React and both renderers consume
the same state and submit intent. Selection uses persistent object IDs across views.

The simulation uses a right-handed system-local XYZ frame in double-precision
kilometres. Rendering scales coordinates by 1/1,000,000. Pixi shows XY with screen
Y inverted and Z in labels; Babylon explicitly uses a right-handed scene. Its
camera orientation is presentation state. A 2D projection never flattens the world.
The Solar System views draw shared orbit guides from the catalogue's orbital
elements. Legacy fixtures use illustrative reference circles. Symbol sizes
are enlarged for readability.

Camera movement and label projection are client-only. No render tick calculates
canonical movement or advances time. The preview and committed paths use the same server-sampled trajectory contract.
Departure and future arrival markers use planned positions. See [System Lab contracts](SYSTEM_LAB.md).

## Components and lifecycle

- React owns connection state, canonical snapshots, selected ID, view choice,
  inspectors, and controls. It does not update simulation state per frame.
- Each map lives in its own lazy-loaded viewport. Switching disposes the previous
  renderer, listeners, and related objects; one renderer is mounted at a time.
- Pixi redraws only changed state/camera/viewport data and avoids hidden work.
  Babylon stops its render loop while hidden and disposes the scene/engine on exit.
- Both retain zoom/Reset View buttons and object-list selection. Directional
  buttons have gesture equivalents and are removed. Pixi handles drag/pinch and
  nearest-symbol selection. Babylon uses a Z-up orbit camera and mesh picking.
  Both support selected-object focus and guard taps against accidental drags.
  See [navigation](MAP_NAVIGATION.md) for mappings, camera limits and validation.
- Babylon labels are projected into screen-space DOM text so they remain readable
  without a separate GUI package or per-label texture.
- Context-loss messages offer switching/reloading. A map error boundary contains
  failed lazy imports after deployment so clock/orders/inspection remain available.
- The UI refreshes server state on reconnect/resume and never resends an uncertain
  command automatically. The browser control lease bounds unattended processing.

Keep these boundaries as further views are added. Layer geography, infrastructure,
borders, resources, and military overlays onto deliberate view contracts when
those systems exist; do not invent a universal map/entity framework in advance.

## Verification and limitations

`scripts/Verify.ps1` runs frontend lint/type/build plus the existing headless
Babylon/Pixi loading and browser-bundle check. Headless success is not GPU evidence.
Browser checks exercise real renderer canvases, course previews, selection,
shared inspector XYZ, camera controls, reference toggles, repeated switching,
responsive layouts, and failed-import recovery.

Actual phone evidence now confirms basic portrait **2D** rendering and the desired
initial interaction experience. The supplied screenshots do not show the 3D view,
gestures, landscape, or lifecycle recovery. Those checks remain explicit follow-up
items alongside iOS/browser versions, cellular-only access, and measurements
of thermal, battery, memory, and frame-time behavior.

The lazy Babylon scene chunk is approximately 1.09 MB raw / 265 KB gzip and triggers
Vite's 500 kB warning. It loads only when selecting 3D. This is a known download
cost, not a demonstrated phone rendering budget.

References: [Babylon ES modules](https://doc.babylonjs.com/setup/frameworkPackages/es6Support),
[Pixi setup](https://pixijs.com/8.x/guides/getting-started/quick-start).
