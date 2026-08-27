# Kilo Code source gate

Cutoff date: 2026-07-22

Decision: `gate`

## Question

Can TokenUsage integrate Kilo Code on Windows to show quota, tokens, or
spend without reusing login, credentials, session content, or interface
automation?

## Answer

Kilo Code offers a candidate surface: the official CLI `kilo stats` shows
tokens and cost by sessions, models, tools, and project. The official
reference does not publish JSON for that command, a versioned schema, or a
read-only guarantee. For that reason there is still no approved adapter.

The extension keeps local state in `kilo.db`, including sessions and
history. That store stays out of scope even if it contains counters.
TokenUsage cannot open it, copy it, infer its tables, or walk the sessions
to recompute spend.

## Identity and support

- Proposed ID for a future descriptor: `kilo-code`.
- Visible name: `Kilo Code`.
- Clients covered by this gate: CLI `kilo` and Kilo Code extensions.
- Observed client: `kilo 7.4.15`, run from the official package
  `@kilocode/cli` in an isolated profile.
- Windows: the documentation publishes a Windows x64 binary and the CLI
  through npm.

## Primary sources

Consulted on 2026-07-22:

| Source | Supporting fact |
|---|---|
| [Kilo Code CLI](https://kilo.ai/docs/code-with-ai/platforms/cli) | Defines the CLI, Windows, sessions, and `kilo stats`. |
| [CLI reference](https://kilo.ai/docs/code-with-ai/platforms/cli-reference) | Defines the `kilo stats` filters; it does not document JSON output for that command. |
| [Extension troubleshooting](https://kilo.ai/docs/getting-started/troubleshooting/troubleshooting-extension) | Identifies `kilo.db` as local state with sessions and history. |
| [Official repository](https://github.com/Kilo-Org/kilocode) | Confirms the project, the CLI package, and Windows distribution. |

## Isolated Windows test

`.snapshots/kilo-t56-smoke` was created, with `HOME`, `USERPROFILE`,
`APPDATA`, `LOCALAPPDATA`, `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `KILO_DIR`,
and npm cache isolated. `npx --yes --package @kilocode/cli kilo --version`
returned `7.4.15`.

`kilo stats --help` showed `--days`, `--tools`, `--models`, and
`--project` filters, with no JSON format. `kilo stats` returned a table of
zero sessions, zero messages, zero tokens, and `$0.00` cost, without login,
API key, or data from the real profile. The test does not certify that the
command does not write state, that the format is stable, or that it covers
an account with real usage.

## Source classification

| Data | Observed source | TokenUsage status | Decision |
|---|---|---|---|
| Remaining quota | Kilo profile, gateway, and teams | No third-party read-only quota endpoint or contract | Blocked |
| Aggregated tokens and cost | `kilo stats` | Human output without schema or read-only guarantee | Gate |
| Sessions and local detail | `kilo.db` | Includes sessions and history | Blocked |
| Session export | `kilo export` | Can include session data; it is not a minimum metrics export | Blocked |
| API keys and auth | `kilo auth`, gateway, and configuration | Usage credentials, with no documented monitor scope | Not allowed |

## Privacy and security limit

TokenUsage can detect `kilo --version` in a future diagnostic. It must not:

- open `kilo.db`, WAL, SHM, configuration, auth, caches, sessions, or history;
- use `kilo export`, `/copy-session`, `/export`, the TUI, Console, web,
  daemon, gateway, profile, teams, or interface automation;
- take API keys, tokens, cookies, environment variables, or Credential
  Manager from Kilo Code;
- infer private paths, tables, or endpoints from local files or traffic.

## Product decision

- Do not create a local scanner, table parser, or public Kilo Code card now.
- Do not ask for or store Kilo Code credentials.
- Keep Ticket 57 in `needs-info`.
- Reopen the adapter only if Kilo publishes a structured aggregated output,
  or confirms in writing a read-only `kilo stats` invocation with a stable
  contract suitable for third parties.
- The later test needs an authorized trial account, process limits, output
  capture without sensitive data, sanitized fixtures, and tests for
  absence, error, new format, and unknown cost.

## Independent review

Grok Build reviewed matrix, plan, tickets, and gates with `Read` and `Grep`
only. It detected that the matrix could present `kilo stats` as the chosen
source. The row now states that no suitable source exists and that the
command is only a candidate. The review does not certify external sources
or live CLI behavior.

## Remaining uncertainty

Kilo can add JSON or change the statistics surface. Before announcing
support, repeat the gate with the current Windows version and a verifiable
read contract.
