# Product specification

Status: approved baseline for implementation

Formal name: TokenUsage
Technical name: TokenUsage
Platform: Windows 10 version 1903 or later, x64 and ARM64

The technical name cutover finished on 2026-08-04. The product, projects, namespaces, assemblies, executable, and CLI use `TokenUsage`. Package Identity and AUMID stay stable so upgrades keep working. ADR-0002 records this decision.

## Goal

Give one person a fast, reliable view of remaining quota, the next reset, tokens, and recent cost for their AI tools. The app uses already-open sessions and local data when a safe contract exists. It does not require a TokenUsage account.

## Users

- A person who uses Codex, Claude Code, or other AI tools every day.
- A support team that needs to know which client ran out of quota and when it resets.
- Local automation that consumes a read-only JSON contract.

## Primary result

From the tray icon, the user opens a panel and sees in less than two seconds:

- which providers have data
- how much was used or remains
- when each window resets
- whether the current pace will exhaust quota before reset
- how much local usage and cost was observed when quota is unavailable
- when the value was taken and which source produced it

## Product rules

1. The app does not create an account or offer a provider login.
2. Local detection does not use the network.
3. Each remote call requires an existing session or a key the user added explicitly.
4. The app does not store copies of credentials that belong to another tool.
5. An estimated, local, partial, or stale value carries a visible label.
6. A failure keeps the last valid value and shows its age.
7. A missing value appears as `No data`. The app does not invent zero.
8. A provider is published only after technical tests, security review, and permitted-use review close.
9. Quota, observed usage, and cost are independent capabilities. A card can have one, two, or three of them.
10. Local readers do not open authentication files or index prompts, responses, tools, or commands.

## Surfaces

### Tray

The icon summarizes the worst state of the chosen metrics:

| State | Treatment |
|---|---|
| Normal | Base icon |
| Near the limit | Amber mark |
| Exhausted or an error that needs action | Red mark |
| Refreshing | Brief, accessible indicator |
| No data | Neutral icon |

Hovering the pointer over the icon shows a floating strip of the chosen providers. The strip shows only providers detected on this computer. When none are present, it shows a short message instead of empty blocks.

Each block has room for two values. In Appearance, the user chooses which value occupies each line, how many providers fit, and whether the provider name appears:

| Setting | Options | Initial value |
|---|---|---|
| Primary value | session limit, period limit, 30-day cost, 30-day tokens | session limit |
| Secondary value | none, or any value other than the primary | period limit |
| Providers | one to four | four |
| Provider name | show or hide | hide |

The secondary value cannot repeat the primary. Strip width and height follow the chosen content. States use green, yellow, orange, and red. A value the source does not offer appears as `—`. The app never invents it. The strip uses the active theme, sits next to the icon on its monitor, respects DPI, and hides when the pointer leaves the icon. The native Windows tooltip is suppressed so it does not overlap the strip.

The primary click closes the strip and opens or closes the compact panel. The context menu offers **Update**, **Options**, and **Exit**. Mouse and keyboard must both work.

### Main panel

A frameless, non-resizable window aligned with the tray icon and clipped to the visible monitor area. Base width: 320 DIPs. Height follows content, with a minimum of 200 DIPs and a maximum of 720 DIPs or 85% of the work area, whichever is smaller. Scaling to physical pixels uses the monitor DPI.

Outer margin is 14 DIPs. Section gap is 14 DIPs at normal density. Cards use a 12 DIP radius and no dominant border. Bars are 5 DIPs high. Text communicates state in addition to color. Provider headers sit outside the card. One grouped card per provider. No nested cards. No visible title bar or in-dashboard application header. Typography uses `Segoe UI` and Fluent resources. Icons use Segoe Fluent Icons or first-party assets.

The screenshot at `docs/design/selected-flyout-option-1.png` guides hierarchy and density. It is not a literal specification when it contradicts upstream code or a native Windows convention. Implementation does not keep three mock defects: a provider header that overflows the outer container, empty vertical space that the window should collapse, and non-Fluent share, refresh, and options controls.

The layout follows this order:

1. the **Total spend** card when a suitable source exists
2. provider cards
3. a fixed footer with identity, age or refresh, and access to options

The window hides when it loses focus. **Options** and customization open inside the same panel. `Esc` goes back one screen. A second `Esc` closes the panel.

The visual target comes from `robinebers/openusage@9d2bf09f10e21f769494a525a9d65c84d7aeb1df`. Light, dark, high contrast, keyboard access, focus, and 200% text are part of the visual gate.

