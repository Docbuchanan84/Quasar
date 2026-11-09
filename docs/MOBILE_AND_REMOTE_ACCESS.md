# iPhone play and remote access

## Current status — 8 November 2026

The deployed campaign release is `f86ffd2`. Desktop Chromium verified the
course workflow and navigation, including emulated touch. The physical-phone evidence
below comes from the original System Lab release. The current course workflow,
3D and lifecycle still need physical iPhone validation.

**Task 002 is deployed and owner-accepted in actual phone use.** After trying the
System Lab, the owner reported: "It looks so good already and works just how I
want." Two supplied phone screenshots show the playable 2D tactical display in
portrait, including selected spacecraft, depth labels, movement vector, and
Continuous time controls in stopped and processing states.

The established primary device is the owner's **iPhone 17 Pro**. The screenshots
were supplied in that testing context; they do not independently establish the
hardware model, installed iOS version, browser application/version, or network path.
Those version/network details remain unrecorded. Safari remains the recommended
baseline, not a claim about which browser produced these screenshots.

This is a successful first real-device gameplay check. It is not a completed
qualification of both renderers, every gesture, lifecycle behavior, or performance.

## Campaign workflow validation

The campaign controls passed Chromium iPhone emulation at 402 px portrait,
320 px narrow portrait, and 874 px landscape, plus desktop Chromium. The
regression covers touch creation, named save, Continue, replacement
confirmation, earlier-checkpoint recovery, and operation of both map views.
The private Tailscale endpoint also served the first-run screen to Chromium
with iPhone 17 Pro emulation. Physical Safari, keyboard and safe-area behavior, and reconnect on the iPhone
17 Pro remain to be verified for this workflow.

## Owner-supplied screenshot evidence

| Evidence | Visible result | What it establishes |
| --- | --- | --- |
| Image 1: full phone browser view | Host connected; 2D selected; clock D001 15:23:15; 1 minute / Continuous; Player stopped; Play available | Live private-site access and a readable portrait command display in a stopped state |
| Image 1: system map | Helios, Iris, Nereid, selected Pathfinder, reference grid/circles, Z labels, travel vector, camera controls | Actual phone rendering of the Pixi tactical view and the selected-object presentation |
| Image 2: closer view | Clock D001 15:28:15; Processing; Stop available; interval/mode controls disabled | Continuous processing is visibly active with the expected control state |
| Image 2: spacecraft | Pathfinder remains selected near Nereid; Z label changes from approximately -469k km to -470k km | Consistent visible movement/depth updates across a five-minute difference in simulated time |
| Owner feedback | "It looks so good already and works just how I want." | Owner acceptance of the initial look and interaction experience |

The five-minute difference is **simulated** time. These still images do not measure
wall-clock speed, frame rate, latency, or a precise trajectory. They do not show an
arrival event, 3D scene, inspector/orders panel, or a gesture being performed.
The owner's general success report is recorded separately from what is visible.

Original images and SHA-256 provenance are retained privately under ignored
`docs/sources/task002-phone-validation-2026-11-08/`. Originals contain the private
access address and browser chrome, so this public document includes observations
rather than the images. The shared Google Game Design Record remains the project
knowledge source; these files are supporting evidence, not another live record.

## Verification coverage

| Check | Current evidence | Remaining work |
| --- | --- | --- |
| Private phone access and portrait 2D rendering | Owner report and supplied screenshots | Repeat after material renderer/layout changes |
| Basic desired interaction experience | Owner acceptance; stopped/processing states and changing ship position shown | Expand deliberate workflow coverage as mechanics grow |
| Both renderers, selection, orders, movement/arrival, Once/Continuous/Stop | Automated simulation/session/HTTP tests and desktop Chromium browser checks | Explicit physical-phone 3D and end-to-end order/arrival checks |
| Pan, zoom, rotation, reference toggle, view cleanup | Desktop browser checks and implemented touch/control paths | Physical-phone drag/pinch/two-finger pan and repeated switching |
| Portrait/landscape and narrow layouts | Actual portrait screenshot; automated 402/320 px and landscape checks | Actual phone landscape, text scaling, browser zoom, and safe-area transitions |
| Background/lock, lease expiry, reconnect | Server tests and desktop offline/reconnect check | Physical-phone lock/app switching and recovery |
| Wi-Fi/cellular | Working phone access; screenshot includes cellular signal indicator | Record the actual path; test cellular-only access explicitly |
| GPU capacity, frame time, memory, heat/battery | Basic actual-phone 2D rendering works | Measure before setting scale/performance budgets |

## Approved pillar and implementation guidance

Frequent iPhone browser play has been an approved core pillar since 8 November
2026. Desktop remains supported; the C# backend owns canonical state and time.

- Keep inspection, selection, orders, and time controls usable in portrait and
  useful in landscape. Preserve depth through focused views and drill-down.
- Prefer readable type and primary targets of at least 44 by 44 CSS pixels.
  Avoid hover-only, mouse-only, or keyboard-required workflows.
- Resolve map gesture conflicts with page scrolling/browser navigation. Preserve
  zoom/recovery buttons and object-list selection. The navigation follow-up removes
  directional buttons with gesture equivalents; see [controls](MAP_NAVIGATION.md).
- Respect safe areas, browser chrome, keyboards, text scaling, and page zoom.
- Mount only the needed renderer; stop hidden work and dispose inactive engines.
  The PC simulation does not remove GPU/memory/battery constraints on the phone.
- Treat lock, backgrounding, and network changes as normal. Refresh authoritative
  state on return, never replay uncertain orders, and never advance time in a tab.

Task 002 uses a five-second lease renewed by the initiating visible browser.
Without renewal, continuous processing stops at the next host tick after expiry.
The host can process further selected intervals during that grace period; this
is not an immediate disconnect guarantee. Reloading creates a new controller ID
and does not silently resume the previous run. These are provisional lab semantics.

## Existing private hosting

The owner's private address is in ignored `docs/LOCAL_PROJECT_CONTEXT.md`. Connect
Tailscale on the phone and open that HTTPS address. No Vite port or PC-localhost
address is needed on the phone.

```text
iPhone browser + Tailscale
    -> private Tailscale Serve HTTPS 9443
    -> loopback ASP.NET host 5080: built React assets and game API
    -> authoritative C# simulation
```

The existing 443/8443 routes were preserved. No public Funnel, router forwarding,
Docker reconfiguration, new login service, or unattended startup was added.
Commands require same-origin JSON and instance/revision validation. This is one
shared lab with host-side campaign saves. Per-user authorization remains a
future design question.

The PC, network, and managed host must stay available. Restarting the host
restores the latest readable campaign checkpoint and pauses simulation time.
See [hosting](HOSTING.md) for restart and rollback, [campaign lifecycle](CAMPAIGN_LIFECYCLE.md)
for save and recovery, and [System Lab](SYSTEM_LAB.md) for time and commands.

References: [Tailscale Serve](https://tailscale.com/docs/reference/tailscale-cli/serve),
[Page visibility](https://developer.mozilla.org/en-US/docs/Web/API/Page_Visibility_API),
[Viewport/zoom](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/meta/name/viewport).
