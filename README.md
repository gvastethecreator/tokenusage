# TokenUsage

TokenUsage is a Windows app for viewing AI coding-tool quota, token use, and spend from sources already available on the computer. It does not require a TokenUsage account.

> TokenUsage is in active pre-release development.

## What works today

- Codex quota and reset windows through the official local `app-server` process.
- Local usage and cost views for Codex, Grok Build, OpenCode, and passive Antigravity databases.
- The Claude reader remains available for a later opt-in, but it is not active by default.
- Vercel AI Gateway code is retained but its provider is temporarily disabled.
- Daily usage heatmaps, provider details, configurable colors, and quota alerts.
- Local Codex reset history, including early resets detected from authoritative usage drops.
- Codex reports can use the current or a previously observed reset cycle as their date range.
- English and Spanish UI.
- A packaged `tokenusage` command for usage, limits, provider status, and diagnostics.

Provider data can be authoritative, local, estimated, incomplete, stale, blocked, or unavailable. The UI labels the source and state instead of treating each value as a remote quota.

## Privacy and security

TokenUsage reads the smallest useful local data source for each provider. It does not index prompts, conversations, tool calls, or commands. Provider support requires a public contract, an approved local aggregate, or an explicit key supplied by the user.

- User-supplied keys go to Windows Credential Locker.
- Local API access and telemetry stay off by default.
- Credentials and customer content must not enter logs, diagnostics, fixtures, or the repository.

See [SECURITY.md](SECURITY.md) to report a security issue.

## Requirements

- Windows 10 version 1809 or later, on `x64` or `ARM64`.
- .NET 10 SDK.
- Visual Studio with MSBuild, Windows app packaging tools, and Windows SDK `10.0.26100.0` for the full packaged build.

The app uses C#, WinUI 3, Windows App SDK, and a full-trust MSIX package. `AnyCPU` and `x86` are not supported.

## Build and run

From PowerShell at the repository root:

```powershell
.\BuildAndRun.ps1 src\TokenUsage.App\TokenUsage.App.csproj -SkipRun /p:Platform=x64
```

Build and launch with package identity:

```powershell
.\BuildAndRun.ps1 src\TokenUsage.App\TokenUsage.App.csproj -Detach /p:Platform=x64
```

The build helper launches the packaged app through `winapp`. Do not run the packaged executable directly.

Run the local quality gate:

```powershell
.\scripts\check.ps1 -Platform x64 -Configuration Release
```

Use `-Platform ARM64` for a cross-architecture package build. Tests still run on the `x64` host.

## Command line

After installing the package and enabling its execution alias:

```powershell
tokenusage refresh
tokenusage usage --days 7 --format human
tokenusage report --days 30 --format human
tokenusage report --from 2026-07-01 --to 2026-07-31 --agent codex --format json
tokenusage limits --format json
tokenusage providers --format human
tokenusage doctor --format human
```

Run `tokenusage refresh` before `usage` or `report` when the app has not updated
the local store. The refresh reads the installed local providers and writes only
normalized numeric usage records.

`report` shows totals, token types, agents, models, high-cost days, daily history, and price coverage. Reported and estimated costs stay separate. The JSON contracts are versioned as `tokenusage.refresh.v1`, `tokenusage.usage.v1`, `tokenusage.report.v1`, `tokenusage.limits.v1`, `tokenusage.providers.v1`, and `tokenusage.doctor.v1`.

Every fresh Codex limits observation also updates `history/quota-resets.v1.json`.
TokenUsage records one reset when an observed official limit returns from used
quota to 100% remaining. The reported boundary distinguishes scheduled resets
from early resets, and repeated refreshes at 100% do not increase the count. It
does not invent resets before tracking began. Codex reports show the observed
reset count for their active period and provide previous/next navigation across
the recorded reset cycles.

## Project map

- `src/TokenUsage.App`: WinUI application and composition.
- `src/TokenUsage.Core`: portable domain contracts.
- `src/TokenUsage.Providers`: provider adapters.
- `src/TokenUsage.Platform.Windows`: Windows services.
- `src/TokenUsage.Runtime.Windows`: shared Windows runtime composition.
- `src/TokenUsage.Cli`: packaged command-line app.
- `tests`: architecture, core, provider, platform, and CLI tests.
- `docs`: product, architecture, provider, research, design, and evidence records.

Start with the [product specification](docs/PRODUCT-SPEC.md), [provider matrix](docs/PROVIDER-MATRIX.md), [implementation plan](docs/IMPLEMENTATION-PLAN.md), and [Windows architecture decision](docs/architecture/ADR-0001-windows-native-baseline.md).

## Provider policy

TokenUsage has no shared login service. Each provider must expose a source that can be used without copying another app's session token or reading its credential store. If the remaining quota cannot be read under that rule, TokenUsage may show observed local usage or spend with clear coverage and pricing notes.

Research gates for planned providers live under [`docs/research`](docs/research). A documented gate does not mean that the provider is implemented.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a change. Keep secrets and real customer data out of issues, commits, screenshots, and tests.

## License

TokenUsage is available under the [MIT License](LICENSE). Third-party material and its terms are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

TokenUsage is an independent project. Provider names and marks belong to their owners. OpenUsage is a reference implementation and does not endorse this project.