### Provider card

Each card shows:

- icon, own name, plan, and status
- always-visible metrics
- a collapsible block for secondary metrics
- source and time of the value in a tooltip or detail
- a short notice for credential, network, throttle, stale data, or partial coverage
- an action to refresh only that provider

The detail header lists the available capabilities:

- `Quota`: limit, remaining, and reset reported by a suitable source
- `Local usage`: activity observed only on this computer
- `Cost`: reported or estimated cost, with its label

Row types:

- a bounded bar with used/remaining and reset
- a simple value for balance, cost, or tokens
- a badge for plan or status
- a 30-day trend
- short diagnostic text

A click on **Used** or **Remaining** changes the mode across the app. A click on the time toggles countdown and exact date.

### Usage and cost

This section appears when at least one provider offers tokens or cost with known coverage.

- quick ranges: today, yesterday, 7 days, 30 days, and current month
- metrics: cost, cost per million tokens, and tokens
- a ring by agent, a total, and a legend
- breakdown by agent and model
- provider-reported cost kept separate from estimated cost
- compare two providers, the current range with the previous range, or two Codex cycles
- unpriced models and covered percentage
- source and estimate detail
- an empty state when the range has no data

Estimated cost at API rates is not presented as a subscription invoice.

The first version does not group by project, session, task, or command. Those views would require storing more metadata and stay outside the small engine.

### Customization

- turn providers on or off
- order providers
- order metrics
- move metrics between always visible and on demand
- hide a metric
- choose up to four providers for the tray strip
- undo changes during the session
- reset one provider or everything, with confirmation for a full reset

### Options

| Group | MVP options |
|---|---|
| General | start with Windows, global shortcut, manual refresh |
| Appearance | system/light/dark, density, transparency, used/remaining, relative/exact time, tray strip content |
| Providers | detection, activation, status, and source |
| Alerts | thresholds, pace, stale data, and credential failure |
| Network | system proxy and connection test |
| Privacy | local API, Origin access, telemetry, export or delete data |
| Diagnostics | version, logs, cache, copy a report with no secrets |
| Update | channel, version, and check for update |

Telemetry stays off at install. The user must confirm any future metrics option.

### CLI

A dedicated executable, `tokenusage.exe`:

```text
tokenusage limits
tokenusage limits codex
tokenusage limits --force --format json
tokenusage refresh
tokenusage usage --days 30 --format json
tokenusage report --days 30
tokenusage report --from 2026-07-01 --to 2026-07-31 --agent codex --format json
tokenusage providers
tokenusage doctor
```

`report` returns totals, token breakdown, agents, models, highest-cost days, a daily series, and price coverage. It keeps provider-reported costs separate from catalog estimates. It does not aggregate projects, sessions, tasks, prompts, or tools.

The CLI shares providers, cache, and models with the app. It can read data without the panel open. Exit codes:

- `0`: a valid response, including stale data that is marked
- `2`: invalid usage or arguments
- `4`: no useful data was obtained

### Local API

Off at install. When enabled, it exposes:

- `GET /v1/limits`
- `GET /v1/limits/{providerId}`
- `GET /v1/usage?days=30`
- `GET /v1/usage/{providerId}?days=30`
- `GET /v1/health`

It requires `Authorization: Bearer <token>`. The token is created when the feature is enabled, stored in Credential Locker, and can be rotated. It is not shown on screen except through a confirmed action.

## Visible state model

Each provider is in one of these states:

| State | UI | Action |
|---|---|---|
| Detecting | Brief skeleton | Wait |
| Available | Data and time | None |
| Refreshing with cache | Previous data plus progress | Wait or cancel the batch |
| No credential | Explanation and a path to open the tool | Sign in with the original tool |
| Unsuitable account type | Explanation | See detail |
| No data | Rows with `No data` | Refresh or open help |
| Partial | Data plus a coverage notice | See source |
| Stale | Last value plus age | Refresh |
| Throttle | Last value plus next attempt | Wait |
| No network | Last value plus network status | Retry |
| Format error | Last value plus a report | Copy diagnostics |
| Policy blocked | Informational card with no access | See provider status |

## Usage pace

For a bounded metric:

- `usedFraction = used / limit`
- `timeFraction = elapsedTime / windowDuration`
- evaluation starts after a minimum sample and a minimum elapsed time
- blue: usage with margin
- amber: usage near the exhaustion pace
- red: projected exhaustion before reset

The calculation, thresholds, and clock are tested with an injectable time source. The UI avoids a prediction when duration, start, or limit is missing.

