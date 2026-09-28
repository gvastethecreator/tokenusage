# GitHub Copilot in VS Code local usage gate

Status: `approved`

Approved by the repository owner on 2026-09-28, with the allowlist-only rule
below. Reviewed against VS Code `1.139.1` with its built-in GitHub Copilot Chat
`0.67.0` and upstream commit
[`1f536797`](https://github.com/microsoft/vscode/tree/1f5367975c88d43d6878f0e6e3f7cb1395e35aef).
Copilot Chat now ships inside the VS Code repository under `extensions/copilot`.

## Result

VS Code keeps each chat session in a local file. Next to the conversation, each
request stores numeric usage: tokens and the Copilot credits that the backend
reported. TokenUsage reads that numeric projection and nothing else. The file
also holds prompts, responses, tool calls and results, file references, and
paths. Those values are skipped while parsing and are never materialized,
stored, logged, or shown.

This gate does not change the Copilot Billing REST connection or the Copilot
CLI gate. [Copilot CLI](COPILOT-CLI.md) stays `policy-blocked`. The quota stays
unavailable: VS Code does not cache `quota_snapshots` on disk, and
`/copilot_internal/user` stays forbidden.

## Roots

For each of `Code`, `Code - Insiders`, and `VSCodium` under `%APPDATA%`:

- `<product>\User\workspaceStorage\<hash>\chatSessions\<sessionId>.jsonl`;
- the older `<sessionId>.json` snapshots in the same folder;
- `<product>\User\globalStorage\emptyWindowChatSessions\<sessionId>.jsonl` and `.json`.

Linked folders and linked files are not followed. Files not written during the
reconciliation window (35 days) are skipped.

## Format

A `.jsonl` file is VS Code's operation log
([`objectMutationLog.ts`](https://github.com/microsoft/vscode/blob/1f5367975c88d43d6878f0e6e3f7cb1395e35aef/src/vs/workbench/contrib/chat/common/model/objectMutationLog.ts#L195-L215)).
Each line is one entry:

- `{"kind":0,"v":<state>}` replaces the whole state;
- `{"kind":1,"k":[path],"v":<value>}` sets a value;
- `{"kind":2,"k":[path],"v":[items],"i":<index>}` truncates the array at `i`, when present, then appends;
- `{"kind":3,"k":[path]}` deletes a value.

VS Code compacts the log to one `kind` 0 entry after 512 entries. The reader
replays the entries in order, so the last write wins. A `.json` file is a full
state snapshot with the same shape. Supported state `version` values are 1, 2,
and 3.

## Allowlist

Session: `version`, `sessionId`, `requests`.

Request (`requests[i]`), as serialized by
[`chatSessionOperationLog.ts`](https://github.com/microsoft/vscode/blob/1f5367975c88d43d6878f0e6e3f7cb1395e35aef/src/vs/workbench/contrib/chat/common/model/chatSessionOperationLog.ts#L165-L171):

- `requestId`, `timestamp` (ms), `modelId` (the model picked in the UI);
- `modelState.value` (0 pending, 1 complete, 2 cancelled, 3 failed, 4 needs input) and `modelState.completedAt` (ms);
- `promptTokens`, `completionTokens`, `copilotCredits`;
- `modelTotals[].model`, `.inputTokens`, `.cachedTokens`, `.outputTokens`;
- `result.metadata.resolvedModel`, `.promptTokens`, `.outputTokens`.

Only short identifier strings (at most 256 bytes) are materialized: request id,
session id, and model ids. Every other value is skipped without being decoded.

## Never read

- Message text, responses, rendered prompts, tool calls, tool results, thinking text, code blocks, references, edited files, and custom titles.
- `workspace.json` and any other workspace descriptor or path.
- `GitHub.copilot-chat` extension storage: `session-store.db`, transcripts, debug logs, embeddings, and caches.
- VS Code and extension host logs, `state.vscdb`, settings, and authentication state.
- Copilot CLI files, Credential Manager, `hosts.yml`, and `gh auth`.

## Mapping

One event for each request whose state is complete, cancelled, or failed and
that carries usage. A state without `modelState` counts as complete when a
`result` exists. Pending requests are skipped until they finish.

- Key: SHA-256 of `copilot-vscode\0chat-request-v1\0{sessionId}\0{requestId}`. A session found under more than one root is counted once; the most recently written copy wins.
- Time: `modelState.completedAt`, else `timestamp`. When both exist, the event is an interval that starts at `timestamp`.
- Model: `result.metadata.resolvedModel`, else the only `modelTotals` model, else the picker model without its `copilot/` prefix. The picker model is kept as the observed model.
- Tokens with `modelTotals` (whole-turn totals per model): input is `inputTokens` minus cached tokens, cache read is `cachedTokens` clamped to `inputTokens`, and output is `outputTokens`. All three are measured. VS Code treats cached tokens as a subset of input ([`chatDebugCacheInsights.ts`](https://github.com/microsoft/vscode/blob/1f5367975c88d43d6878f0e6e3f7cb1395e35aef/src/vs/workbench/contrib/chat/browser/chatDebug/chatDebugCacheInsights.ts#L721-L765)).
- Tokens without `modelTotals`: input is `promptTokens` (else `result.metadata.promptTokens`) and has unknown completeness, because it describes only the last model call of the turn. It is a lower bound. Output is `completionTokens`, summed across the turn's calls, and is measured. The older `result.metadata.outputTokens` fallback has unknown completeness.
- Cost: `copilotCredits × $0.01`, reported by the provider, with partial coverage. It is the billed credit value before any allowance included in the plan, not the net charge. A request without credits is unpriced.

## Limits and failure contract

- Files over 16 MiB are not read and mark the read partial.
- An unknown entry `kind` or state `version` gives `UnsupportedSchema` for that file.
- A malformed complete line marks the read partial and is skipped. An unterminated last line is a write in progress and is ignored.
- Code completions and next edit suggestions are not recorded by VS Code. They are also not billed in credits.
- Bring-your-own-key models report no credits.
- Deleted sessions stop being read; stored events stay.
- The format is internal to VS Code and can change without notice.

## Primary sources

- [Serialized request usage fields](https://github.com/microsoft/vscode/blob/1f5367975c88d43d6878f0e6e3f7cb1395e35aef/src/vs/workbench/contrib/chat/common/model/chatSessionOperationLog.ts#L165-L171)
- [Usage semantics (`IChatUsage`, `IChatUsageModelTotal`)](https://github.com/microsoft/vscode/blob/1f5367975c88d43d6878f0e6e3f7cb1395e35aef/src/vs/workbench/contrib/chat/common/chatService/chatService.ts#L166-L215)
- [Credit accumulation per turn](https://github.com/microsoft/vscode/blob/1f5367975c88d43d6878f0e6e3f7cb1395e35aef/extensions/copilot/src/extension/intents/node/toolCallingLoop.ts#L1982-L1996)
- [Usage-based billing for individuals](https://docs.github.com/en/copilot/concepts/billing-and-usage/individuals/billing)
