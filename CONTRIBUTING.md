# Contributing to Quasar

Quasar is in early development. Start with the README, project plan, and decision
record. The simulation library must remain independent of the browser and host.

## Development

Use Windows x64, PowerShell 7, the .NET SDK selected by `global.json`, and the
Node version in `.node-version`. Clone the repository, then run:

```powershell
.\scripts\Setup.ps1
.\scripts\Verify.ps1
```

Use the same verification command before submitting changes. It restores locked
NuGet packages, builds .NET, runs the test runner and formatter, then checks the
frontend lint, renderer packages, TypeScript, and Vite build. The current
simulation test project has no behavior tests; add focused tests with real
simulation behavior rather than treating an empty runner as a test-suite pass.

## Version control and notes

1. Start from current `main` and create a focused branch. Agent branches use
   `codex/<short-task-name>`.
2. Make a coherent change and update `docs/DEVLOG.md` with the result, validation,
   decisions/pending questions, and next step. Update the decision record or plan
   when the change affects them.
3. Run verification and review the diff, including newly added files. Keep
   dependency lock changes intentional. Never add credentials, private connection
   addresses, owner-only source documents, generated output, or live game data.
4. Commit with a descriptive message, push the branch, and open a pull request.
   CI must pass before merging. Resolve review discussions and use a clear merge
   message. Do not force-push or rewrite shared history without an explicit need
   and owner authorization.

The initial repository bootstrap may be committed directly to `main`; subsequent
work should use branches and pull requests. Avoid mixing unrelated changes.

## Design discussions

Public implementation decisions and progress live in repository documents and
pull requests. The owner also maintains a shared Google Game Design Record;
its access details and full snapshots are private working context. Do not infer
approval from exploratory ideas. Ask the owner for relevant context if a task
depends on information that is not public. Quasar does not use Obsidian.

## License

Contributions are provided under the repository's MIT license. Keep third-party
code and assets appropriately licensed and attributed.
