# Quasar

[![Verify](https://github.com/Docbuchanan84/Quasar/actions/workflows/verify.yml/badge.svg)](https://github.com/Docbuchanan84/Quasar/actions/workflows/verify.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Quasar is a personal, simulation-first 4X/grand strategy project inspired by
Aurora C#. Its intended scope includes interstellar civilizations, exploration,
warfare, populations, industry, research, logistics, politics, and persistent history.

**Design pillar:** frequent iPhone browser play, with the owner's iPhone 17 Pro
as the primary phone test device. Core workflows must be comfortable with touch
and a small screen while retaining simulation depth and desktop support. See
[mobile and remote access](docs/MOBILE_AND_REMOTE_ACCESS.md).

**Status: private hosted skeleton online.** The built React page connects to a
read-only ASP.NET health endpoint over Tailscale HTTPS. The owner confirmed it
works on the iPhone 17 Pro. The simulation library and test project remain empty;
there is no universe clock, gameplay, persistence, or map view yet.

The owner's hosted preview is private; its address is kept in local owner context.
The public repository contains the code and instructions to run your own copy.
The host currently runs in a managed foreground terminal; unattended startup is
not enabled. See [hosting and restart instructions](docs/HOSTING.md).

## Start here

From PowerShell 7 in this folder:

```powershell
.\scripts\Setup.ps1
.\scripts\Verify.ps1
```

Run each application in its own terminal; stop with Ctrl+C:

```powershell
.\scripts\Start-Server.ps1  # http://127.0.0.1:5080/api/health
.\scripts\Start-Client.ps1  # http://127.0.0.1:5173
```

These scripts select Quasar's user-local .NET 10 SDK without changing the machine
PATH or its existing .NET 8 installation. The two run commands belong in separate
terminals. See [development setup](docs/DEVELOPMENT_SETUP.md) for the verified
machine inventory, installation details, and troubleshooting.

## Technology choices and remaining proposals

| Component | Direction | Current state |
| --- | --- | --- |
| Simulation | C# / .NET 10 | Empty independent library |
| Host/API | ASP.NET Core | Serves built client and read-only health API |
| Browser controls/data UI | React / TypeScript / Vite | Mobile skeleton with live connection check |
| Tests | xUnit | Project and runner configured |
| 3D views / globes | Babylon.js | Approved; package installed, views not implemented |
| 2D maps / operational views / plans | PixiJS | Approved; package installed, maps not implemented |
| Persistence | PostgreSQL candidate | Deferred; no schema or database dependency |
| Transport | HTTP; WebSockets when justified | Not implemented |
| Remote access | Private HTTPS via Tailscale Serve — Approved | Live on 9443; phone confirmed; startup pending |
| Source control | Public GitHub / Git | `main`, focused branches, pull requests, and CI |

The owner approved React + Babylon.js + PixiJS on 2026-11-08. MapLibre GL JS is
deferred. See [frontend architecture](docs/FRONTEND_ARCHITECTURE.md) for component
roles and [decisions](docs/DECISIONS.md) for approval scope and other open choices.

## Project sources

The owner's shared **Quasar — Game Design Record** in Google Docs holds design,
project knowledge, and tracking. Access details remain private. Quasar
does not use Obsidian. See [project sources](docs/SOURCES.md) for
its dated local snapshot, provenance, and confirmed/candidate/open distinctions.
It supplies design context; exploratory mechanisms remain proposals.

## Immediate objective

**Task 002: implement and test the smallest deterministic universe clock in
`Quasar.Simulation`.** It should advance by explicit elapsed durations without a
browser, network, database, real-time timer, or wall-clock dependency.
See the acceptance criteria and later browser/backend milestones in the
[project plan](docs/PROJECT_PLAN.md).

## Version control and ongoing notes

Public repository: [Docbuchanan84/Quasar](https://github.com/Docbuchanan84/Quasar).
See [CONTRIBUTING.md](CONTRIBUTING.md) for branches, commits, pull requests, and
verification. [DEVLOG.md](docs/DEVLOG.md) records ongoing implementation notes;
[DECISIONS.md](docs/DECISIONS.md) and the project plan track approved choices and
next work. Update notes with the related code change. Private service details,
source snapshots, credentials, and generated files stay outside Git.

## License

[MIT](LICENSE), as selected by the owner.
