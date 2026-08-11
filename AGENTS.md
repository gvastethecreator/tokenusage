# TokenUsage agent contract

## Issue tracker

GitHub Issues and Project 4 hold live work state. `.scratch/tokenusage/` holds synchronized local context and evidence. See `docs/agents/issue-tracker.md`.

## Triage labels

Use `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, and `wontfix`. See `docs/agents/triage-labels.md`.

## Domain docs

Use the single-context TokenUsage product, provider, architecture, and research documents. See `docs/agents/domain.md`.

## Contributor start

1. Read `README.md`, `docs/PRODUCT-SPEC.md`, and `docs/PROVIDER-MATRIX.md`.
2. Read the ADR and provider research that cover the requested change.
3. Inspect `git status --short --branch` before an edit.
4. Read `docs/codemap/codemap.json` before changing a module.
5. Run the code-map status command from `.agents/skills/maintain-code-map/SKILL.md`.

## Project skills

- Project skills are physical files under `.agents/skills/`.
- `.agents/skills.lock.json` records their source revision and content fingerprint.
- Use `evolve-project-guidance` for changes to project rules or project skills.
- Use `writing-for-agents` for changes to `AGENTS.md` or agent-facing documents.
- Use `simple-english` before writing task, plan, rule, or procedure text.
- If the map is stale or a major code flow changes, use `maintain-code-map`.

## Architecture boundaries

- `TokenUsage.Core` owns portable domain, storage, cache, and coordination contracts.
- `TokenUsage.Providers` references `TokenUsage.Core` and owns provider adapters.
- `TokenUsage.Platform.Windows` references `TokenUsage.Core` and owns Windows integration.
- `TokenUsage.Runtime.Windows` composes Core, Providers, and Platform services.
- `TokenUsage.App` owns WinUI views, view models, and application composition.
- `TokenUsage.Cli` owns commands and stable JSON output.
- `TokenUsage.Package` owns the MSIX manifest, app payload, CLI payload, and execution alias.

## Implementation rules

- Preserve unrelated work. A bounded worker without Git permission must use the parent-recorded checkout state.
- Build the packaged WinUI app for `x64` or `ARM64`.
- Never use `AnyCPU`.
- Never run the packaged executable directly.
- Keep credentials and customer content out of the repo, diagnostics, fixtures, and agent prompts.
- Keep prompts, conversations, commands, tool calls, emails, account identifiers, and full local paths out of usage storage.
- Preserve reported cost, estimated cost, unavailable cost, coverage, and unpriced tokens as separate states.
- Do not copy another application's session token or read another application's credential store.
- Do not add Playwright or another browser runner as a TokenUsage product dependency.
- Treat `docs/codemap/codemap.html` as documentation, not as a web application runtime.
- Treat delegated changes as untrusted until the parent reviews the diff and runs local proof.
- Do not commit, push, publish, install tools, or change product scope unless the current task grants that authority.

## Verification

- Run the narrowest test that can disprove the changed behavior.
- If package proof is required, run `scripts/check.ps1 -Platform x64 -Configuration Release` once at the final integration boundary.
- Use `-Platform ARM64` only for the ARM64 package path. Tests still run on the `x64` host.
- If the UI changes, verify the packaged app and record the affected real states.
- Verify keyboard access, text scale, high contrast, and reduced motion for affected UI states.
- If module boundaries, dependencies, routes, storage, or major flows change, update all three `docs/codemap/` artifacts.
