---
name: tokenusage-maintenance
description: Maintain TokenUsage after AI provider releases. Update provider compatibility, model pricing, and project dependencies; find and fix bugs; review architecture and reduce resource use while preserving data and product quality. Use for a TokenUsage maintenance pass or provider update review, with the user's requested scope.
---

# TokenUsage maintenance

Use this skill in the TokenUsage repository. All command and code paths below
are relative to its root. A full maintenance pass covers provider updates,
dependencies, and a bounded project review. Honor a narrower request, including
a review without edits.

Update TokenUsage's integrations; installing or upgrading the providers' own
applications is a separate action. Follow the current session's authority for
commits, branches, remote writes, installation, and release. Previous entries in
`tasks.json` or scratch records are evidence, not renewed authorization.

## Establish the scope

1. Read the applicable repository instructions and inspect the working tree.
   Identify which provider releases prompted the pass and preserve existing work.
2. Use `TokenUsage.slnx` and the current provider catalog as the inventory. Do not
   copy a fixed list of providers or package versions into this skill.
3. Read the relevant sections of the sources below. Before product edits, use
   `docs/codemap/codemap.lock` and the affected map section to find callers,
   dependencies, tests, and flows. Treat incomplete static analysis as incomplete.
   If the required map is stale or cannot answer the impact question, use the
   available `maintain-code-map` skill.
4. Reuse `tasks.json` and the related working record for a maintenance run that
   needs tracking. Keep evidence, decisions, valid checks, blockers, and the next
   action together. Do not create a second tracker or a report for a small edit.

| Area | Owning sources |
|---|---|
| Provider identity, capabilities, sources, and support limits | `src/TokenUsage.Providers/Catalog/ProviderModuleCatalog.cs`, `src/TokenUsage.Runtime.Windows/Providers/WindowsProviderCatalog.cs`, [provider matrix](../../../docs/PROVIDER-MATRIX.md) |
| Pricing and its evidence | `src/TokenUsage.Providers/Pricing/`, provider pricing catalogs, [pricing rules](../../../docs/PRICING.md) |
| Dependencies and packaging | Project files in `TokenUsage.slnx`, `scripts/package.ps1`, package manifest, [dependency maintenance](../../../docs/DEPENDENCY_UPDATES.md) |
| History, recovery, and account identity | [usage storage](../../../docs/USAGE-STORAGE.md) |
| Verification and provider activation | [contributor testing](../../../docs/CONTRIBUTOR-TESTING.md), [contribution rules](../../../CONTRIBUTING.md) |

## Update providers and prices

- Compare the affected installed or requested provider version with current
  official release notes, API/schema documentation, or official source. Record
  the version, source link, and review date. Distinguish documentation checks,
  sanitized fixture checks, and live Windows observations.
- Trace discovery, bounded reads, parsing, normalization, account selection,
  storage, CLI output, and presentation for each changed contract. Check source
  precedence, cumulative versus incremental counters, units, reset times, local
  date ranges, repeated imports, and account changes where affected.
- Preserve stable event identity and reconciliation. A repeated refresh must
  not duplicate usage; a partial scan or missing source must not erase history.
  Keep reported cost, estimated cost, credits, quota, unknown models, and missing
  data distinct. Preserve the public CLI JSON contract.
- Review prices from official model-price rows, including host-specific rates,
  cache reads/writes, context thresholds, effective dates, and promotions. Update
  the owning catalogs and typed evidence together. Unknown or insufficiently
  described usage stays unpriced; do not invent a rate or infer request sizes
  from session totals.
- `tokenusage pricing refresh --dry-run` is the existing bounded source check.
  Identify the executable and confirm it matches the checkout under review;
  otherwise use the existing local entry point or state that version mismatch.
  Its markers do not prove correct prices or complete model coverage. Manual
  comparison with official rows is still required; a catalog update must not
  silently rewrite historical usage or discard its pricing provenance.
- Follow the documented source and privacy limits. Do not broaden credential
  access or retain raw conversations, authentication responses, or personal
  paths. Use minimal sanitized fixtures; leave original private evidence out of
  the repository. A fixture alone does not establish real integration support.
- If an account, installation, or approved source is unavailable, finish the
  independent work and state the missing proof. Preserve the existing support
  stage until the applicable provider publication gate is met.

