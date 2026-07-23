# Ticket 74E3: Vercel key budget UI

Status: implemented and verified with synthetic data in the packaged x64 app.
The authorized real-key smoke remains in Ticket 74F.

## Delivered behavior

- Options accepts an optional raw Vercel API key ID beside the secret API key.
- Local validation rejects a prefixed or malformed ID before connect and keeps
  the action disabled.
- Connect passes the key ID into the existing Credential Locker connection and
  clears both input controls immediately.
- The provider status keeps quota, usage, spend and report coverage separate.
- The dashboard shows the per-key limit, remaining USD, used progress, reset
  cadence and active state without changing account-report coverage.
- Missing ID, missing budget, inactive budget and quota failure use distinct
  localized states. A quota failure leaves a current account report usable.
- English and Spanish resources cover input, validation, status, budget and
  reset text.

## UI proof

The fixture build used `EnableUiTestFixtures=true` and launched through
`winapp run` with `--test-vercel-fake`. It did not run the packaged executable
directly. The fixture reports a 10 USD monthly budget with 6.50 USD remaining.

`tests/ui/ticket-74e3-vercel-quota.ps1` passed 8/8 from a cold tray-only state:

- guarded disconnected state;
- invalid and valid key-ID paths;
- report plus quota composition;
- independent capability rows;
- spend, tokens and budget card;
- disconnect cleanup;
- interactive AutomationIds.

The first automation run waited on a window that the tray app had not opened.
The script now opens Options through the native tray menu when no app window
exists. A second cold-state run passed 8/8.

Reviewed captures:

- `artifacts/ticket-74e3/01-connected-options.png`
- `artifacts/ticket-74e3/02-connected-dashboard.png`
- `artifacts/ticket-74e3/ui-results.json`

The 560 by 1260 flyout keeps the Vercel card inside the existing compact card
layout. The budget progress bar, remaining amount, monthly UTC reset, spend,
tokens and request count fit without horizontal clipping. Color does not carry
the budget state alone; the same state has text and automation values.

## Automated proof

Focused Release/x64 checks:

- `VercelGatewayCardProjectorTests`: 7/7;
- `LocalizationContractTests`: 5/5;
- packaged UIA: 8/8.

Final `scripts/check.ps1 -Platform x64 -Configuration Release`:

- Architecture: 62/62;
- Core: 86/86;
- CLI: 82/82;
- Providers: 254/254;
- Platform Windows: 98/98;
- solution and x64 MSIX package build: passed.

## Review boundary

The WinUI review found no new blocking issue in MVVM boundaries, compiled
bindings, theme resources, localization, input validation or accessibility.
The fixture contains no real Vercel key, account data or quota. Ticket 74F must
run a packaged smoke with an authorized disposable key before public enablement.
