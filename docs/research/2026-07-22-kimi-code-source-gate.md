# Kimi Code source gate

Cutoff date: 2026-07-22

Decision: `block`

## Question

Can TokenUsage integrate Kimi Code on Windows to show quota, tokens, or
spend without reusing login, credentials, session content, or interface
automation?

## Answer

Not yet. Kimi Code offers the `kimi` CLI and a VS Code extension. The
interactive `/usage` command shows tokens, context, and quota, and the web
console shows quota and Extra Usage. The reviewed sources do not define a
machine output, metric export, or read-only API for those data.

Local files contain credentials, history, and the agent's full
communication. The subscription is limited to interactive use, and its
terms forbid automation that simulates human use without written
authorization. TokenUsage cannot invoke the TUI, use `kimi web`, copy its
token, or read `.kimi-code`.

## Identity and support

- Proposed ID for a future descriptor: `kimi-code`.
- Visible name: `Kimi Code`.
- Publisher and service owner: `Moonshot AI PTE. LTD.`.
- Main client: Kimi Code CLI, executable `kimi`, distributed as a binary
  and package `@moonshot-ai/kimi-code`.
- Observed version: `0.29.0`, published on 2026-07-22.
- Windows: the official installer supports PowerShell; it requires Git for
  Windows for its Git Bash environment. The TypeScript CLI is the current
  client; the Python variant remains legacy.
- Editor: a VS Code extension exists, although new installs are limited to
  users of the legacy Python CLI.

## Primary sources

Consulted on 2026-07-22:

| Source | Supporting fact |
|---|---|
| [Kimi Code overview](https://www.kimi.com/code/docs/en/) | Defines Kimi Code, its CLI/VS Code products, and the separation from Kimi Platform. |
| [Kimi Code CLI on GitHub](https://github.com/MoonshotAI/kimi-code) | Official repository, distribution, PowerShell support, and release `0.29.0`. |
| [CLI changelog](https://www.kimi.com/code/docs/en/kimi-code-cli/release-notes/changelog.html) | Publishes `0.29.0` on 2026-07-22. |
| [Data locations](https://www.kimi.com/code/docs/en/kimi-code-cli/configuration/data-locations.html) | Pins the Windows root and the contents of credentials, sessions, logs, and history. |
| [Sessions and context](https://www.kimi.com/code/docs/en/kimi-code-cli/guides/sessions.html) | Describes `wire.jsonl`, `state.json`, exports, and their sensitive content. |
| [`kimi` command](https://www.kimi.com/code/docs/en/kimi-code-cli/reference/kimi-command.html) | Documents CLI, exports, and local `kimi web` with a bearer token. |
| [Slash commands](https://www.kimi.com/code/docs/en/kimi-code-cli/reference/slash-commands.html) | Defines `/usage` as a TUI command, not as a JSON subcommand. |
| [Membership Benefits](https://www.kimi.com/code/docs/en/kimi-code/membership.html) | Explains weekly quota, 5-hour window, Extra Usage, balance, and console. |
| [Community Guidelines](https://www.kimi.com/code/docs/en/kimi-code/community-guidelines.html) | Limits the subscription to interactive use and forbids non-interactive automation. |
| [Terms](https://www.kimi.com/user/agreement/modelUse?version=v2) | Identifies Moonshot AI PTE. LTD. and forbids automation that simulates human use without written authorization. |
| [Kimi API: balance and usage](https://www.kimi.com/help/kimi-api/api-balance-and-usage) | Documents Kimi Platform balance and costs, a product separate from Kimi Code. |

## Isolated Windows test

The machine had neither `kimi` nor `%USERPROFILE%\.kimi-code`.
`@moonshot-ai/kimi-code@0.29.0` was installed inside
`.snapshots/kimi-code-t50-smoke` with `npm --prefix ... install --ignore-scripts
--no-save`; the global installer, OAuth, API key, and local data were not
used.

`kimi --version` returned `0.29.0`. `kimi --help` confirmed the
`export`, `provider`, `acp`, `web`, `server`, `login`, `doctor`, `vis`,
`migrate`, and `upgrade` subcommands; it does not publish a usage/quota
subcommand. The test did not create `%USERPROFILE%\.kimi-code`.

## Source classification

| Data | Observed source | TokenUsage status | Decision |
|---|---|---|---|
| Remaining quota | TUI `/usage` and Kimi Code Console | No machine contract or permission to automate | Blocked |
| Tokens and context | TUI `/usage` | No structured output or metric export | Blocked |
| Extra Usage spend | Console and visible balance | No Kimi Code API or export for third parties | Blocked |
| Local data | `sessions`, `wire.jsonl`, `state.json`, logs, and history | Include prompts, responses, paths, commands, and traces | Blocked |
| Kimi Code API key | Inference endpoint | No read-only scope; TokenUsage is not an authorized monitor client | Not allowed |
| Kimi Platform | Official Balance Query API | Separate product, accounts, keys, endpoint, and billing | Out of this provider |

Kimi Platform can be the subject of a separate manual investigation. Its
balance or spend must not be mixed with Kimi Code quota, tokens, or Extra
Usage.

## Privacy and security limit

TokenUsage can detect the presence and version of `kimi` with
`kimi --version`. It must not open, copy, or use:

- `%USERPROFILE%\.kimi-code`, `KIMI_CODE_HOME`, or a scan of their
  subdirectories;
- `config.toml`, `credentials`, `sessions`, `session_index.jsonl`,
  `user-history`, logs, exports, tasks, plans, MCP, skills, or `AGENTS.md`;
- `state.json`, which includes `lastPrompt`, or `agents/*/wire.jsonl`,
  which stores the full communication and request traces;
- OAuth, API keys, `kimi web` bearer tokens, cookies, Console, Kimi
  Platform keys, or login state;
- `kimi web`, ACP, `/usage`, the TUI, or console automation;
- private endpoints, observed traffic, inferred formats, or a modified
  client identity.

`kimi export` and `/export-md` also stay out: the documentation warns that
they can contain code, prompts, commands, paths, and logs.

## Product decision

- Do not create a local reader, remote client, or Kimi Code provider card
  in this phase.
- Do not ask for or store Kimi Code or Kimi Platform credentials for this
  provider.
- Do not automate `/usage`, the CLI, the local web, or the Console.
- Version detection can remain a future diagnostic without a quota, token,
  or spend promise.
- A generic form of values the user types explicitly would need its own
  ticket; it does not form a Kimi Code adapter and is not in Ticket 51.
- Keep Ticket 51 in `needs-info`.
- Reopen only with a read-only API or export, documented for third parties,
  that delivers minimum metrics without sessions or credentials and that
  authorizes automatic queries.

## Independent review

Grok Build reviewed this decision with `Read` and `Grep` only and issued
`accept`. It did not use web, shell, or write. The local review confirmed
that the gate, matrix, and plan match. This review does not replace the
sources in the table above.

## Remaining uncertainty

Kimi can publish a structured `/usage` output, a quota API, or a metric
export. Before announcing support, repeat the gate and test the current
Windows version with an authorized test account.
