# Pricing evidence and refresh

TokenUsage estimates raw API value only when a local event contains a concrete
model and numeric token counters. It does not treat an API estimate as a
subscription invoice or plan-credit charge.

Every active rate has typed evidence with:

- an official HTTPS source;
- the date that source was reviewed;
- direct-provider or host-specific billing scope;
- an effective start and, when applicable, an explicit inclusive or exclusive end;
- the catalog version and exact price match stored with the event.

Host-specific rates are separate catalog entries. For example, Cursor's Gemini
3.8 Flash output rate does not replace the direct Google API rate.

Run the local audit:

```powershell
tokenusage pricing audit --format human
```

Run the weekly refresh logic without writing files:

```powershell
tokenusage pricing refresh --dry-run
```

The refresh fetches only the URLs compiled into the allowlist. Redirects are
disabled. Each request has a 20-second timeout, a 1 MiB maximum, an allowed text
content type, and a fixed projection of public pricing markers. It stores only
the projection result, never the fetched page.

Unstructured HTML changes create a review item in
[`pricing-refresh.md`](pricing-refresh.md); they do not edit a rate. A promotion
that expires without a versioned successor fails the refresh. The scheduled
workflow writes only a changed report, opens or updates one draft pull request,
and never merges it. A human must verify the official source, make any catalog
or evidence edit, approve the pull request, and merge it.
