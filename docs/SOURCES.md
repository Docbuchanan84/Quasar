# Quasar project sources

## Public implementation sources

- `README.md`: current implementation status and entry points.
- `docs/DECISIONS.md`: approved choices, proposals, and open technical questions.
- `docs/PROJECT_PLAN.md`: incremental milestones.
- `docs/DEVLOG.md`: dated development results and validation.
- Code, tests, pull requests, and CI: implemented behavior and its verification.

## Owner's shared design record

The owner maintains **Quasar — Game Design Record** in Google Docs for project
knowledge, design discussions, questions, decisions, progress, and handoffs.
Quasar does not use Obsidian. The record distinguishes confirmed directions,
candidate mechanisms, and open questions; exploratory ideas are not approvals.

The Google Doc's access details are private working context. On the owner's PC,
consult the ignored `docs/LOCAL_PROJECT_CONTEXT.md` for the live link and local
snapshot location. Full source snapshots under `docs/sources/` are intentionally
excluded from the public repository. Do not publish those files or assume that
possession of a URL establishes authenticated access.

For design work that depends on current private content, use the available
Google Drive/Docs connector to verify a live read. If access is unavailable, say
which public or dated local material was used and ask the owner for the necessary
context. Building and running this repository does not require Google access.
Do not create competing knowledge vaults or automatic two-way synchronization.

## Established directions reflected publicly

- An authoritative, headless C# simulation independent of presentation.
- True 3D galactic space and continuous planetary coordinates.
- Variable time advancement and persistent causality; scheduling/replay details
  still require modeling and validation.
- React for controls/data UI, Babylon.js for 3D, and PixiJS for 2D maps.
- Frequent iPhone browser play as a pillar, with iPhone 17 Pro as the initial
  physical phone target alongside desktop.
- Private remote access through Tailscale Serve. The code is public; the owner's
  running instance and design-document permissions are not made public by GitHub.

See [decisions](DECISIONS.md) for approval scope and [collaboration workflow](SHARED_DESIGN_WORKFLOW.md)
for how implementation notes and the shared design record relate.
