# Approved frontend architecture

**Approved by the owner: 2026-11-08.** This records the selected libraries and
their roles. It does not approve every future implementation mechanism or claim
that maps, performance targets, or backend integration already work.

## Responsibilities

**Approved experience pillar:** frequent browser play on the owner's iPhone 17
Pro. Phone touch layouts and real-device checks are required alongside desktop.
See [mobile and remote access](MOBILE_AND_REMOTE_ACCESS.md) for the design goals,
rendering/lifecycle guidance, and proposed private deployment.

| Technology | Role | Prepared package/version |
| --- | --- | --- |
| React | Controls, tables, inspectors, forms, menus, management screens | Existing React/React DOM 19.3.1 |
| Babylon.js | 3D space views, planetary globes, altitude/depth relationships | `@babylonjs/core` 9.35.0 |
| PixiJS | 2D planetary/operational/tactical maps, deck plans, schematics | `pixi.js` 8.22.0 |

TypeScript and Vite remain the client toolchain. The renderer packages provide
their own types. No separate `@types` packages or React renderer wrappers were
added. MapLibre is deferred; loaders, physics, extra GUI packages, and other
engine modules can be evaluated when a specific task needs them.

## Authority and shared views

The C# simulation owns canonical time, coordinates, entities, production, combat,
and outcomes. The ASP.NET host will expose deliberate state/command boundaries.
React and the renderers display that state and submit intent.

A 2D map and 3D view must describe the same world. Share entity identity and
selection meaning; derive display coordinates using explicit transforms.
Projection into a plane does not flatten the simulation. Do not couple canonical
state to Babylon vectors/meshes or Pixi display objects.

Client-only camera motion, pan/zoom, hover, and visual interpolation are view
state. A render tick is not a simulation step. Do not move authoritative physics
or game rules into either rendering engine.

## Initial integration guidance

These are implementation guidelines for future tasks, not completed components:

- React owns the page layout and canvas lifecycle. Create/dispose engines and
  scenes around mounted viewports; remove listeners and stop loops on cleanup.
- Let each renderer update its own frame-level objects. Send deliberate changes
  such as selection to React; avoid React state updates for every rendered object
  on every frame.
- Prefer separate 2D/3D viewports initially. Mixing both engines into one GPU
  context is not required by this choice. Pause unnecessary rendering in hidden
  views and validate simultaneous views before promising support.
- Import Babylon modules intentionally, and load view code only when needed.
  Installed packages do not need to inflate the current placeholder bundle.
- Pixi provides rendering; projections, geographic data, zoom/detail rules,
  overlays, labels, and map interaction still need explicit design.
- Establish coordinate units, local frames, precision, and visible-detail limits.
  Babylon's large-world facilities are useful tools, not proof of galaxy-scale
  rendering or simulation capacity.

## Prepared development checks

```powershell
.\scripts\Setup.ps1
.\scripts\Verify.ps1
# Renderer check alone:
npm.cmd --prefix src/Quasar.Client run check:renderers
```

Setup's existing `npm ci` restores the approved dependencies from the lock file.
Verification includes a Babylon NullEngine scene initialization/cleanup check,
a Pixi display-object check, and a Vite browser bundle containing both engines
and React. This catches missing/incompatible package imports without a live GPU
or permanent demo scene. The in-memory bundle is not written into the app output.

These checks do not establish browser GPU support, visual correctness, or map
performance. The hosted skeleton now includes a real backend health check,
offline/online status, and foreground-resume checks; map/game features remain
unimplemented. See [HOSTING.md](HOSTING.md) for the owner-confirmed phone preview.

## Next frontend validation

When rendering work is requested, use one small fixture represented as a Pixi
2D map and Babylon 3D/globe view, with linked selection in a React inspector.
Check coordinate agreement, switching/cleanup, input, labels, resizing, and
measured frame/memory cost before scaling entity counts. This does not change
the headless-clock milestone or authorize implementing game mechanics now.
Include real iPhone portrait/landscape, touch, app-switch/lock/reconnect, and
Wi-Fi/cellular checks. Desktop viewport emulation alone is insufficient.

References: [Babylon ES modules](https://doc.babylonjs.com/setup/frameworkPackages/es6Support),
[Babylon/React integration](https://doc.babylonjs.com/communityExtensions/Babylon.js%2BExternalLibraries/BabylonJS_and_ReactJS),
[Pixi setup](https://pixijs.com/8.x/guides/getting-started/quick-start).

## Task 002 renderer validation (2026-11-08)

System Lab now mounts one lazy renderer at a time, sharing canonical snapshots and
selection through React. Pixi's XY projection exposes Z; Babylon uses XYZ in a
right-handed scene. Both support selection and explicit camera buttons as well
as gestures. Inactive engines/listeners are disposed, hidden loops stop, and Pixi
avoids redrawing unchanged state. Representative surface imagery is CSS-generated
and clearly illustrative. See SYSTEM_LAB.md for transforms and known download cost.
Renderer checks and desktop emulation do not establish iPhone GPU capacity.
