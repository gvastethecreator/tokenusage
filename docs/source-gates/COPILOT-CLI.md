# GitHub Copilot CLI local usage gate

Status: `policy-blocked`

Reviewed on 2026-09-03 against Copilot CLI `1.0.82`, the official command
reference, and repository commit
[`be82101e`](https://github.com/github/copilot-cli/tree/be82101e70f0253b57519bebb9cc9d0f6dfb2ed2).
The public repository distributes releases, documentation links, and issue
workflows; it does not publish the CLI implementation needed to verify a
stronger local file contract.

## Result

Copilot CLI documents OpenTelemetry token counters, but the current file
export is not an eligible TokenUsage source. `COPILOT_OTEL_FILE_EXPORTER_PATH`
writes all signals to one JSON-lines file. There is no documented file setting
that writes metrics alone.

Prompt, response, and tool content is disabled by default, but it can be
enabled through `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`. Even with
content disabled, the same file contains session traces with conversation ids,
pseudonymous user ids, response and turn ids, tool names and descriptions,
server addresses, errors, and other fields outside the minimum projection.
TokenUsage cannot establish that every existing or future line was produced
with content capture off before opening the mixed file.

The normal Copilot directory is also ineligible. `~/.copilot/session-state/`
contains resumable conversation event logs and workspace artifacts;
`session-store.db`, logs, command history, settings, MCP data, and credentials
serve other purposes. On Windows the OAuth token normally lives in Credential
Manager under `copilot-cli`. TokenUsage does not open or reuse any of them.

The Windows inspection was metadata-only. A `.copilot` directory exists on the
review machine, but no `copilot` command, file-export setting, enabled OTel
setting, or content-capture setting was present. No Copilot file, Credential
Manager entry, editor token, environment value, or `gh auth` state was read.

## Candidate allowlist

A future metrics-only export could project only:

- resource `service.name=github-copilot` and `service.version`;
- metric `gen_ai.client.token.usage`, unit `tokens`;
- numeric histogram sum/count and point start/end timestamps;
- token type `input` or `output`;
- requested or resolved model when the metric contract publishes it.

It would reject every other resource, span, event, trace, session, user,
response, interaction, turn, server, tool, error, prompt, output, instruction,
path, and identifier field. The source would provide local CLI token usage
only. It would not replace the existing opt-in GitHub Billing REST connection,
infer quota, or turn `github.copilot.cost` span data into an invoice.

## Failure contract

- Missing or disabled telemetry: `NoData`.
- Locked output: keep the last reliable snapshot as stale.
- File over 16 MiB: `SourceTooLarge`; do not partially ingest it.
- Unknown schema, signal, metric, unit, attribute, or token type:
  `UnsupportedSchema`.
- Content-bearing field or non-metric signal: `PolicyBlocked`; persist nothing
  from that read.
- Rotation: reconcile complete files by stable file identity and UTC point
  timestamps; duplicate points are idempotent and gaps stay gaps.

## Re-entry condition

Reopen this gate only when GitHub publishes a metrics-only file/export contract
or a documented per-signal sink that prevents traces, events, content, identity,
and tool metadata from entering the source. A positive gate also needs a
sanitized official-format fixture, bounded parser tests, current Windows smoke,
and explicit human privacy approval before an implementation ticket is created.

Primary sources:

- [Copilot CLI command and OpenTelemetry reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference)
- [Copilot CLI configuration directory](https://docs.github.com/en/enterprise-cloud@latest/copilot/reference/copilot-cli-reference/cli-config-dir-reference)
- [Copilot CLI authentication and Windows credential storage](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/authenticate-copilot-cli)
- [Copilot agent OpenTelemetry privacy model](https://docs.github.com/en/copilot/concepts/agents/opentelemetry)
- [Copilot CLI session data](https://docs.github.com/en/copilot/concepts/agents/copilot-cli/chronicle)