## Refresh and cache

- local detection in parallel at first start
- last valid snapshot loaded before the first network call
- remote refresh at the start of each app session
- base cadence of five minutes
- a manual refresh that ignores TTL
- timeout and cancellation per provider
- backoff with jitter for throttle and transient failures
- a slow provider does not block publication of the others
- atomic snapshot write after validation
- data older than ten minutes marked stale by default

The interval can change after load and contracts are measured. It is never shortened below the provider policy.

## Initial detection

The first run inspects locally, without reading secret content:

- known executables on `PATH` and install paths
- known data folders
- environment variables that change the path
- presence of credential files or databases
- the user's own already-stored manual keys

It activates only providers with a suitable path. If none appear, it shows Codex and Claude as guided options without claiming they are connected.

Presence probing does not read usage files and does not need a local database, so it returns before the first scan. These rules follow from that:

- the panel and tray strip list only providers whose root was found
- a tool without a root stays out of the list even if the store still has its history. That history still counts in usage and cost totals
- a tool with a root and no history appears with `No data`, not as absent
- when probing finds no tool, the panel uses its own message and does not treat it as a provider failure

## Codex MVP

Requirements:

- locate `codex` safely
- start `codex app-server --stdio` with no console window
- complete the handshake
- read limits and usage with stable methods
- do not invoke login, logout, reset consumption, or a model request
- do not read or copy the token
- stop the child process on exit
- tolerate CLI updates and new fields
- explain API-key-only or an account without ChatGPT limits
- use local logs only for detail that the official method does not provide

### Codex reset history

- store each numeric observation of the official windows without credentials or content
- record a scheduled reset when the reported window advances after it expires
- record an early reset when official usage drops materially before the reported date, even if OpenAI does not change that date
- ignore minor rounding variation and old or repeated observations
- do not fabricate resets before the first observation
- let the Codex report use the current cycle or an earlier observed cycle as a range
- compare two observed cycles in the report
- state that durable usage is aggregated by day and that the reset day cannot be split by hour

## Initial Claude

Allowed scope in the first version:

- detect `CLAUDE_CONFIG_DIR` and the default directory
- read session logs for tokens and trend
- calculate estimated cost with a versioned catalog
- mark unsaved sessions as out of coverage
- do not read or use subscription OAuth for a distributed remote call

Live quota is enabled after the gate defined in the provider matrix.

## Accessibility

- complete keyboard navigation
- stable focus order
- names and states for a screen reader
- high contrast that does not depend on color alone
- a 44 DIP minimum on primary actions
- text at 200% without clipping critical values
- reduced motion according to the system
- a tooltip reachable by focus, not hover only

## Performance

- panel visible from cache in less than 500 ms on reference hardware
- refresh does not block the UI thread
- idle usage under 150 MB as an initial target
- idle CPU near zero
- no continuous file polling. Use a batch or a watcher with debounce
- start the Codex process on demand and reuse it while the app runs

These figures become gates after the first vertical slice is measured.

## Privacy and data

Persisted:

- settings and order
- snapshots without credentials
- numeric observations and quota reset boundaries
- scanner indexes and daily aggregates
- normalized events without content during the retention period
- catalog version and rates used for each estimate
- the local API token in Credential Locker
- rotated logs without quota values by default

Not persisted:

- tokens from Codex, Claude, or another tool
- prompt or response content
- tool, command, task, or file names
- project name and working path in the first version
- account email unless a future feature requires it and the user accepts it
- project paths in ordinary reports

## Out of MVP

- own login
- cloud sync across computers
- consumption of Codex reset credits
- support for private endpoints without a gate
- a web panel
- an always-visible desktop widget
- unpackaged installation
- automatic import of secrets from browsers
- an index of transcripts, tasks, tools, or commands
- Grok or Antigravity quota through private endpoints, tokens, or TUI

## MVP success criteria

- clean MSIX install and uninstall
- a reliable tray icon after Explorer restart
- a correct flyout across multiple displays and DPI
- Codex shows quota, reset, and usage with an existing session
- zero login or token copy
- cache and failure states verified
- light, dark, and high contrast themes
- keyboard and screen reader cover the main flow
- unit, contract, integration, and UI suites green
- a signed x64 package in beta and an ARM64 test before stable
- MIT license and third-party notices included
- final name and identity approved

The cost beta adds Claude, Grok Build, and OpenCode with fixtures, differential totals, and visible coverage. Antigravity CLI first needs a real local database, sanitized fixtures, and a parser that does not use its login.
