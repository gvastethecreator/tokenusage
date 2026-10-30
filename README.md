<p align="center">
  <img src="docs/assets/github-readme-assets/final/readme-hero.svg" alt="TokenUsage — a silly mascot counting colored tokens" width="100%" />
</p>

<p align="center">
  <a href="https://github.com/gvastethecreator/tokenusage/actions/workflows/ci.yml"><img alt="CI status" src="https://shieldcn.dev/github/ci/gvastethecreator/tokenusage.svg?workflow=ci&branch=main&variant=secondary&size=xs" /></a>
  <a href="https://gvastethecreator.github.io/tokenusage/"><img alt="Project site" src="https://shieldcn.dev/badge/site-pages-087f86.svg?logo=githubpages&variant=branded&size=xs" /></a>
  <img alt="Windows x64 and ARM64" src="https://shieldcn.dev/badge/platform-Windows+x64+%7C+ARM64-0078d4.svg?logo=windows&variant=branded&size=xs" />
  <a href="https://github.com/gvastethecreator/tokenusage/stargazers"><img alt="GitHub stars" src="https://shieldcn.dev/github/stars/gvastethecreator/tokenusage.svg?variant=secondary&size=xs" /></a>
  <a href="LICENSE"><img alt="MIT license" src="https://shieldcn.dev/github/license/gvastethecreator/tokenusage.svg?variant=secondary&size=xs" /></a>
</p>

<p align="center">
  <a href="https://gvastethecreator.github.io/tokenusage/">Project site</a> ·
  <a href="#release-downloads">Download</a> ·
  <a href="#product-tour">Product tour</a> ·
  <a href="#provider-support">Providers</a> ·
  <a href="CHANGELOG.md">Changelog</a> ·
  <a href="#build-and-run">Build</a> ·
  <a href="#command-line">CLI</a>
</p>

A Windows tray app for keeping track of AI coding usage. See your tokens, known cost, and available quota in one place, then open the report when you want the detail. The CLI exposes the same local data for scripts and diagnostics.

No TokenUsage account is required. Provider data stays local unless you enable a documented opt-in connection.

