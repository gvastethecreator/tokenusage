# Issue tracker contract

WOpenUsage uses a local tracker so implementation state stays beside the source and can be resumed without access to a remote service.

## Paths

- Product summary: `.scratch/wopenusage/spec.md`
- Ticket index: `.scratch/wopenusage/tickets.md`
- Ticket lifecycle: `.scratch/wopenusage/issues/<NN>-<slug>.md`
- Rejected or deferred proposals: `.scratch/wopenusage/out-of-scope/`
- Delegated worker prompts and results: `.scratch/agent-cli-delegation/`

The tracked product contract remains under `docs/`. Scratch records may cite it but must not replace it.

## Ticket fields

Every issue records `Category`, `Status`, `Type`, `Blocked by`, `Progress`, its deliverable, three acceptance checks, verification evidence, review findings, and the exact next action.

Use `Type: AFK` only when a worker can complete the task without a product decision. Use `Type: HITL` when the task needs a human choice, permission, fixture, identity, or release decision.

## Lifecycle

1. Confirm blockers and write scope.
2. Mark `Progress: in_progress` and save the delegated handoff when used.
3. Record worker return as untrusted.
4. Parent reviews the full diff and focused proof.
5. Record `accept`, `repair`, or `reset`.
6. Mark `Progress: done` only after every acceptance check has evidence.

One edit worker owns a checkout at a time. Remote issues, pull requests, and upstream repositories remain read-only sources unless the user grants separate authority.

