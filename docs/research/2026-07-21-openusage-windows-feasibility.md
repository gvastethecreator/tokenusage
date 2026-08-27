# Feasibility of a Windows OpenUsage implementation

Date: 2026-07-21

Status: research closed; design can start

Pinned upstream: `robinebers/openusage@9d2bf09f10e21f769494a525a9d65c84d7aeb1df`

## Question

Can we create a native Windows app with OpenUsage's interface and core functions that shows remaining usage from sessions already open on the machine, without its own account or a new login?

## Answer

Yes for the product and for Codex. The UI, refresh cycle, cache, local usage, CLI, and a local API have solid Windows equivalents.

The Codex path was proven with the official `codex app-server` interface. Claude can compute usage from local logs, but its live quota lacks a public read-only interface. Grok Build and OpenCode have local paths suitable for tokens and spend. Antigravity needs a passive parser and keeps its quota blocked by policy. Detail is in the [agent and spend research](2026-07-21-agent-costs-and-quotas.md).

The Windows tray does not support a persistent strip of text and bars next to the icon. The design will use a status icon, a short tooltip, and a native flyout on click.

## Source studied

OpenUsage was cloned into `.reference/openusage` and the analysis is pinned to the stated SHA. That revision has 237 Swift files and 138 test files. The root repo ignores this clone.

The [MIT license](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/LICENSE) allows copying and changing the code if the notice is kept. The [trademark policy](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/TRADEMARK.md) reserves the name, logo, and visual identity. The product needs its own name, icon, and legal text.

## What OpenUsage does

The [pinned README](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/README.md) and its documents describe:

- a per-provider panel with used or remaining percentages and a countdown;
- forecasted pace against the period's time;
- spend and tokens for today, yesterday, and 30 days;
- a trend chart and per-model detail;
- local provider detection, order, visibility, and up to two featured metrics;
- a persistent five-minute cache and parallel refresh;
- last valid result during failures, with a stale-data notice;
- global shortcut, start with the system, proxy, theme, density, notices, and updates;
- one-shot CLI and local HTTP server.

Detail is in [dashboard](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/dashboard.md), [refresh](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/refreshing.md), [settings](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/settings.md), [CLI](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/cli.md), and [local API](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/local-http-api.md).

## Upstream architecture pattern

The [architecture document](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/architecture.md) uses a composition root, a runtime per provider, and a common model consumed by the app, CLI, and HTTP.

Each provider follows this sequence:

1. Detect whether a local credential exists without using the network.
2. Read an authentication source or a key that belongs to the app.
3. Query limits or usage with a client of its own for that provider.
4. Map the response to a common snapshot.
5. Store only the snapshot and refresh state.

The models pinned in [ProviderSnapshot](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Models/ProviderSnapshot.swift) and [MetricLine](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Models/MetricLine.swift) cover progress, values, badges, charts, and text. This pattern works for Windows and keeps the UI from knowing provider files, tokens, or JSON.

## Windows parity

| Capability | Windows path | Decision |
|---|---|---|
| Flyout | WinUI `Window` + `AppWindow`, HWND, and position next to the work area | A frameless, fixed-size window that hides when it loses focus |
| Tray | Win32 `Shell_NotifyIconW` | Internal interop; icon, tooltip, click, and accessible menu |
| Text next to the icon | The tray API does not offer that surface | State by icon; summary in tooltip and flyout |
| Single instance | Windows App SDK AppLifecycle | Redirect activations to the main instance |
| Notices | `AppNotificationManager` | Alerts for quota, pace, credential, and stale data |
| Startup | `StartupTask` with package identity | Explicit user setting |
| Own secrets | Windows Credential Locker | Only keys the user supplies to this app |
| Distribution | Full-trust MSIX | `x64` first, `ARM64` before stable |
| Update | Store or signed App Installer | Separate stable and beta channels |

