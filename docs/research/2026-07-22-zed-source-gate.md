# Zed source gate

Cutoff date: 2026-07-22

Decision: `block`

## Question

Can TokenUsage integrate Zed on Windows to show quota, tokens, or spend
without reusing login, credentials, thread content, or interface
automation?

## Answer

Not yet. Zed shows tokens for the active native-agent thread inside the
Agent Panel. External agents and terminal threads keep their own
authentication, and the documentation warns that token availability and
restoration vary by integration.

The official code persists messages, tool results, model, accumulated
usage, and per-request usage in the same `DbThread`. It then compresses the
object and stores it in `threads.db`. A local reader would have to
decompress data that includes prompts, responses, and tools to reach the
counters. That path crosses TokenUsage's privacy limit.

No public aggregated-metrics API, CLI, or export that a third-party app can
query was found.

## Identity and support

- Proposed ID for a future descriptor: `zed`.
- Visible name: `Zed`.
- Evaluated scope: native Zed Agent.
- Outside this descriptor: external ACP agents and Terminal Threads; their
  usage still belongs to the agent or provider they run.
- The `zed` CLI is not installed on this machine; the editor was not
  installed or started during the gate.

## Primary sources

Consulted on 2026-07-22:

| Source | Supporting fact |
|---|---|
| [Agents](https://zed.dev/docs/ai/agents) | Distinguishes Zed Agent, external agents, and Terminal Threads. |
| [Agent Panel](https://zed.dev/docs/ai/agent-panel) | Shows tokens of the active thread and warns about differences for external agents. |
| [API access](https://zed.dev/docs/ai/use-api-access) | Separates Zed Agent model credentials from external and terminal agents. |
| [Thread database code](https://github.com/zed-industries/zed/blob/aba12fc8a0fe44a0742acc0d096e843d07385962/crates/agent/src/db.rs) | SHA consulted during the gate; defines `DbThread`, its messages, and counters; compresses the blob and creates `threads.db`. |

## Minimum Windows test

`Get-Command zed` did not find the CLI on the machine. The test does not
install Zed, create threads, open the panel, or examine user data
directories. The decision rests on the primary sources and the product's
data limit.

## Source classification

| Data | Observed source | TokenUsage status | Decision |
|---|---|---|---|
| Remaining quota | Agent Panel and the model provider | No Zed quota API for third parties | Blocked |
| Zed Agent tokens | Thread indicator and `threads.db` | The store includes transcript and tools | Blocked |
| Cost | Model-provider account or Zed-hosted models | Belongs to the configured provider; no aggregated Zed export | Blocked |
| External agents and terminal | ACP or their own CLI | Must be attributed to the provider that runs them | Out of this provider |
| API keys | Keychain or variables per provider | They are not monitor credentials and are not reused | Not allowed |

## Privacy and security limit

TokenUsage must not:

- open, copy, query, or decompress `threads.db`, its WAL/SHM, or a future
  thread database;
- read messages, summaries, titles, paths, tool results, sandbox grants,
  configuration, settings, keychain, or provider variables;
- automate Agent Panel, Threads Sidebar, Terminal Threads, ACP agents,
  feedback, dashboard, or Markdown export;
- take Anthropic, OpenAI, Google, xAI, OpenCode, or other provider keys
  that Zed uses for a thread;
- add an external agent's usage as native Zed usage.

## Product decision

- Do not create code, a descriptor, a local scanner, a remote client, or a
  Zed card in this phase.
- Keep Ticket 59 in `needs-info`.
- Reopen only when Zed publishes a read-only, aggregated, minimum, and
  third-party-authorized API or export, with clear units and coverage.
- A future integration must separate the native agent from external agents
  and keep cost under the model provider when that applies.

## Independent review

Grok Build reviewed matrix, plan, tickets, and gates with `Read` and `Grep`
only. It confirmed the separation among Zed Agent, external agents, and
Terminal Threads, and did not find an approved path that reads threads or
credentials. The review does not certify external sources or live editor
behavior.

## Remaining uncertainty

Zed can publish account metrics, an aggregated export, or persistence
changes. Before announcing support, repeat the gate with the current
Windows version and an approved fixture that does not contain thread
content.
