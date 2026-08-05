# Ticket 74A: Vercel AI Gateway report client

Date: 2026-07-23
Status: implemented and verified offline

## Delivered

- fixed `GET https://ai-gateway.vercel.sh/v1/report` client;
- inclusive 31-day request cap;
- daily-only query contract for the first cut;
- exact decimal cost and integer token mapping;
- typed auth, plan, throttle, transient and contract failures;
- final-origin check before response parsing;
- no environment, file, vault or foreign-key discovery;
- sanitized errors that do not retain HTTP bodies or inner exception text;
- one MiB response cap, requested-range and duplicate-day checks;
- synthetic contract fixtures and 21 focused tests.

## Review

Grok reviewed Ticket 73 and accepted the source gate with limits. Two Grok
implementation runs then failed on `read_file` before producing output. The
parent discarded the isolated snapshot and wrote this cut locally.

Parent review found that retaining `JsonException`, `HttpRequestException` or
`OperationCanceledException` as inner errors could keep private source text.
The final client maps those failures to fixed messages without inner errors.

## Proof

```text
dotnet test tests\TokenUsage.Providers.Tests\TokenUsage.Providers.Tests.csproj \
  --filter FullyQualifiedName~VercelGatewayReportClientTests --no-restore

Passed: 21, Failed: 0, Skipped: 0
```

```text
dotnet test tests\TokenUsage.Providers.Tests\TokenUsage.Providers.Tests.csproj \
  --no-restore

Passed: 191, Failed: 0, Skipped: 0
```

`dotnet format` ran on the three C# files. It returned success and also printed
a workspace-load warning without a file or diagnostic. The build and both test
runs passed after formatting.

## Remaining gates

- no Credential Locker service exists in this cut;
- no quota endpoint or key-ID form exists yet;
- no runtime, cache or UI is wired yet;
- no network call or real credential was used;
- live Windows and MSIX smoke still needs explicit user authorization.