## Update dependencies

For a full pass, run the existing checks and inspect their results:

```powershell
.\scripts\deps-check.ps1
.\scripts\audit.ps1
```

`deps-check.ps1` exits with code 1 when updates exist. `audit.ps1` includes
transitive dependencies and exits with code 1 for vulnerabilities or an
incomplete audit. Read the reported cause before retrying.

Review official release notes and target current stable versions compatible
with the supported application and build contracts. Apply justified updates in
small compatible groups, including aligned references across projects. Report
available updates held back and the concrete blocker; do not call a scan an
update. A breaking platform migration needs its own agreed scope.

Keep the app's Windows App SDK WinUI/Runtime references aligned and the WinApp
helper private. The native package builder uses the app's restored SDK tools
and package metadata; do not duplicate package versions in that script. Avoid
replacing the split SDK packages with an aggregate that adds unused components.
Preserve both packaged executables, their registrations, and the shared portable
`.dotnet` runtime layout. Use the existing .NET/MSBuild toolchain; do not install a new
SDK, package manager, or test framework as an incidental repair.

## Review bugs, architecture, and resource use

Review the major layers once at module level: Core, Providers, Platform.Windows,
Runtime.Windows, Presentation, App, CLI, and packaging. Then follow concrete
findings into their callers and existing tests. Use
`tests/TokenUsage.Architecture.Tests/ArchitectureRules.cs` and project references
to check boundaries; do not impose a new architecture based on file size alone.

Look for duplicated provider knowledge, business rules in UI code, dependency
cycles, stale paths, incorrect error states, refresh races, cancellation gaps,
unbounded reads, repeated scans, full checkpoint rewrites, unnecessary UI work,
and redundant package/runtime assets. Prioritize observed correctness failures
and supported simplifications. Fix related defects through existing public
seams. Leave unrelated redesigns as explicit follow-up findings.

For each performance change, name the cost it should reduce and compare before
and after under the same build, data, and workload. Use existing diagnostics or
scripts for the relevant metric: refresh duration, CPU, allocations/memory,
disk reads/writes, startup, or portable size. Separate cold and unchanged refresh
workloads. Missing measurements remain unknown; fewer lines or dependencies do
not prove a runtime improvement.

Preserve accuracy, coverage, history, privacy, accessibility, and responsiveness.
Do not reduce polling correctness, hide stale/partial states, remove supported
behavior, or weaken validation to make the app appear lighter. When storage or
migration changes are necessary, follow the storage guide and validate against
an isolated copy. Never reset the installed app or overwrite real LocalState as
a test setup.

For refresh/storage optimizations, preserve the shared app/CLI/hook coordination,
pending refresh requests, transactional cursor, and checkpoint recovery order.
An unchanged scan can still need a small durable write for concurrency safety;
do not use zero writes as the acceptance criterion.

## Verify and close

- Review the diff and choose the smallest reliable existing checks at the
  verification boundary. Reuse valid results and extend coverage only for a
  meaningful uncovered failure. Follow the testing guide's evidence by change
  type; parser tests do not prove discovery, live totals, UI, or installation.
- For product or dependency changes, complete the required repository gate
  once before review, or its relevant parts with valid evidence for every part:

  ```powershell
  .\scripts\check.ps1 -Platform x64 -Configuration Release
  ```

  This includes the package build. Opt-in integration tests need their own
  environment. Record unavailable checks and the resulting limit; do not mark
  an unverified provider or change as fully validated.
- Obtain packaged WinUI and CLI evidence when the testing guide requires it.
  Visual changes need rendered proof. Documentation-only changes use link,
  path, command, and formatting checks instead of product builds.
- Update provider coverage, pricing, storage, or dependency docs only where the
  change affects their reader. Keep the existing tracker current for a full
  pass. When architecture boundaries or major flows change, update
  `codemap.json`, `codemap.md`, `codemap.html`, and `codemap.lock` together.
- Inspect the final scope for missed files and scratch artifacts. Summarize
  provider/package versions changed, defects fixed, measured improvements,
  checks and results, deferred updates, and remaining risks. If nothing needs
  changing, say what was checked; do not manufacture a refactor.
