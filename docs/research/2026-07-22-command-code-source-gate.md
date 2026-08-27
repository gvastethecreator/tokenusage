# Command Code source gate

Cutoff date: 2026-07-22

Decision: `block`

## Question

Can TokenUsage integrate Command Code on Windows to show quota, tokens, or
spend without reusing login, credentials, session content, or interface
automation?

## Answer

Not yet. Command Code shows credit limits and quota in `/usage` inside the
interactive session. Its Studio shows usage, tokens, cost, and per-request
history after authentication. The reviewed sources do not define a read API
for Command Code quota, balance, spend, or history, or a metric export
suitable for third parties.

The `--output-format` JSON belongs to the `cmd -p` response; it does not
cover `/usage`. The Provider API offers inference and model listing with the
same CLI API key. It does not publish a balance, quota, or history endpoint,
and it does not turn that key into a read-only monitor credential.

Local files mix conversations, credentials, and preferences. The public
build cannot invoke `/usage`, automate Studio, use an API key, or read
`.commandcode` or `~/.commandcode`.

## Identity and support

- Proposed ID for a future descriptor: `command-code`.
- Visible name: `Command Code`.
- Publisher and service owner: `Langbase, Inc. d/b/a Command Code`.
- Main client: CLI `command-code`, distributed by npm as
  `command-code`; on native Windows it is invoked as `cmdc` because `cmd`
  is a reserved system command.
- Observed version: `1.0.1`.
- Windows: native alpha support in PowerShell, Windows Terminal, and Git
  Bash; WSL is the path the publisher recommends.
- Editor: the documentation presents IDE integration, without a usage or
  spend contract independent of Studio and the CLI.

## Primary sources

Consulted on 2026-07-22:

| Source | Supporting fact |
|---|---|
| [Main documentation](https://commandcode.ai/docs) | Identifies Command Code and its official CLI. |
| [CLI Reference](https://commandcode.ai/docs/reference/cli) | Pins `cmd`, sessions, `--output-format` for `-p`, subcommands, and interactive commands. |
| [Windows Guide](https://commandcode.ai/docs/troubleshooting/windows) | Pins `cmdc` for native Windows, which is still alpha, and recommends WSL. |
| [Usage Limits](https://commandcode.ai/docs/resources/usage-limits) | Defines `/usage`, balance, and 5-hour and weekly limits inside the CLI. |
| [Pricing & Limits](https://commandcode.ai/docs/resources/pricing-limits) | Places request history and costs in Studio. |
| [Studio](https://commandcode.ai/docs/studio) | Confirms per-request data and that API keys are obtained after login. |
| [Provider API](https://commandcode.ai/docs/provider) | Lists only inference and models; the same key authenticates CLI and API. |
| [Security & Privacy](https://commandcode.ai/docs/resources/security) | Documents `auth.json`, local conversations, and `.commandcode/taste/`. |
| [Privacy Policy](https://commandcode.ai/privacy) | Identifies Langbase, Inc. and classifies prompts, outputs, metadata, and Taste. |
| [Terms of Service](https://commandcode.ai/terms) | Confirms the service, its account, and credential obligations. |
| [Official repository](https://github.com/CommandCodeAI/command-code) | Confirms the public project, npm, and the CLI flow. |

## Isolated Windows test

The machine had no global `cmdc`. `command-code@latest` was installed
without lifecycle scripts inside `.snapshots/command-code-t52-smoke`. The
resolved package was `1.0.1`.

The test used a temporary profile under that same snapshot, without login,
API key, or user data. `cmdc --version --no-auto-update` returned `1.0.1`.
`cmdc --help --no-auto-update` confirmed `cmdc`, `/usage`, `/session-file`,
`.jsonl` sessions, and `--output-format json` only for `-p`; it did not show
a subcommand for quota, usage, balance, billing, or metric export. Even help
created a `.commandcode` profile inside the temporary profile, so future
tests must keep the isolation.

## Source classification

| Data | Observed source | TokenUsage status | Decision |
|---|---|---|---|
| Remaining quota | Interactive `/usage` | No machine contract | Blocked |
| Credits and limits | `/usage` | Interactive session only; no metrics JSON | Blocked |
| Tokens and spend | Studio Usage | Authenticated interface; no public API or export | Blocked |
| Provider API | Per-request inference responses | Does not expose account state or history; requires a usage key | Out of this provider |
| Local data | `projects`, `.jsonl` sessions, `auth.json`, and Taste | Mix prompts, responses, credentials, and rules | Blocked |

A future Provider API integration, if authorized, would be a different
product from Command Code: it would measure only requests that TokenUsage
instruments, not the agent's existing consumption or its subscription
limits.

## Privacy and security limit

TokenUsage can detect the presence and version of `cmdc` with
`cmdc --version` in a future diagnostic. It must not open, copy, or use:

- `~/.commandcode/projects/`, `.jsonl` sessions, checkpoints, exports,
  conversations, prompts, responses, attachments, files, code, or commands;
- `~/.commandcode/auth.json`, `COMMAND_CODE_API_KEY`, cookies, OAuth,
  Studio profiles, MCP tokens, or login state;
- `.commandcode/taste/`, `AGENTS.md`, skills, mods, MCP, memory, plans, or
  project rules;
- `/usage`, `/session-file`, `/export`, the TUI, Studio, observed traffic,
  or private endpoints;
- Provider API, because the key has no read-only monitor scope and its data
  does not cover existing Command Code usage.

## Product decision

- Do not create a local reader, remote client, or Command Code provider
  card in this phase.
- Do not ask for or store Command Code or Provider API credentials for this
  provider.
- Do not automate `/usage`, the CLI, Studio, or the Provider API.
- Version detection can remain a future diagnostic without a quota, token,
  or spend promise; it must respect the alpha state of native Windows.
- A generic form of values the user types explicitly would need its own
  ticket; it does not form a Command Code adapter and is not in Ticket 53.
- Keep Ticket 53 in `needs-info`.
- Reopen only with a read-only API or export, documented for third parties,
  that delivers minimum metrics without sessions or credentials and that
  authorizes automatic queries.

## Independent review

Grok Build reviewed this decision with `Read` and `Grep` only and issued
`accept`. It did not use web, shell, or write. The local review confirmed
that the gate, matrix, and plan match. This review does not replace the
sources in the table above.

## Remaining uncertainty

Command Code can publish an account API, a metric export, or a structured
`/usage` subcommand. Before announcing support, repeat the gate and test
the current Windows version with an authorized test account.
