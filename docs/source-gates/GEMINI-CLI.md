# Gemini CLI local usage gate

Status: `policy-blocked`

Reviewed on 2026-09-03 against Gemini CLI `0.58.0` and upstream commit
[`55b495d6`](https://github.com/google-gemini/gemini-cli/tree/55b495d6db1794bf5b7f37a9bc03ebcab5103673).

## Result

Gemini CLI has numeric token metrics, but it does not expose them through an
eligible Windows source that TokenUsage can read today.

The official `/stats model` command is an interactive, current-session view.
It has no documented machine output or history export. Local chat recordings
under `%USERPROFILE%\.gemini\tmp\<project>\chats\` are not eligible. The
current record type stores message content, display content, tool arguments,
tool results, session identifiers, directories, and token summaries together.

OpenTelemetry is also ineligible as a file source. `GEMINI_TELEMETRY_OUTFILE`
writes spans, logs, and metrics to the same file. The source constructs all
three exporters for that path. `logPrompts` defaults to `true`, and even the
token metric adds common attributes such as session id, installation id,
email, and authentication type.

The Windows inspection was metadata-only. A `.gemini` directory exists on the
review machine, but no `gemini` command, `tmp` chat directory, telemetry output
setting, or enabled telemetry setting was present. No file below `.gemini` was
opened.

## Candidate allowlist

A future source may accept only a documented metrics-only export with content
signals disabled before they reach the file. The projection would contain:

- metric name `gemini_cli.token.usage`;
- numeric counter value and point timestamp;
- `model`;
- `type`: `input`, `output`, `thought`, `cache`, or `tool`.

It would reject all other metrics and every resource, log, span, event,
session, installation, email, authentication, server, prompt, response, tool,
path, and identifier field. Local usage would remain separate from Google
quota and from direct Gemini API cost.

## Failure contract

- Missing output: `NoData`.
- Locked output: keep the last reliable snapshot as stale.
- File over 16 MiB: `SourceTooLarge`; do not partially ingest it.
- Unknown schema, signal, attribute, or token type: `UnsupportedSchema`.
- Content-bearing field or non-metric signal: `PolicyBlocked`; do not persist
  any event from that read.
- Rotation: reconcile only complete files by a stable file identity and UTC
  point timestamp; never infer missing intervals.

## Re-entry condition

Reopen this gate only when Gemini CLI documents a metrics-only local export or
a per-signal output that never contains logs, traces, prompts, responses,
tools, account data, or session identifiers. A positive gate also needs a
sanitized official-format fixture, bounded parser tests, and explicit human
privacy approval before an implementation ticket is created.

Primary sources:

- [OpenTelemetry configuration and signal contract](https://github.com/google-gemini/gemini-cli/blob/55b495d6db1794bf5b7f37a9bc03ebcab5103673/docs/cli/telemetry.md)
- [All three file exporters share the configured output](https://github.com/google-gemini/gemini-cli/blob/55b495d6db1794bf5b7f37a9bc03ebcab5103673/packages/core/src/telemetry/sdk.ts)
- [Token metric fields](https://github.com/google-gemini/gemini-cli/blob/55b495d6db1794bf5b7f37a9bc03ebcab5103673/packages/core/src/telemetry/metrics.ts)
- [Chat record content fields](https://github.com/google-gemini/gemini-cli/blob/55b495d6db1794bf5b7f37a9bc03ebcab5103673/packages/core/src/services/chatRecordingTypes.ts)
- [Quota and `/stats model`](https://geminicli.com/docs/resources/quota-and-pricing/)
