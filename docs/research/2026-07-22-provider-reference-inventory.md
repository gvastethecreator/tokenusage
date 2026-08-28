# Reference provider coverage inventory

Cutoff date: 2026-07-23

Decision: expand the scope with Kilo Code and Zed; keep Kimi Code and Cursor
on their existing gates; open gates for Vercel AI Gateway and Mistral Vibe;
and open a second Windows-first wave without creating adapters yet.

## Question

Which providers appear in the local references and are missing from
TokenUsage scope, and which of them justify a source gate before creating
code?

## Method and limit

Pinned local clones of OpenUsage, CodeBurn, and AgentsView were reviewed.
They are inventory and comparison, not authorization to read sessions,
transcripts, credentials, or internal formats. Every integration decision
uses separate primary sources.

| Reference | Local commit | Use in this review |
|---|---|---|
| `robinebers/openusage` | `9d2bf09f10e21f769494a525a9d65c84d7aeb1df` | Compare the quota provider set. |
| `getagentseal/codeburn` | `6e3c57a9ff95a624f1d9affa7384d32a67f359b7` | Detect local spend providers and paths that need a gate. |
| `kenn-io/agentsview` | `1ee2de88e2dae54326d8b47aeb2de2f58b5944f9` | Contrast agent families and avoid duplicating sources. |

## Request result

| Requested provider | State before this review | Action |
|---|---|---|
| Kilo Code | Absent | Added to M9; Ticket 56 closed the initial gate and Ticket 57 stays in `needs-info`. |
| Kimi Code | Ticket 50 closed as blocked | The gate and Ticket 51 are kept; they are not duplicated. |
| Cursor | Public gate for Teams and Enterprise | Tickets 30, 31, and 44 are kept; Individual stays out of scope. |
| Zed | Absent | Added to M9; Ticket 58 closed the initial gate and Ticket 59 stays in `needs-info`. |

## Reference findings

OpenUsage documents Antigravity, Claude, Codex, Copilot, Cursor, Devin, Grok,
OpenCode, OpenRouter, and Z.ai. All are already in the matrix. CodeBurn adds a
broader list of local spend sources. Among them are Gemini CLI, Kiro, Roo
Code, Goose, Kimi CLI, and Cursor Agent. It also includes Kilo Code, Kimi
Code, Zed, and the providers TokenUsage already had.

AgentsView confirms the presence of Gemini, Kiro, Roo Code, Kilo, Kimi, and
Zed. Overlap between references raises the priority of the first three, but
it does not validate their paths or policy.

Comparing the complete indexes found seven overlaps that were not in scope:
Forge, Hermes Agent, OpenClaw, Pi, Qwen, Warp, and Mistral Vibe. The first
six received Tickets 67–72. Mistral Vibe receives Ticket 75 because it meets
the same two-reference rule. Their local adapters read sessions, so that
overlap does not authorize a scanner.

CodeBurn also records Vercel AI Gateway. Its adapter queries an HTTP report
aggregated by day and model with a manual key. The shape is a better
candidate than a transcript, but the reference does not prove a public
contract, scopes, suitable plans, or permission to use it. Ticket 73 must
resolve those points with primary sources before a client is created.

Many CodeBurn and AgentsView adapters obtain spend from local session data.
That approach can contain prompts, responses, commands, paths, and
credentials. TokenUsage does not adopt those paths only because they appear
in a reference.

The executable CodeBurn registry contains 38 adapters: 27 loaded directly
and 11 under deferred load. Its documentation index lags and omits
Codebuff, Mux, Open Design, Vercel AI Gateway, and Zed. To count coverage,
use `.reference/codeburn/src/providers/index.ts`, not the total declared in
the README. This difference does not change priority: those adapters remain
subject to the same source and privacy gate.

## Next priority

Ticket 60 splits into small gates:

1. Gemini CLI, Ticket 61, for presence in both references and known Windows use.
2. Kiro, Ticket 62, for presence in both references and a distinct CLI/IDE family.
3. Roo Code, Ticket 63, for Cline-family frequency and the need to separate its
   task storage from aggregated usage.
4. Goose, Ticket 64, for presence in CodeBurn and a possible cross-platform local path.
5. Kimi CLI, Ticket 65, because CodeBurn treats it as a source distinct from Kimi Code.
6. Cursor Agent, Ticket 66, because CodeBurn separates it from the Cursor editor.
7. Forge, Ticket 67, present in both references.
8. Hermes Agent, Ticket 68, present in both references.
9. OpenClaw, Ticket 69, present in both references.
10. Pi, Ticket 70, present in both references and related to OMP.
11. Qwen, Ticket 71, present in both references and distinct from the model provider.
12. Warp, Ticket 72, present in both references and with sensitive terminal data.
13. Vercel AI Gateway, Ticket 73, for its aggregated-report candidate with a manual key.
14. Mistral Vibe, Ticket 75, for presence in both references and the need to
    exclude messages, tools, and commands.
