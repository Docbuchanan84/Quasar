# Development log

Keep concise entries for completed work. Include the date, result, validation,
decisions/pending questions, and one next step. Commit the entry with the related
change so Git history and notes remain connected. This is an implementation log;
the owner's shared Google Doc remains the design discussion record.

## 2026-11-08 — Public repository and version-control foundation

- Owner approved a public GitHub repository and explicitly chose MIT licensing.
- Prepared `main`, a reproducible CI verification workflow, contribution guidance,
  and this ongoing development log. Actions are pinned to reviewed commit SHAs.
- Separated private deployment addresses, owner-only source access, and full
  document snapshots from the public source. Local copies remain recoverable.
- Existing foundation: headless C#/.NET 10 library; ASP.NET serving a built React
  skeleton and read-only health endpoint; React/Babylon.js/PixiJS approved roles.
- Mobile play is a pillar; the owner confirmed the private skeleton on iPhone 17
  Pro. No simulation clock, game-state API, maps, or persistence exists yet.
- Validation: local build/format/lint/type/bundle and renderer checks pass. GitHub
  CI and remote publication are verified during the repository bootstrap.
- Limit: private hosting currently uses a foreground process; unattended startup
  remains unresolved. The simulation test project has no behavior tests yet.
- Next implementation task: the minimal headless universe clock with focused
  behavior tests. Durable private hosting is a separate operational follow-up.
