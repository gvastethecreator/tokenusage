# Contributing to TokenUsage

Thanks for helping improve TokenUsage. Focused fixes, provider research, tests, documentation, and interface work are welcome.

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

A pull request without a linked issue can be closed. This rule avoids duplicate work and unsafe provider integrations.

Report security issues through [GitHub private vulnerability reporting](https://github.com/gvastethecreator/tokenusage/security/advisories/new). Do not open a public issue.

## Prepare the repository

- Use Windows 10 version 1809 or later on `x64` or `ARM64`.
- Install the .NET 10 SDK.
- Install Visual Studio with MSBuild, Windows app packaging tools, and Windows SDK `10.0.26100.0`.
- Install Python 3 for project skills under `.agents/skills/`.
- Read the [product specification](docs/PRODUCT-SPEC.md) and [provider matrix](docs/PROVIDER-MATRIX.md).
- Read the architecture decision and provider research that cover your change.

Do not add Playwright or another browser runner to the product, solution, package, or CI.

## Make the change

1. Create a branch from the current `main` branch.
2. Keep the diff tied to one issue.
3. Use existing public seams and project patterns.
4. Add the smallest test that proves the changed behavior.
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

Never read another application's credential store. Never copy its session token. Never store prompts, responses, commands, tool calls, emails, account identifiers, or full local paths.

### When maintainers cannot test the provider

Supply evidence that another contributor can verify without your account:

1. Add a sanitized fixture that matches the observed data shape.
2. Remove credentials, content, identifiers, and personal paths.
3. Add deterministic tests for parsing, normalization, and failure states.
4. Record the provider version and the exact collection steps.
5. Attach redacted CLI or UI proof from a real Windows installation.
6. Show absent-account, stale-data, and changed-schema behavior.
7. Explain cost provenance and unpriced-token handling.

Mocked data proves deterministic behavior. It does not prove discovery, permissions, product compatibility, or real values.

The provider remains `Prepared`, `Experimental`, or `PolicyBlocked` until reproducible evidence closes its publication gate. TokenUsage does not label unverified support as active.

Read the [contributor testing guide](docs/CONTRIBUTOR-TESTING.md) for the required evidence by change type.

## Verify the change

Run the narrowest relevant test while developing. Run the complete Windows gate before review:

```powershell
.\scripts\check.ps1 -Platform x64 -Configuration Release
```

For UI changes, also verify the packaged app. Record the affected real states. Check keyboard access, text scale, high contrast, and reduced motion.

A successful build does not prove visual behavior. A fixture does not prove a provider works on a real installation.

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