> [!IMPORTANT]
> The current release is an **unsigned Windows x64 portable preview**. [Download preview 5](https://github.com/gvastethecreator/tokenusage/releases/tag/v0.0.1-preview.5). Provider coverage depends on the tool, account, and available data source.

## What you get

- **Fast tray view** — check today's usage, the period, the per-agent breakdown, and quota.
- **A report that answers questions** — will each quota last to its reset, why usage changed from the previous period, and how much work each quota point covered across recent resets.
- **Multiple accounts** — show separate quotas for code-swap (Codex) and claude-swap (Claude) profiles.
- **Honest data states** — reported, estimated, partial, stale, unavailable, and unpriced values remain distinct.
- **Stable CLI output** — read reports, limits, and diagnostics as human text or versioned JSON.
- **Local-first and English-only** — keep the interface predictable without indexing conversations or commands.

## Product tour

### The tray view

Today's and the last 30 days' tokens, spend by tool, and each quota with its reset.

<img src="docs/assets/screenshots/tray-overview.webp" alt="Tray view with today's and 30-day tokens, spend by tool, and Codex and Claude quota windows" width="360" />

### The report

Choose 7, 30, or 90 days. Each period is compared with the one before it.

<img src="docs/assets/screenshots/report-overview.webp" alt="Report summary, weekly Claude and Codex quota pace with a projection to the reset, and tokens per day by agent" width="100%" />

- **Summary** — tokens, approximate spend at API list prices, daily average, and how many quotas run out before their reset at the current pace.
- **Will it last to the reset?** — used quota across each daily or longer window, with earlier cycles in gray and a dashed projection that stops where the quota runs out.
- **Tokens per day** — stacked by agent, with the previous period as a dashed line.
- **Why did it change?** — a waterfall from the previous total to the current one through the agents and models that changed most.
- **Recent resets** — used quota, cycle tokens, and tokens per 1% for the last four cycles of each quota.
- **Detail** — tokens, change, spend, share, and active days by agent, model, or day.

<img src="docs/assets/screenshots/report-changes.webp" alt="Waterfall from the previous period's tokens to the current total by agent and model, and recent weekly resets with tokens per 1%" width="100%" />

The share button saves the whole report as one image. In Options > Appearance, choose rings, bars, or numbers only for compact limits. Rare options live in Options > Advanced.

These captures use synthetic sample data and contain no account data. The banner mascot is an illustration generated with imagegen.

## Provider support

TokenUsage reads twelve tools. Each one has a bounded local reader.

| Provider | Local usage | Cost | Live quota |
|---|---|---|---|
| Codex | Yes | Reported or estimated | Yes, through the official local `app-server` |
| Claude Code | Yes | Reported or estimated | Yes, opt-in account usage (5-hour, weekly all models, and named model limits) |
| Cursor | Yes, partial | Estimated when the model matches | Not available through the current contract |
| Grok Build | Yes | Reported or estimated | Not available through an approved interface |
| ZCode | Yes, counters per request | Estimated when the model matches | Not available through an approved interface |
| OpenCode | Yes | Reported or estimated | No common quota source |
| Antigravity | Yes, experimental | Estimated | Blocked by policy |
| Amp | Yes, partial | Credits stay separate from USD | No stable public source |
| Mux | Yes | Reported | No common quota source |
| Goose | Yes, partial | Estimated when pricing exists | No common quota source |
| Hermes | Yes, partial | Reported or estimated | No common quota source |
| GitHub Copilot | Yes, VS Code chat sessions, partial | Recorded AI credits before plan allowances | No remaining-quota source |

See the [provider matrix](docs/PROVIDER-MATRIX.md) for sources, limits, and publication gates.

TokenUsage never creates fake activity. Missing data appears as missing data.

## How the data works

Each provider adapter reads the smallest approved source that can answer a usage question. Sources include official local APIs, bounded numeric logs, and read-only aggregate database queries.

TokenUsage keeps these values separate:

- provider-reported cost
- cost estimated from known model pricing
- tokens without a known price
- coverage and freshness
- quota remaining and reset time
- observed local usage.

An API-rate estimate is not a subscription invoice. A local usage total is not a remote account quota.

## Privacy and security

- No prompt, response, conversation, command, tool call, email, or account identifier enters usage storage.
- TokenUsage does not copy another application's session token or read its credential store.
- Local API access and telemetry stay off by default.
- Logs, diagnostics, fixtures, issues, and pull requests must not contain credentials or customer content.

Read [SECURITY.md](SECURITY.md) before reporting a vulnerability.

## Requirements

The portable preview runs on Windows x64 and includes its .NET and Windows App SDK runtimes. The app targets Windows 10 version 1809 or later. This preview has been checked on Windows 11; clean-machine qualification is still pending.

Building from source also requires the .NET 10 SDK, Visual Studio with MSBuild and Windows app packaging tools, and Windows SDK `10.0.26100.0`. Source builds support x64 and ARM64; this release includes x64 only.

## Release downloads

[Download v0.0.1-preview.5](https://github.com/gvastethecreator/tokenusage/releases/tag/v0.0.1-preview.5), extract the ZIP, and open `TokenUsage.App.exe`.

This preview is **unsigned and portable-only**. Windows may show a publisher or SmartScreen warning. It has no MSIX installer and is not delivered by automatic updates. See the [release notes](docs/releases/0.0.1.md) for fixes and limits.

The portable ZIP contains the app and CLI. Run `tokenusage.cmd` from its root to use the CLI.

Both executables use the `Data` folder beside the app executable.

Keep `TokenUsage.portable` in the extracted folder. Move the complete folder when you move or update the portable app.

The MSIX and portable builds use separate data folders. Installing one build does not delete or import data from the other build.

Read the [release procedure](docs/RELEASING.md) for build, signature, and publication details.

### Automatic updates

In Options → General, enable automatic updates to check GitHub once every
24 hours while TokenUsage runs. Startup checks respect the same interval.
Updates are off by default. You can also
check and install manually. Only newer stable releases for your architecture are
accepted; downloads must pass GitHub SHA-256 and size verification.

Portable updates apply after exit and preserve the Data folder. Direct MSIX
updates also require a trusted package signature and Windows 10 version 2004 or
later. Store installations use Microsoft Store updates; development builds do
not self-update. Builds released before this updater need one manual upgrade.

## Build and run

Build from PowerShell at the repository root:

```powershell
.\BuildAndRun.ps1 src\TokenUsage.App\TokenUsage.App.csproj -SkipRun /p:Platform=x64
```

Build and launch with package identity:

```powershell
.\BuildAndRun.ps1 src\TokenUsage.App\TokenUsage.App.csproj -Detach /p:Platform=x64
```

The helper launches the packaged app through `winapp`. Do not run the packaged executable directly.

Run the complete local gate before requesting review:

```powershell
.\scripts\check.ps1 -Platform x64 -Configuration Release
```

Use `-Platform ARM64` for a cross-architecture package build. Tests still run on the `x64` host.

For a quick dependency and security pass on active projects:

```powershell
.\scripts\deps-check.ps1
.\scripts\audit.ps1
```

The repository uses the .NET SDK and MSBuild.

## Command line

Install the package and enable its execution alias. Then run:

```powershell
tokenusage refresh
tokenusage report --days 30 --format human
tokenusage report --from 2026-07-01 --to 2026-07-31 --agent codex --format json
tokenusage limits --format json
tokenusage doctor --format human
tokenusage pricing audit --format human
tokenusage pricing refresh --dry-run
```

Pricing evidence keeps direct API rates separate from host-specific rates. The
weekly refresh checks only allowlisted official pages and never stores their
content or edits a price. See [pricing evidence and refresh](docs/PRICING.md).

Run `tokenusage refresh` before a report when the app has not updated the local store. Refresh writes normalized numeric records from installed providers.

The JSON contracts use versioned names such as `tokenusage.report.v1`, `tokenusage.limits.v1`, and `tokenusage.doctor.v1`.

## Documentation

- [Documentation index](docs/README.md)
- [Provider matrix](docs/PROVIDER-MATRIX.md)
- [Contributor testing guide](docs/CONTRIBUTOR-TESTING.md)
- [Release procedure](docs/RELEASING.md)
- [Maintenance dependency notes](docs/DEPENDENCY_UPDATES.md)

### Repository map

| Path | Responsibility |
|---|---|
| `src/TokenUsage.App` | WinUI views, view models, and application composition |
| `src/TokenUsage.Core` | Portable domain, storage, cache, and coordination contracts |
| `src/TokenUsage.Providers` | Provider adapters and pricing support |
| `src/TokenUsage.Presentation` | Shared dashboard and report presentation |
| `src/TokenUsage.Platform.Windows` | Windows integration |
| `src/TokenUsage.Runtime.Windows` | Shared Windows runtime composition |
| `src/TokenUsage.Cli` | Commands and stable JSON output |
| `src/TokenUsage.Package` | MSIX manifest and packaged payloads |
| `tests` | Architecture, core, provider, platform, app, and CLI tests |

## Contributing

Contributions and pull requests are welcome.

**Open an issue before writing the change.** Every pull request must link its issue and stay within the agreed scope.

Provider contributions need reproducible evidence. This rule is especially important when maintainers cannot access the provider. A mock fixture alone does not prove a real integration.

Read [CONTRIBUTING.md](CONTRIBUTING.md) and the [contributor testing guide](docs/CONTRIBUTOR-TESTING.md) before starting.

## Acknowledgements

The provider catalog and documentation structure take inspiration from [OpenUsage](https://github.com/janekbaraniewski/openusage), [CodexBar](https://github.com/steipete/CodexBar), and [CodeBurn](https://github.com/getagentseal/codeburn). TokenUsage adapts those ideas to a native Windows app with its own privacy and evidence rules.

These projects do not endorse TokenUsage. Provider names and marks belong to their owners.

## License

TokenUsage is available under the [MIT License](LICENSE). Third-party material and terms appear in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

<h4 align="right">Support continued development</h4>
<p align="right">
  <a href="https://github.com/sponsors/gvastethecreator/"><img src="https://shieldcn.dev/badge/%E2%9D%A4-sponsor%20this%20project-red.svg?animate=pulse" alt="Sponsor this project" /></a>
  <a href="https://ko-fi.com/gvaste"><img src="https://shieldcn.dev/badge/Ko--fi-support%20development-ff5e5b.svg?logo=kofi&variant=branded" alt="Support development on Ko-fi" /></a>
  <a href="https://x.com/gvastebb"><img src="https://shieldcn.dev/x/mention/gvastebb.svg?variant=branded" alt="Follow on X" /></a>
</p>
