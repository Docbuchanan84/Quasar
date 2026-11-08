# iPhone play and remote access

## Approved pillar — first-class iPhone browser play

**Approved by the owner on 2026-11-08:** Quasar will frequently be played from
the owner's **iPhone 17 Pro** browser. Phone usability is a core design pillar
from the beginning. Desktop remains supported; simulation depth and authority
remain in the C# backend. The installed iOS/browser version is not yet recorded.

Design core inspection, map selection, orders, pause/time controls, and management
workflows for comfortable touch use on a phone. Preserve depth through focused
views, drill-down, filtering, and progressive disclosure. A phone user should be
able to play, not merely watch a desktop session.

## Implementation guidance

These are guidelines and acceptance goals for future UI work, not claims that
the current placeholder implements them:

- Support portrait for core workflows and useful landscape layouts. Reflow
  inspectors into drawers/sheets or focused pages. Keep large-table scrolling
  inside the relevant region rather than forcing the whole page sideways.
- Use readable type and generous touch targets; approximately 44 by 44 CSS pixels
  is a project target for primary actions. Avoid hover-only information, required
  right-click/keyboard commands, and small unlabeled icon controls.
- Provide deliberate map pan/zoom/selection behavior with clear gesture modes.
  Resolve conflicts between map gestures, scrolling, and browser navigation.
- Respect display safe areas, browser chrome, the software keyboard, text scaling,
  and browser zoom. Do not disable page zoom to hide layout problems.
- React owns the responsive controls; Pixi supplies 2D maps and Babylon supplies
  3D views. Load the needed view, stop unnecessary work in hidden views, and
  choose detail/pixel density with battery, heat, memory, and bandwidth in mind.
  The approved libraries do not establish a mobile performance budget.
- Treat app switching, screen lock, backgrounding, network changes, and reconnect
  as ordinary conditions. Reload authoritative server state when needed and show
  clear connected/synchronizing/disconnected states. Do not silently duplicate
  an order after an uncertain acknowledgement or calculate canonical time in a tab.
- Keep costly simulation work on the host. Graphics still execute on the phone;
  a fast PC does not remove mobile GPU or memory limits.

Safari is the recommended initial browser baseline. Validate on the actual
iPhone 17 Pro, record its iOS/browser version, and also check desktop. Responsive
desktop emulation helps layout checks but does not prove phone rendering,
touch behavior, suspension/resume, thermal behavior, or network reliability.

## Approved remote access — live skeleton

The owner approved setup and requires access away from home. The live skeleton
runs on this PC using private **Tailscale Serve over HTTPS** for the
owner's phone. Tailscale is the access network; the PC remains the application
host. The phone needs the Tailscale app connected to the appropriate tailnet and
permission to reach the app, plus a working network connection.

The owner's private address is in the ignored `docs/LOCAL_PROJECT_CONTEXT.md`.
The owner confirmed it
works on the iPhone 17 Pro. See [HOSTING.md](HOSTING.md) for run/restart controls.

Deployed skeleton path:

```text
iPhone browser + Tailscale
    -> private HTTPS endpoint via Tailscale Serve
    -> local ASP.NET host serving built React assets and the API
    -> authoritative C# simulation
```

One HTTPS origin now serves the built frontend and read-only health API. The
phone does not use the Vite development port or a PC `localhost` address. The
skeleton handles online/offline and foreground-resume checks. Game API commands,
application authorization, and unattended service supervision remain future work.
WebSockets remain an optional transport.

Use a distinct route/port arrangement that preserves existing services. Public
Funnel, router port forwarding, exit nodes, and automatic two-way synchronization
are not needed for this private app path. Quasar uses HTTPS 9443 to proxy local
5080. The existing 443/8443 routes were compared and preserved.

The PC, application process, power, and home internet must remain available.
An asleep or powered-off PC cannot serve the game. Unattended startup, restarts,
saves/backups, and a durable startup policy still need follow-up. The current
host runs in a managed foreground terminal, not an unattended service. Automatic
background launch was rejected by approval review and a failed trial task was
removed; see [hosting limits](HOSTING.md#current-process-lifetime).
If availability beyond this PC's uptime is needed later, evaluate an always-on
host then. Tailscale cannot guarantee access without a working network path.

## Live workstation inspection (2026-11-08)

- Tailscale 1.102.3 reports `Running`, with this PC online.
- Existing tailnet-only Serve HTTPS routes use port 443 to proxy local port 3000
  and port 8443 to proxy local port 8081. Their application identities were not
  inspected. Preserve both when planning Quasar access.
- The authorized follow-up deployed built React assets and read-only `/api/health`
  through ASP.NET, plus private Serve HTTPS 9443. There are no game endpoints.
- The owner confirmed actual phone access. Firewall and power settings were not
  changed; other Serve routes were preserved. Unattended startup is not enabled.

## Validation and unresolved choices

The initial actual-phone page check passed. Continue validation over both
Wi-Fi and cellular: load, touch selection, inspection, a server-acknowledged
command, portrait/landscape, keyboard use, lock/unlock, app switching, reconnect,
and an unavailable host. Measure payload size, frame time, memory pressure, and
battery/thermal behavior before setting budgets or claiming readiness.

Still open: iOS/browser version, endpoint allocation, host process supervision,
app authorization, save/recovery policy, offline behavior, and whether simulation
continues or pauses when the player disconnects. Frequent mobile access does
not decide those game-time semantics. Home-screen/PWA conveniences can be
evaluated later; no native app or offline simulation is required by this pillar.

References: [Tailscale Serve](https://tailscale.com/docs/reference/tailscale-cli/serve),
[Tailscale on iOS](https://tailscale.com/docs/install/ios),
[Page visibility](https://developer.mozilla.org/en-US/docs/Web/API/Page_Visibility_API),
[Viewport/zoom](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/meta/name/viewport).
