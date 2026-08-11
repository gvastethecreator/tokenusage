# Contributing to TokenUsage

TokenUsage welcomes focused fixes, tests, documentation, and provider research. The project is in pre-release development, so discuss large changes before writing them.

## Before you start

- Use Windows 10 version 1809 or later on `x64` or `ARM64`.
- Install the .NET 10 SDK.
- Install Visual Studio with MSBuild, Windows app packaging tools, and Windows SDK `10.0.26100.0`.
- Install Python 3 to run the project skills under `.agents/skills/`.
- Use Node.js and Playwright only for the optional `docs/codemap/codemap.html` browser smoke.
- Read the [product specification](docs/PRODUCT-SPEC.md), [provider matrix](docs/PROVIDER-MATRIX.md), and relevant architecture decision.

Do not add browser automation packages to the solution, application, MSIX package, or product CI.

## Make a change

1. Create a branch from the current `main` branch.
2. Keep the change small and tied to one problem.
3. Add or update focused tests.
4. Update the related document when behavior, provider coverage, or a public contract changes.
5. Run the local checks that cover the changed path.

Use `TokenUsage` in project files, namespaces, package metadata, CLI contracts, and user-facing copy.

## Verify

Run the complete Windows gate before requesting review:

```powershell
.\scripts\check.ps1 -Platform x64 -Configuration Release
```

For UI changes, also capture the affected real states and check keyboard access, text scale, high contrast, and reduced motion. A successful build alone does not prove the UI result.

## Provider and privacy rules

- Use a public provider contract, an approved local aggregate, or a key entered by the user.
- Do not copy another app's session token or read its credential store.
- Do not index prompts, conversations, commands, tool calls, or customer content.
- Keep real credentials and private data out of source, tests, snapshots, screenshots, logs, issues, and pull requests.
- Label quota, observed use, spend, estimates, coverage, age, and blocked states accurately.

Fixtures must use obvious fake values. Security issues follow [SECURITY.md](SECURITY.md) and must not be reported in public issues.

## Pull requests

Explain the user effect, the source of provider data, the tests run, and any limits that remain. Keep unrelated formatting or refactors out of the change.

TokenUsage uses the [MIT License](LICENSE). Contributions are submitted under the same license.
