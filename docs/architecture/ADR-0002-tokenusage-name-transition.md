# ADR-0002: TokenUsage name transition

Status: implemented on 2026-08-04

Date: 2026-07-22

## Decision

The formal and technical product name is `TokenUsage`. The UI, projects, namespaces, assemblies, executables, paths, and tests use this name. Package Identity and AUMID stay stable so the upgrade path is preserved.

## Reason

A name change in the middle of open verticals would mix two identities in the package and in evidence. It would also make upgrades, local data, CLI aliases, and uninstall harder to verify.

## Technical cutover

The 2026-08-04 cutover changed these items as one unit:

- solution, projects, folders, namespaces, and assemblies
- app and CLI executables
- the `tokenusage.exe` alias and JSON contracts `tokenusage.*.v1`
- local tracker paths, scripts, tests, and documentation

The cutover keeps the current Identity, AUMID, and Publisher. Installation, packaged update, domain, logo, and the beta channel still need their own tests or decisions. Ticket 02 keeps those items open.
