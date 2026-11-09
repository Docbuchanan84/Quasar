# Shared design collaboration

## Responsibilities of each record

- The owner's shared Google **Quasar — Game Design Record** holds project design,
  questions, decisions, progress, and cross-chat handoffs. Quasar does not use Obsidian.
- GitHub holds the public codebase, version history, pull requests, automated checks,
  implementation decisions, milestones, and dated development notes.
- `docs/SOURCES.md` indexes these roles. `docs/DEVLOG.md` records completed work;
  `docs/DECISIONS.md` distinguishes Proposed / Approved / Rejected / Deferred.
- Code/tests establish implemented behavior; a library choice or plan is not proof
  of implementation, correctness, performance, or scale.

The Google Doc link, full source snapshots, deployment address, and original
machine audit are private context. On the owner's PC, use the ignored
`docs/LOCAL_PROJECT_CONTEXT.md`. Public clones do not include that file.
Do not publish owner-only documents to make a source link work.

## Verified connection history

The 8 November 2026 release documentation update added a current implementation
section in the live record using a guarded native Google Docs edit. Readback
confirmed the new heading structure, release links, and retained design baseline.

During the initial setup, the existing authenticated Google Drive plugin fetched
the record and native Google Docs structure/revision. Requested edits were made
with revision guards and verified by exact content readback. Read and edit access
worked in that session; future sessions must discover and verify their own tools
rather than assuming another ChatGPT/Codex session's access.

No dummy write, new bridge, Obsidian access, or automatic synchronization is needed.
If authentication expires, use the official plugin reconnect flow. Ask the owner
for missing source access rather than guessing design decisions.

## Ongoing work

1. Establish the task's scope and read the relevant current sources.
2. Preserve confirmed/candidate/open distinctions. The owner approves material
   product/architecture decisions. Update the Google Doc when the requested task
   calls for a shared design/tracking edit; verify the completed external write.
3. Implement on a focused branch, validate, and update the development log and
   relevant decisions/plan/setup documents with the same change.
4. Commit and push completed authorized project work; use pull requests and CI
   for subsequent changes. Report the commit/PR and checks in the task handoff.
5. Include one next step and actual limitations. Do not assume chats share hidden
   context or filesystem state. Keep a concise summary in reusable documents.

The initial public bootstrap is authorized directly on `main`. Later work follows
[CONTRIBUTING.md](../CONTRIBUTING.md). Public source availability does not expose
the owner's private running service or change Google Doc permissions.

## Agent instructions and current limits

Root `AGENTS.md` records the approved frontend, mobile pillar, private hosting
boundary, source roles, and contribution workflow. The updated file was read back
in the working session; automatic loading in a fresh session was not separately
tested. The project-specific no-Obsidian rule overrides general guidance only
for Quasar. Existing global integrations were not changed.

The private System Lab is available for phone play. See [hosting](HOSTING.md)
for startup and recovery and the [project plan](PROJECT_PLAN.md) for current
validation and implementation work.
