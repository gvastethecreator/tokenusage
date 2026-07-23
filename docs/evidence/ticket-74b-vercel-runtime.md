# Ticket 74B: Vercel AI Gateway runtime

Date: 2026-07-23
Status: implemented; focused proof passed

## Delivered

- experimental `vercel-ai-gateway` runtime;
- connection-source seam for the later Credential Locker adapter;
- fixed 30-day UTC report window;
- aggregation of documented cost, token and request metrics;
- provider-reported manual-key provenance;
- empty report without invented zero metrics;
- partial coverage when any daily field is absent;
- typed auth, account, throttle, transient and contract outcomes;
- last-good preservation for throttle, transient and contract failures;
- checked aggregation and safe retry-date clamping;
- cancellation propagation and fixed outcome copy without secret text.
- detection checks credential presence without loading the API key.

## Delegation and review

Grok Build wrote runtime, mapper and tests in an isolated project-local
snapshot. It had no repo reads, shell, network, credentials or subagents.

Parent Sol audit returned `repair` before integration. Repairs:

- matched the real report-exception constructor;
- added `IVercelGatewayReportClient` to the owned report contract;
- removed public message constants added only for tests;
- normalized the runtime clock to UTC;
- fixed analyzer-invalid test names;
- removed a blank-string assertion that could match normal spaces;
- clamped unrepresentable `Retry-After` dates.

Fresh independent review also returned `repair`: dependencies that ignored a
cancelled token could still cause an available, not-configured or successful
result. The runtime now checks cancellation after every external `await`; three
tests cover dependencies that cancel and then return normally.

The same reviewer rechecked the repair and returned `ACCEPT`.

Final parent Sol audit: `accept`. The last autopsy also removed needless key
loading during detection by splitting presence check from credential retrieval.

## Proof

```text
dotnet test tests\WOpenUsage.Providers.Tests\WOpenUsage.Providers.Tests.csproj \
  --filter FullyQualifiedName~VercelGatewayProviderRuntimeTests --no-restore

Passed: 25, Failed: 0, Skipped: 0
```

```text
dotnet test tests\WOpenUsage.Providers.Tests\WOpenUsage.Providers.Tests.csproj \
  --filter FullyQualifiedName~VercelGateway --no-restore

Passed: 46, Failed: 0, Skipped: 0
```

`dotnet format` returned success for the five affected C# files and printed the
same workspace-load warning recorded in 74A without a file or diagnostic.

## Remaining gates

- no Windows Credential Locker adapter exists;
- runtime is not composed into the cache or flyout yet;
- UI source, account scope, report lag and dual-use key warnings remain pending;
- quota-by-key endpoint remains pending;
- no live credential or network request was used.
