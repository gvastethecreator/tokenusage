# Contributor testing guide

This guide defines the evidence required for TokenUsage changes. Use it with [CONTRIBUTING.md](../CONTRIBUTING.md).

## Core rule

Prove the changed behavior at the cheapest reliable seam. Add evidence for risks that a lower-level test cannot observe.

Do not treat a build, mock, screenshot, or test count as universal proof.

## Evidence by change type

| Change | Required evidence |
|---|---|
| Parser or normalization | Sanitized fixture and focused deterministic tests |
| Provider discovery | Real Windows detection proof and absent-install behavior |
| Quota or reset logic | Boundary tests, repeated observations, early reset, and scheduled reset behavior |
| Pricing | Reported and estimated paths, unknown model, unpriced tokens, and coverage |
| Storage or migration | Upgrade path, repeat run, corrupt input, and no-content inspection |
| CLI | Focused process test and stable JSON contract when output changes |
| WinUI behavior | Packaged app proof for every affected state |
| Visual change | Before and after evidence at the affected size and theme |
| Accessibility | Keyboard access, text scale, high contrast, and reduced motion |
| Documentation only | Link, command, path, and formatting checks |

Run the complete gate once before requesting review:

```powershell
.\scripts\check.ps1 -Platform x64 -Configuration Release
```

Use `-Platform ARM64` for the ARM64 package path. Tests still run on the `x64` host.

## Provider publication gate

An active provider needs all applicable evidence below:

- source and precedence are documented
- the tested product version and Windows architecture are recorded
- fixtures use sanitized values from a real observed shape
- file reads, database queries, and response sizes have bounds
- timeout and cancellation work
- absent account, stale data, permissions, and changed schema have tests
- several accounts and account changes have defined behavior
- logs and caches contain no secrets or customer content
- reported and estimated cost remain separate
- unknown models remain visible as unpriced
- real totals match a trusted reference over the same period
- the reader does not open authentication or conversation content
- packaged WinUI and CLI paths show the same normalized result
- source, coverage, freshness, and limits appear in the interface.

Record non-applicable items with a short reason. Do not silently omit them.

## Evidence for inaccessible providers

Maintainers cannot hold accounts for every provider. Contributors must make review reproducible without sharing an account.

Provide:

1. A public contract link or a safe description of the local aggregate.
2. The provider version and exact collection steps.
3. A minimal sanitized fixture that preserves field types and units.
4. Tests for parsing, normalization, boundaries, and failure states.
5. Redacted CLI or UI output from the real provider on Windows.
6. A comparison between TokenUsage totals and the provider source.
7. A list of claims that remain unverified.

Use obvious fake identifiers and values in committed fixtures. Keep the original evidence outside the repository.

The maintainer can request a live screen share, a second tester, or more evidence before activation. Until then, keep the provider `Prepared`, `Experimental`, or `PolicyBlocked`.

## UI evidence

Capture only the changed surface. Include every affected state, not one ideal state.

Check:

- empty, loading, success, stale, unavailable, and error states
- light and dark themes
- long provider names and translated text
- keyboard focus and tooltips
- text scaling and narrow layouts
- reduced motion when animation changes
- the installed `x64` or `ARM64` package.

Remove account data, identifiers, and private paths before attaching evidence.

## Pull request report

List commands with exact results:

```text
dotnet test path\to\focused.tests.csproj -> passed 12/12
.\scripts\check.ps1 -Platform x64 -Configuration Release -> passed
Packaged x64 provider refresh -> matched the sanitized reference total
```

List every skipped check and its blocker. State the remaining risk in plain language.