15. DeepSeek TUI / CodeWhale, Ticket 77, to resolve whether the inherited paths
    represent a migration or two identities.
16. Windsurf, Ticket 78, for its explicit Windows paths in AgentsView.
17. Trae, Ticket 79, for its Windows variants and the lack of an aggregated source.
18. Aider, Ticket 80, to pin consent, roots, and the privacy limit.
19. OpenHands CLI, Ticket 81, to separate the local agent from remote services.
20. Amp, Ticket 82, as a low-priority gate for a suitable local source.
21. Codebuff, Ticket 83, to evaluate its aggregated accounting without reading chats.
22. Piebald, Ticket 84, for its Windows path and its own local storage.

AgentsView records 53 identities. Aider, Amp, Windsurf, OpenHands CLI,
Zencoder, Trae, Qoder, Cortex Code, DeepSeek TUI, gptme, iFlow, IcodeMate,
MiMoCode, Piebald, Posit Assistant, Positron Assistant, QClaw, QwenPaw,
Reasonix, Shelley, and WorkBuddy remain later candidates. Variants of
Claude, Copilot, Kiro, and Antigravity are resolved inside their families
before opening their own IDs.

CodeBurn keeps Crush, Droid, IBM Bob, LingTai TUI, Mux, Open Design, Quick
Desktop, and Zerostack as later candidates. They receive Tickets 86-93.
OMP stays under the Pi gate. DeepSeek TUI and CodeWhale share candidate
paths and receive a single identity gate before deciding whether they need
one or two providers.

The complete review of `internal/parser/types.go` and the AgentsView
discovery table also found Zencoder, Qoder, Cortex Code, gptme, iFlow,
IcodeMate, MiMoCode, Posit Assistant, Positron Assistant, QClaw, QwenPaw,
Reasonix, Shelley, WorkBuddy, OpenClaude, and Claude Cowork. Tickets 94-109
pin small gates. VS Code Copilot and Visual Studio Copilot stay in the
GitHub Copilot family; Kiro IDE stays under Kiro; Antigravity IDE and CLI
stay under their family; `vibe` corresponds to Mistral Vibe.

AgentsView also contains `ChatGPT` and `Claude.ai` as import sources. They
do not represent a local agent or a measurable quota, so they receive
neither a provider ID nor an integration ticket.

## Product decision

- Kilo Code and Zed are represented in the matrix and M9 with gate status.
- Kimi Code and Cursor stay covered by their current tickets.
- Kimi CLI and Cursor Agent are investigated separately; they do not inherit
  the Kimi Code or Cursor Admin API contracts. AgentsView mixes the Kimi
  paths under one identity, so it does not provide evidence to separate them.
- Forge, Hermes Agent, OpenClaw, Pi, Qwen, and Warp receive their own gates
  because they appear in both references.
- Vercel AI Gateway receives its own gate for its aggregated-report
  candidate; Mistral Vibe receives another because it appears in both
  references.
- DeepSeek/CodeWhale, Windsurf, Trae, Aider, OpenHands, Amp, Codebuff, and
  Piebald receive research gates in Tickets 77–84. A single reference
  pins the study order; it does not authorize support.
- ZooCode enters Ticket 63 as a candidate successor of Roo Code. The
  reference records Roo Code's closure and an active fork, but it does not
  authorize creating a new ID or inheriting paths.
- The remaining candidates receive Tickets 86-109. Ticket 85 first validates
  identity, family, and priority to avoid duplicate IDs.
- No candidate in the next wave receives a descriptor, logo, scanner,
  credential, or card before its source gate.
- Future research must keep agent usage, subscription quota, and model-
  provider spend separate.

## Remaining uncertainty

The references change quickly, and their parsers do not prove terms or
contracts. Before announcing support for any candidate, repeat the gate with
current primary documentation, an isolated Windows test, and authorized
minimum data when needed.

Inventory pointers: `.reference/codeburn/src/providers/index.ts`,
`.reference/codeburn/src/providers/vercel-gateway.ts`,
`.reference/codeburn/src/providers/mistral-vibe.ts`, and
`.reference/agentsview/internal/parser/types.go`.