Microsoft sources: [packaging](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/), [MSIX container and trust](https://learn.microsoft.com/en-us/windows/msix/msix-containerization-overview), [windows with AppWindow](https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows), [getting HWND](https://learn.microsoft.com/en-us/windows/apps/develop/ui/retrieve-hwnd), [Shell_NotifyIconW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw), [notifications](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/), [single instance](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-instancing), and [Credential Locker](https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker).

## Proven Codex path

[`codex app-server`](https://github.com/openai/codex/blob/a26f219f6788c951dcb3bf435fab4c6d0f4d2f40/codex-rs/app-server/README.md) is the interface Codex uses for clients such as its VS Code extension. Its default stable transport is JSONL over `stdio`.

The protocol requires:

1. start `codex app-server --stdio`;
2. send `initialize` with the client's name, title, and version;
3. send the `initialized` notification;
4. request `account/rateLimits/read` for quota and resets;
5. request `account/usage/read` for summary and daily buckets.

Codex keeps the login, refresh token, and remote call. The app processes typed fields and tolerates new fields. The documentation marks those two methods as part of the stable account surface.

### Local test

A schema-only smoke test ran against the installed `codex` and the existing session. The test did not print tokens, email, or usage figures.

Results:

- `initialize`: correct;
- `account/rateLimits/read`: correct;
- groups seen: `rateLimits`, `rateLimitsByLimitId`, `rateLimitResetCredits`;
- `account/usage/read`: correct;
- groups seen: `summary`, `dailyUsageBuckets`.

The implementation must keep a supervised process, put a timeout on each request, validate the response ID, and restart the process after close, binary change, or protocol error. The client must declare its own `clientInfo.name`. For enterprise deployments, Codex documentation asks to contact OpenAI to register the client in their compliance logs.

### Local source for Codex spend

OpenUsage also reads `sessions` and `archived_sessions` under `CODEX_HOME` to measure tokens and estimate spend. Its [Codex document](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/codex.md) explains time zones, subagent deduplication, and prices.

The official `account/usage/read` method reduces the initial work. The logs remain useful for per-model detail and for comparing results. The MVP should use the official method for totals and add the local scanner when a differential test proves its value.

## Claude path and launch limit

The [Claude Code authentication documentation](https://code.claude.com/docs/en/authentication) states that Windows stores credentials in `%USERPROFILE%\.claude\.credentials.json`, or under `CLAUDE_CONFIG_DIR`. It also defines the order of authentication sources.

OpenUsage reads that credential, queries a quota endpoint, and rotates tokens when needed. It also computes spend from `projects`; its [Claude document](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/claude.md) describes both sources.

Claude Code does not document a read-only quota command. Writing a rotated credential from two processes can invalidate the session. The public changelog also records race fixes between processes.

The [Claude Code legal guide](https://code.claude.com/docs/en/legal-and-compliance) reserves subscription OAuth for native Anthropic applications and directs third parties to API keys or cloud providers. Querying quota has less scope than sending a model request, but it still uses a subscription credential outside the official client. Before distributing that function, one of these contracts is required:

- a public read-only quota interface;
- an official Claude Code command that returns quota;
- written permission from Anthropic for this case.

Meanwhile, the app can detect Claude logs and show measured local usage or estimated cost with a clear label. That view cannot claim how much of the plan remains.

## Local API and privacy

OpenUsage listens on `127.0.0.1:6736` and publishes CORS `*`. Its [privacy note](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/local-http-api.md#cors-and-privacy) warns that any open page can read the snapshots while the app runs.

The Windows version will ship with the API off. When it is turned on:

- it will listen only on `127.0.0.1`;
- it will require a random token stored in Credential Locker;
- it will reject requests with `Origin` except an explicit list;
- it will have a concurrency limit, timeout, and maximum body;
- it will exclude paths, tokens, email, and account data that are not required;
- it will record access without including quota values.

An OpenUsage compatibility mode can be added as an option with a privacy notice.

## Providers

Upstream announces Antigravity, Claude, Codex, Copilot, Cursor, Devin, Grok, OpenCode, OpenRouter, and Z.ai. Research of their adapters shows five classes:

- official local interface: Codex;
- local logs or databases: Claude, Codex, Grok, OpenCode, and, with experimental coverage, Antigravity;
- private endpoint with a reused credential: Claude, Cursor, Copilot, Antigravity, Devin, and Grok;
- approved manual key: OpenRouter, Cursor Admin API, GitHub billing API, and Devin v3 for one organization's consumption;
- blocked manual key: Z.ai, per its [quota gate](2026-07-21-zai-gate.md).

Each private provider needs a technical test, policy review, and sanitized fixtures. The [provider matrix](../PROVIDER-MATRIX.md) pins the order.

## Risks

| Risk | Effect | Control |
|---|---|---|
| Private endpoint changes | Card without data | Isolated adapter, versioned contract, last valid value, and remote flag |
| Two processes rotate a token | Session close | Prefer the official interface; do not write someone else's credentials in the MVP |
| Local source changes schema | Incomplete spend | Tolerant parser, fixtures per version, and coverage notice |
| MSIX changes paths or permissions | Failed detection | Tests inside the signed package, without depending on the working directory |
| Explorer restarts | Missing icon | Re-register the icon after `TaskbarCreated` |
| Several screens or scales | Flyout off screen | Position by monitor, DPI, and work area |
| Loopback reachable from the web | Quota leak | API off, token, Origin policy, and minimum data |
| Upstream brand | Confusion or claim | Own name, logo, package, and notices |
| Provider policy | Function not distributable | Launch gate per provider |

## Uncertainty

- The Codex protocol is stable today, but the client must check the version and tolerate extra fields.
- The exact look of the flyout must be validated on Windows 10 and 11, with dark theme, high contrast, and 100–200% scales.
- No credential content was read during the research.
- Private providers stay outside any version promise until their test and allowed-use review are closed.

## Decision

Start a Windows MVP with a WinUI shell, Win32 tray, common cache, and a Codex integration through `app-server`. Build a small local token and spend engine for Claude, Grok Build, and OpenCode. Evaluate Antigravity only through a passive local database. Keep Claude, Grok, Antigravity quota and the other private endpoints behind provider gates. Package as MSIX, keep telemetry and the local API off at install, and use our own name and identity.
