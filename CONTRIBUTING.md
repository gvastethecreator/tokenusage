# Contributing to TokenUsage

Contribute a focused fix, provider integration, test, documentation update, or interface change. Start with an issue so the maintainer can agree on the scope and required evidence.

## Start with an issue

Every pull request needs an issue first.

1. Search the existing issues.
2. Open the matching issue form.
   - Use a [bug report](https://github.com/gvastethecreator/tokenusage/issues/new?template=bug_report.yml) for repeatable failures.
   - Use a [change proposal](https://github.com/gvastethecreator/tokenusage/issues/new?template=feature_request.yml) for focused product changes.
   - Use a [provider integration request](https://github.com/gvastethecreator/tokenusage/issues/new?template=provider_integration.yml) for provider work.
3. Describe the problem, intended result, evidence, and limits.
4. Agree on the scope before writing a large change.
5. Link the issue from the pull request with `Closes #123`.

A pull request without a linked issue can be closed.

Report security issues through [GitHub private vulnerability reporting](https://github.com/gvastethecreator/tokenusage/security/advisories/new). Do not open a public issue.

## Prepare the repository

- Use Windows 10 version 1809 or later on `x64` or `ARM64`.
- Install the .NET 10 SDK.
- Install Visual Studio with MSBuild, Windows app packaging tools, and Windows SDK `10.0.26100.0`.
- Read the [provider matrix](docs/PROVIDER-MATRIX.md) for sources, limits, and publication gates.

Do not add Playwright or another browser runner to the product, solution, package, or CI.

## Make the change

1. Create a branch from the current `main` branch.
2. Keep the diff tied to one issue.
3. Use existing public seams and project patterns.
4. Reuse existing coverage. Extend a focused test when a meaningful failure is not covered.
5. Update public docs when behavior, provider coverage, or a contract changes.
6. Keep unrelated formatting and refactors out of the pull request.

Use `TokenUsage` in namespaces, package metadata, CLI contracts, and user-facing copy.

## Provider contributions

Provider integrations need more than a parser and a green unit test.

Document these items in the issue:

- exact provider product and tested version
- Windows version and architecture
- public API, official export, or bounded local aggregate used as the source
- fields read and fields intentionally excluded
- quota, token, cost, reset, coverage, and freshness semantics
- account types and failure states
- terms, policy, and privacy limits
- reproducible commands or steps.

Do not add reads of another application's credential store. The documented Claude account-limits opt-in reads only the existing Claude Code access token, expiry, and plan fields in memory; Claude Code owns renewal. It must never persist or log that token. Account-management flows must use their existing secure stores. Never store prompts, responses, commands, tool calls, emails, or full local paths in usage records or diagnostics.

The [contributor testing guide](docs/CONTRIBUTOR-TESTING.md#provider-publication-gate) defines the publication gate and the evidence to supply when maintainers cannot access your provider. Keep original evidence outside the repository and commit only sanitized fixtures. Mocked data proves deterministic behavior; it does not prove discovery, permissions, compatibility, or real totals.

The provider remains `Prepared`, `Experimental`, or `PolicyBlocked` until reproducible evidence closes its publication gate.

## Verify the change

At a verification boundary, use the narrowest existing check that proves the changed behavior. Reuse a passed result while its code and environment are unchanged. Before requesting review, run the complete Windows gate:

```powershell
.\scripts\check.ps1 -Platform x64 -Configuration Release
```

For UI changes, verify the affected states in the packaged app, including keyboard access, text scale, high contrast, and reduced motion. A successful build does not prove visual behavior. Use the [testing guide](docs/CONTRIBUTOR-TESTING.md) to select the required evidence for other changes.

If a required check cannot run, explain the exact blocker and the unverified risk in the pull request.

## Open the pull request

Complete the pull request template. Include:

- the linked issue
- the user-visible result
- the source and privacy review for provider data
- tests and commands that ran
- packaged runtime evidence for UI or provider changes
- remaining limits or blocked checks.

Do not include credentials, customer content, private paths, or raw provider databases in commits, logs, screenshots, issues, or pull requests.

Contributions use the same [MIT License](LICENSE) as TokenUsage.
