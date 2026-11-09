# Development setup

## Requirements

The maintained setup scripts currently target Windows x64 and PowerShell 7.
The browser client and .NET projects use standard cross-platform toolchains,
but Linux/macOS setup scripts are not supplied yet.

| Tool | Project baseline |
| --- | --- |
| .NET SDK | `global.json`: 10.0.403, newer 10.0.4xx patches allowed; previews excluded |
| Node | `.node-version`: 24.13.0; client accepts compatible Node 24 |
| npm | Major 11; package-manager baseline 11.14.1 |
| Git | Recent Git with HTTPS access to GitHub |
| Editor | VS Code or an appropriate .NET IDE; neither is needed by CI |

The first workstation audit verified these versions. Exact current dependencies
are recorded in the npm and NuGet lock files. Private machine inventory and
connection details are kept outside the public repository.

## Clone and restore

```powershell
git clone https://github.com/Docbuchanan84/Quasar.git
Set-Location Quasar
.\scripts\Setup.ps1
.\scripts\Verify.ps1
```

Setup uses a suitable installed SDK or downloads the pinned Windows x64 SDK from
Microsoft, checks its SHA-512 against official release metadata, and extracts it
to `%LOCALAPPDATA%\Quasar\dotnet`. An existing incompatible local folder is
reported rather than overwritten. No system installer or permanent PATH edit is
required. Node/npm and PowerShell must already be installed.

Setup uses `dotnet restore --locked-mode` and `npm ci`. Network access is needed
for uncached packages. No database, Docker, cloud runtime, private Google Doc,
or Tailscale connection is required to build and run locally.

For individual .NET commands:

```powershell
. .\scripts\Use-DevEnvironment.ps1
dotnet --version
dotnet test Quasar.slnx
```

The environment script selects the local SDK for that terminal only. An alternate
SDK root can be supplied with `QUASAR_DOTNET_ROOT`. No extra `dotnet workload`
installation is needed for these ordinary C#/ASP.NET projects.

## Run locally

In separate terminals:

```powershell
.\scripts\Start-Server.ps1  # http://127.0.0.1:5080/api/health
.\scripts\Start-Client.ps1  # http://127.0.0.1:5173
```

Vite proxies `/api` to the backend. The development backend alone may return 404
at `/` because it does not contain built client assets. Stop with Ctrl+C. Do not
start the development backend while the published host already uses port 5080.
See [HOSTING.md](HOSTING.md) for a built single-origin client/API publication.

## Prepared frontend

React/React DOM 19.3.1, TypeScript 7.0.2, and Vite 8.3.6 form the initial browser
baseline. Added exact renderer dependencies are `@babylonjs/core` 9.35.0 and
`pixi.js` 8.22.0. See [frontend architecture](FRONTEND_ARCHITECTURE.md).

`Verify.ps1` restores locked NuGet packages, builds .NET in Release, invokes the
test runner, checks .NET formatting, then runs frontend lint, the renderer
package/browser-bundle check, nine Node navigation and two coordinate checks,
TypeScript compilation, and Vite build.

The renderer check uses Babylon NullEngine and a Pixi display object and builds
a separate in-memory browser bundle. It does not establish browser GPU support,
visual correctness, or performance. The .NET suite covers simulation, route
search, host sessions, checkpoint validation, and SQLite campaign recovery.

## Editor and maintenance

VS Code workspace tasks run verification and each development server. The
workspace recommends C# Dev Kit; install it optionally through VS Code or with
`code --install-extension ms-dotnettools.csdevkit`. The CLI workflow does not
require it. Visual Studio Community is also optional; use a version supporting
the pinned .NET SDK and select ASP.NET/web development if using that IDE.

For deliberate dependency updates, use `npm install` or
`dotnet restore --force-evaluate`, review changed locks, and rerun verification.
Keep the local SDK serviced alongside `global.json`. Do not install additional
map/database/physics libraries simply because they may be useful later.

## CI and known limits

GitHub Actions runs the same Setup/Verify scripts on a Windows runner for `main`
pushes, pull requests, and manual dispatch. No repository secrets are required.
See [CONTRIBUTING.md](../CONTRIBUTING.md) and the [development log](DEVLOG.md).

The owner confirmed the System Lab's initial phone experience and supplied
portrait 2D screenshots. Exact iOS/browser versions, physical-phone 3D/full
lifecycle coverage, cellular-only access, and measured GPU capacity remain open.
See [mobile evidence](MOBILE_AND_REMOTE_ACCESS.md).
The deployed host uses a separate PowerShell process. Unattended startup is open.
For manual recovery, use Start-HostedApp.ps1 in a terminal that stays open.
See HOSTING.md for the stopped-host/empty-502 recovery. Original audit documents remain recoverable in the
owner's private local context. Generated files should not be committed; `.gitignore`
does not itself prevent a file-sync service from syncing build directories.

## Task 002 setup notes (2026-11-08)

No system SDK/PATH, Docker, Tailscale, or runtime dependency changes. The existing
xUnit project also references the server for session tests; locked restore was
refreshed accordingly. Vite's lazy Babylon chunk exceeds its 500 kB warning level
(~1.09 MB raw/~265 kB gzip); builds pass. Broader phone GPU/performance checks and
unattended startup remain unresolved. Use a separate loopback port such as 5090
for isolated API tests while the published host occupies 5080.

## Map navigation checks (2026-11-08)

`Verify.ps1` also runs `npm run test:maps`: nine Node/Babylon tests for tap/drag
boundaries, pinch handoff/cancellation, input mappings after attachment, focus,
reset, scaling and camera constraints. No new test/runtime package is needed.
The optional real-browser mouse/native-touch script and its isolated-host setup
are documented in [MAP_NAVIGATION.md](MAP_NAVIGATION.md). No new setup blocker;
physical iPhone/Safari qualification remains open.
