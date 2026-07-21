# WOpenUsage agent contract

## Issue tracker

Project work lives under `.scratch/wopenusage/`. Remote issues, pull requests, and repositories are read-only sources unless the user grants separate write authority. See `docs/agents/issue-tracker.md`.

## Triage labels

Use `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, and `wontfix`. See `docs/agents/triage-labels.md`.

## Domain docs

Use the single-context WOpenUsage product, provider, architecture, and research documents. See `docs/agents/domain.md`.

## Implementation rules

- Preserve unrelated work and inspect `git status --short --branch` before edits.
- Build the packaged WinUI app for `x64` or `ARM64`; never use `AnyCPU` or run its packaged executable directly.
- Keep credentials and customer content out of the repo, diagnostics, fixtures, and agent prompts.
- Treat delegated changes as untrusted until the parent reviews the diff and runs local proof.
- Do not commit, push, publish, install tools, or change product scope unless the current task grants that authority.
