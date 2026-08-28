# ZCode source gate

Cutoff date: 2026-07-22

Decision: `block`

## Question

Can TokenUsage integrate ZCode on Windows to show remaining quota, local
usage, or spend without reusing login, credentials, or session content?

## Answer

Not yet. ZCode shows both data groups inside its app: `App Usage` for local
session records and `Coding Plan` for remote Z.ai or BigModel quota and
usage. The reviewed official sources do not publish a third-party read API,
a metric export, or the path and schema of the local records. The policy
confirms that conversations record inputs and generated content, and the
terms forbid extracting data or accessing in an automated way without
authorization.

TokenUsage will not add a ZCode adapter while a suitable public source is
missing. The gate can be reopened with an authorized read-only API or with
a documented, safe local export.

## Identity and support

- Proposed ID for a future descriptor: `zcode`.
- Visible name: `ZCode`.
- Product: desktop app with an integrated ZCode Agent; the documentation
  describes a desktop workspace, tasks, terminal, and review.
- Publisher and service owner: `JINGSHENG HENGXING TECHNOLOGY
  PTE.LTD`.
- Observed version: `3.4.2`, published on 2026-07-22.
- Windows x64: documented install. The page also publishes a Windows ARM64
  download link, although its guide and support sentence detail only x64.
  ARM64 needs a real smoke before it is announced as provider support.
- The reviewed sources do not describe a ZCode executable or CLI contract.
  `/goal`, `/compact`, and custom commands live inside ZCode Agent.

## Primary sources

Consulted on 2026-07-22:

| Source | Supporting fact |
|---|---|
| [ZCode terms](https://zcode.z.ai/en/terms) | Defines the product and the legal provider; requires login and forbids bots, scraping, data extraction, and reverse engineering. |
| [Privacy policy](https://zcode.z.ai/en/privacy) | Identifies the controller; records conversations, inputs, files, code, commands, and generated content. |
| [Install](https://zcode.z.ai/en/docs/install) | Describes the desktop app, Windows x64, and Windows x64/ARM64 links. |
| [Changelog](https://zcode.z.ai/en/changelog) | Publishes ZCode `3.4.2` on 2026-07-22. |
| [ZCode Agent](https://zcode.z.ai/en/docs/agent-framework) | Describes the own agent inside the desktop workspace. |
| [Commands](https://zcode.z.ai/en/docs/commands) | Pins `/goal`, `/compact`, and Markdown commands as agent functions. |
| [Usage Stats](https://zcode.z.ai/en/docs/usage-stats) | Separates local `App Usage` from remote `Coding Plan` and lists their metrics. |
| [Connect Models & Plans](https://zcode.z.ai/en/docs/configuration) | Documents inference endpoints for an API key; not a third-party contract for quota, accumulated usage, or billing. |
| [Feedback & Support](https://zcode.z.ai/en/docs/feedback) | Documents `%USERPROFILE%\.zcode\logs` on Windows for support. |

## Source classification

| Data | Observed source | TokenUsage status | Decision |
|---|---|---|---|
| Remaining quota | `Coding Plan` in the ZCode UI | No public API, export, or third-party permission | Blocked |
| Historical usage | `App Usage` reads local records | Path and schema unpublished; the records can contain session content | Blocked |
| Tokens by model | `App Usage` and `Coding Plan` | No machine contract or documented minimum source | Blocked |
| Spend or billing | Subscription and plan inside the service | No third-party spend API or export | Blocked |
| Manual API key | ZCode allows a key to invoke models | It does not represent permission to read quota or spend | Not allowed |

The OpenAI and Anthropic endpoints described for Z.ai or BigModel serve to
invoke models. TokenUsage will not call them to measure consumption, infer
balance, or probe undocumented paths.

## Privacy and security limit

TokenUsage must not open, index, or use:

- `%USERPROFILE%\.zcode\logs` or a broad scan of `.zcode`;
- `AGENTS.md`, commands, skills, subagents, MCP configuration, or a
  workspace's `.zcode` files;
- conversations, prompts, responses, attachments, files, code, shell
  commands, tool results, tasks, or session history;
- cookies, tokens, login state, ZCode, Z.ai, or BigModel credentials, or
  API keys from another product;
- private endpoints, observed traffic, interface automation, or mechanisms
  inferred from binaries and logs.

The logs path is the only Windows ZCode data path that the reviewed
documentation publishes. Its purpose is support and it can include task
material; it is not a suitable metrics source.

## Product decision

- Do not create code, a descriptor, a local reader, or a remote client for
  ZCode in this phase.
- Do not ask for or store an API key, cookie, token, or ZCode/Z.ai
  credential for this provider.
- Do not announce ZCode quota, usage, spend, or ARM64 support in the public
  UI.
- Keep Ticket 49 in `needs-info`.
- Reopen when ZCode publishes one of these options:
  1. A public read API with endpoint, version, least-privilege
     authentication, account/region scope, and express third-party
     permission.
  2. A local export or file with path, schema, license, and a guarantee
     that it contains only timestamp, model, tokens, and cost, without
     session content or credentials.

## Independent review

Grok Build reviewed this gate in read-only mode. It had access only to this
note, the matrix, the plan, and the README; it did not use web, shell, or
edit permissions. Its verdict was `accept`: the matrix keeps the same
limits and does not turn UI behavior into a third-party contract. The
parent later verified the local diff. This review does not replace the
primary sources in the table above.

## Remaining uncertainty

The visible ARM64 download needs an install test and a safe read before any
claim. ZCode can publish an export, an API, or policy changes after this
cutoff; the gate must be reviewed before a beta that promises support.
